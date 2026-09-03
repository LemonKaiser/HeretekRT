using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared._WH40K.MetaProgress;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.MetaProgress;

/// <summary>
///     Серверный источник истины для каталога и выбранных игроком украшений.
///     Все операции одного игрока последовательно выполняются через его состояние.
/// </summary>
public sealed class WH40KDecorationSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;

    private readonly ConcurrentDictionary<NetUserId, PlayerSelectionState> _playerStates = new();
    private DecorationCatalog _catalog = DecorationCatalog.Empty;
    private WH40KDecorationAccessMode _accessMode;
    private readonly long _serverEpoch = DateTime.UtcNow.Ticks;
    private long _catalogRevision;
    private long _stateRevision;

    public event Action<NetUserId>? SelectionChanged;

    public WH40KDecorationAccessMode AccessMode => _accessMode;

    public override void Initialize()
    {
        base.Initialize();
        RebuildCatalog();

        SubscribeNetworkEvent<WH40KDecorationRequestStateEvent>(OnRequestState);
        SubscribeNetworkEvent<WH40KDecorationSetSelectionEvent>(OnSetSelection);
        _userDb.AddOnLoadPlayer(LoadPlayerDataAsync);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnected);
        _admins.OnPermsChanged += OnAdminPermsChanged;
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        _configuration.OnValueChanged(CCVars.Wh40kDecorationsMode, OnAccessModeChanged, true);
    }

    public override void Shutdown()
    {
        _admins.OnPermsChanged -= OnAdminPermsChanged;
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        _configuration.UnsubValueChanged(CCVars.Wh40kDecorationsMode, OnAccessModeChanged);
        base.Shutdown();
    }

    /// <summary>
    ///     Возвращает украшение только после загрузки пользовательского выбора из БД.
    ///     До этого вызывающий код использует штатное представление без мигания default-украшения.
    /// </summary>
    public bool TryGetSelectedDecoration(
        NetUserId userId,
        WH40KMetaDecorationCategory category,
        out WH40KMetaDecorationPrototype? decoration)
    {
        decoration = null;
        if (!_players.TryGetSessionById(userId, out var session) || !CanUseDecorations(session))
            return false;

        if (!TryGetLoadedSelection(userId, out var selection))
            return false;

        var selectedId = GetSelectedId(selection, category);
        if (!string.IsNullOrWhiteSpace(selectedId) &&
            _catalog.TryGet(selectedId, out var selected) &&
            selected.Category == category &&
            CanUseDecoration(session, selected))
        {
            decoration = selected;
            return true;
        }

        decoration = GetDefaultDecoration(category, session);
        return decoration != null;
    }

    public bool CanUseDecorations(ICommonSession session)
    {
        return _accessMode switch
        {
            WH40KDecorationAccessMode.All => true,
            WH40KDecorationAccessMode.Admins => IsDecorationStaff(session),
            _ => false,
        };
    }

    /// <summary>
    ///     Determines whether the player's selected decoration may style the entire OOC or LOOC line.
    ///     The player must first be allowed to use decorations by the primary access mode.
    /// </summary>
    public bool ShouldApplyFullLineEffect(ICommonSession session)
    {
        if (!CanUseDecorations(session))
            return false;

        return WH40KDecorationAccessPolicy.ParseMode(_configuration.GetCVar(CCVars.Wh40kDecorationsFullLineMode)) switch
        {
            WH40KDecorationAccessMode.All => true,
            WH40KDecorationAccessMode.Admins => IsDecorationStaff(session),
            _ => false,
        };
    }

    private bool CanUseDecoration(ICommonSession session, WH40KMetaDecorationPrototype decoration)
        => CanUseDecorations(session) && (!decoration.AdminOnly || IsDecorationStaff(session));

    private void OnRequestState(WH40KDecorationRequestStateEvent ev, EntitySessionEventArgs args)
    {
        _ = HandleRequestStateAsync(args.SenderSession);
    }

    private void OnSetSelection(WH40KDecorationSetSelectionEvent ev, EntitySessionEventArgs args)
    {
        _ = HandleSetSelectionAsync(ev, args.SenderSession);
    }

    private async Task HandleRequestStateAsync(ICommonSession session)
    {
        try
        {
            var state = GetOrCreatePlayerState(session.UserId);
            if (!await EnsureSelectionLoadedAsync(session, state))
                return;

            PushState(session, state);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to send WH40K decorations to {session.UserId}: {exception}");
        }
    }

    private async Task HandleSetSelectionAsync(WH40KDecorationSetSelectionEvent ev, ICommonSession session)
    {
        var decorationId = ev.DecorationId?.Trim() ?? string.Empty;
        if (decorationId.Length == 0 || decorationId.Length > 128 ||
            !_catalog.TryGet(decorationId, out var decoration) ||
            decoration.Category != ev.Category ||
            !CanUseDecoration(session, decoration))
        {
            PushState(session, GetOrCreatePlayerState(session.UserId), ev.RequestId);
            return;
        }

        var state = GetOrCreatePlayerState(session.UserId);
        var changed = false;
        try
        {
            await state.Gate.WaitAsync();
            try
            {
                if (!IsCurrentState(session.UserId, state))
                    return;

                if (!await LoadSelectionUnderGateAsync(session.UserId, state))
                    return;

                var previous = state.Selection!;
                var selection = previous.WithSelection(ev.Category, decorationId);
                if (selection.Equals(previous))
                {
                    PushState(session, state, ev.RequestId);
                    return;
                }

                state.Selection = selection;
                try
                {
                    await _db.SetWh40kDecorationSelectionAsync(session.UserId, ToData(selection));
                    changed = true;
                }
                catch (Exception exception)
                {
                    state.Selection = previous;
                    Log.Error($"Failed to save WH40K decorations for {session.UserId}: {exception}");
                }
            }
            finally
            {
                state.Gate.Release();
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to change WH40K decorations for {session.UserId}: {exception}");
        }

        if (!IsCurrentState(session.UserId, state))
            return;

        PushState(session, state, ev.RequestId);
        if (changed)
            SelectionChanged?.Invoke(session.UserId);
    }

    private async Task LoadPlayerDataAsync(ICommonSession session, CancellationToken cancel)
    {
        var state = GetOrCreatePlayerState(session.UserId);
        var loaded = await EnsureSelectionLoadedAsync(session, state, cancel);
        cancel.ThrowIfCancellationRequested();

        if (!loaded || !IsCurrentState(session.UserId, state))
            return;

        // Ghost may already exist by the time the user data manager completes its callback.
        PushState(session, state);
        SelectionChanged?.Invoke(session.UserId);
    }

    private async Task<bool> EnsureSelectionLoadedAsync(
        ICommonSession session,
        PlayerSelectionState state,
        CancellationToken cancel = default)
    {
        await state.Gate.WaitAsync(cancel);
        try
        {
            if (!IsCurrentState(session.UserId, state))
                return false;

            return await LoadSelectionUnderGateAsync(session.UserId, state, cancel);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task<bool> LoadSelectionUnderGateAsync(
        NetUserId userId,
        PlayerSelectionState state,
        CancellationToken cancel = default)
    {
        if (state.Selection != null)
            return true;

        try
        {
            var stored = await _db.GetWh40kDecorationSelectionAsync(userId, cancel);
            cancel.ThrowIfCancellationRequested();
            if (!IsCurrentState(userId, state))
                return false;

            state.Selection = NormalizeSelection(stored == null
                ? GetDefaultSelection()
                : new WH40KDecorationSelection(
                    stored.SelectedGhostSkinId,
                    stored.SelectedOocTitleId,
                    stored.SelectedOocNameColorId));
            return true;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to load WH40K decorations for {userId}: {exception}");
            if (IsCurrentState(userId, state))
                state.Selection = GetDefaultSelection();
            return IsCurrentState(userId, state);
        }
    }

    private void OnPlayerDisconnected(ICommonSession session)
    {
        if (_playerStates.TryRemove(session.UserId, out var state))
            state.Retire();
    }

    private void OnAccessModeChanged(string value)
    {
        var accessMode = WH40KDecorationAccessPolicy.ParseMode(value);
        if (_accessMode == accessMode)
            return;

        _accessMode = accessMode;
        foreach (var session in _players.Sessions)
            RefreshSession(session);
    }

    private void OnAdminPermsChanged(AdminPermsChangedEventArgs args)
    {
        RefreshSession(args.Player);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<WH40KMetaDecorationPrototype>())
            return;

        RebuildCatalog();
        foreach (var session in _players.Sessions)
        {
            if (_playerStates.TryGetValue(session.UserId, out var state) && state.Selection != null)
            {
                var normalized = NormalizeSelection(state.Selection);
                if (!normalized.Equals(state.Selection))
                    state.Selection = normalized;
            }

            RefreshSession(session);
        }
    }

    private void RefreshSession(ICommonSession session)
    {
        PushState(session, GetOrCreatePlayerState(session.UserId));
        SelectionChanged?.Invoke(session.UserId);
    }

    private void PushState(ICommonSession session, PlayerSelectionState state, long acknowledgedSelectionRequestId = 0)
    {
        if (!IsCurrentState(session.UserId, state) ||
            !_players.TryGetSessionById(session.UserId, out var current) ||
            !ReferenceEquals(current, session))
        {
            return;
        }

        var entries = CanUseDecorations(session)
            ? _catalog.Entries.Where(entry =>
                _catalog.TryGet(entry.Id, out var prototype) && CanUseDecoration(session, prototype)).ToArray()
            : [];

        var selection = state.Selection == null
            ? WH40KDecorationSelection.Empty
            : GetDisplaySelection(session, state.Selection);
        var revision = Interlocked.Increment(ref _stateRevision);
        RaiseNetworkEvent(new WH40KDecorationStateEvent(
            new WH40KDecorationState(
                _serverEpoch,
                revision,
                _catalogRevision,
                acknowledgedSelectionRequestId,
                entries,
                selection)), session);
    }

    private WH40KDecorationSelection GetDisplaySelection(ICommonSession session, WH40KDecorationSelection selection)
    {
        if (!CanUseDecorations(session))
            return WH40KDecorationSelection.Empty;

        return new WH40KDecorationSelection(
            GetAccessibleSelectionId(session, selection.SelectedGhostSkinId, WH40KMetaDecorationCategory.GhostSkins),
            GetAccessibleSelectionId(session, selection.SelectedOocTitleId, WH40KMetaDecorationCategory.OocTitles),
            GetAccessibleSelectionId(session, selection.SelectedOocNameColorId, WH40KMetaDecorationCategory.OocNameColors));
    }

    private string GetAccessibleSelectionId(ICommonSession session, string id, WH40KMetaDecorationCategory category)
    {
        if (IsValidSelection(id, category) &&
            _catalog.TryGet(id, out var decoration) &&
            CanUseDecoration(session, decoration))
        {
            return id;
        }

        return GetDefaultDecoration(category, session)?.ID ?? string.Empty;
    }

    private WH40KDecorationSelection NormalizeSelection(WH40KDecorationSelection selection)
    {
        return new WH40KDecorationSelection(
            IsValidSelection(selection.SelectedGhostSkinId, WH40KMetaDecorationCategory.GhostSkins)
                ? selection.SelectedGhostSkinId
                : GetDefaultDecoration(WH40KMetaDecorationCategory.GhostSkins)?.ID ?? string.Empty,
            IsValidSelection(selection.SelectedOocTitleId, WH40KMetaDecorationCategory.OocTitles)
                ? selection.SelectedOocTitleId
                : GetDefaultDecoration(WH40KMetaDecorationCategory.OocTitles)?.ID ?? string.Empty,
            IsValidSelection(selection.SelectedOocNameColorId, WH40KMetaDecorationCategory.OocNameColors)
                ? selection.SelectedOocNameColorId
                : GetDefaultDecoration(WH40KMetaDecorationCategory.OocNameColors)?.ID ?? string.Empty);
    }

    private bool IsValidSelection(string id, WH40KMetaDecorationCategory category)
        => !string.IsNullOrWhiteSpace(id) &&
           _catalog.TryGet(id, out var prototype) &&
           prototype.Category == category;

    private WH40KDecorationSelection GetDefaultSelection()
    {
        return new WH40KDecorationSelection(
            GetDefaultDecoration(WH40KMetaDecorationCategory.GhostSkins)?.ID ?? string.Empty,
            GetDefaultDecoration(WH40KMetaDecorationCategory.OocTitles)?.ID ?? string.Empty,
            GetDefaultDecoration(WH40KMetaDecorationCategory.OocNameColors)?.ID ?? string.Empty);
    }

    private WH40KMetaDecorationPrototype? GetDefaultDecoration(
        WH40KMetaDecorationCategory category,
        ICommonSession? session = null)
    {
        foreach (var prototype in _catalog.GetCategory(category))
        {
            if (session == null || CanUseDecoration(session, prototype))
                return prototype;
        }

        return null;
    }

    private bool IsDecorationStaff(ICommonSession session)
    {
        return _admins.HasAdminFlag(session, AdminFlags.Admin) ||
               _admins.HasAdminFlag(session, AdminFlags.Moderator);
    }

    private bool TryGetLoadedSelection(NetUserId userId, out WH40KDecorationSelection selection)
    {
        if (_playerStates.TryGetValue(userId, out var state) && !state.IsRetired && state.Selection != null)
        {
            selection = state.Selection;
            return true;
        }

        selection = WH40KDecorationSelection.Empty;
        return false;
    }

    private PlayerSelectionState GetOrCreatePlayerState(NetUserId userId)
        => _playerStates.GetOrAdd(userId, _ => new PlayerSelectionState());

    private bool IsCurrentState(NetUserId userId, PlayerSelectionState state)
        => !state.IsRetired && _playerStates.TryGetValue(userId, out var current) && ReferenceEquals(current, state);

    private void RebuildCatalog()
    {
        var prototypes = new List<WH40KMetaDecorationPrototype>();
        foreach (var prototype in _prototypes.EnumeratePrototypes<WH40KMetaDecorationPrototype>())
        {
            if (!ValidatePrototype(prototype))
                continue;

            prototypes.Add(prototype);
        }

        foreach (var category in Enum.GetValues<WH40KMetaDecorationCategory>())
        {
            var defaults = prototypes.Count(prototype => prototype.Category == category && prototype.DefaultSelected);
            if (defaults == 0)
                Log.Warning($"WH40K decoration category '{category}' has no explicit default; the first sorted entry is used.");
            else if (defaults > 1)
                Log.Warning($"WH40K decoration category '{category}' has {defaults} explicit defaults; the first sorted entry is used.");
        }

        _catalog = new DecorationCatalog(prototypes);
        _catalogRevision++;
    }

    private bool ValidatePrototype(WH40KMetaDecorationPrototype prototype)
    {
        if (!Enum.IsDefined(prototype.Category) || string.IsNullOrWhiteSpace(prototype.ID) ||
            prototype.ID.Length > 128 || string.IsNullOrWhiteSpace(prototype.TitleKey))
        {
            Log.Error($"Ignoring malformed WH40K decoration prototype '{prototype.ID}'.");
            return false;
        }

        if (prototype.OocGradientColors.Count > WH40KDecorationMarkup.MaxPaletteColors)
        {
            Log.Warning($"WH40K decoration '{prototype.ID}' has more than {WH40KDecorationMarkup.MaxPaletteColors} gradient colors; excess colors are ignored.");
        }

        if (!string.IsNullOrWhiteSpace(prototype.OocTitleEffect) &&
            string.IsNullOrWhiteSpace(WH40KDecorationMarkup.NormalizeTitleEffect(prototype.OocTitleEffect)))
        {
            Log.Error($"Ignoring WH40K title decoration '{prototype.ID}' with unknown effect '{prototype.OocTitleEffect}'.");
            return false;
        }

        var palette = WH40KDecorationMarkup.BuildPalette(prototype.OocGradientColors, prototype.OocColorHex);
        if (prototype.Category == WH40KMetaDecorationCategory.OocNameColors &&
            palette.Count == 0 &&
            !(prototype.OocAuraRadius > 0 && prototype.OocAuraAlphaPercent > 0 &&
              WH40KDecorationMarkup.TryResolveColor(prototype.OocAuraHex, out _)))
        {
            Log.Error($"Ignoring WH40K name-color decoration '{prototype.ID}' without a valid color or aura.");
            return false;
        }

        if (prototype.Category == WH40KMetaDecorationCategory.GhostSkins &&
            (string.IsNullOrWhiteSpace(prototype.GhostRsiPath) ||
             string.IsNullOrWhiteSpace(prototype.GhostState) ||
             !prototype.GhostRsiPath.StartsWith("/Textures/", StringComparison.Ordinal) ||
             !prototype.GhostRsiPath.EndsWith(".rsi", StringComparison.OrdinalIgnoreCase) ||
             !WH40KDecorationMarkup.TryResolveColor(prototype.GhostTintHex, out _)))
        {
            Log.Error($"Ignoring malformed WH40K ghost decoration '{prototype.ID}'.");
            return false;
        }

        return true;
    }

    private static string GetSelectedId(WH40KDecorationSelection selection, WH40KMetaDecorationCategory category)
    {
        return category switch
        {
            WH40KMetaDecorationCategory.GhostSkins => selection.SelectedGhostSkinId,
            WH40KMetaDecorationCategory.OocTitles => selection.SelectedOocTitleId,
            WH40KMetaDecorationCategory.OocNameColors => selection.SelectedOocNameColorId,
            _ => string.Empty,
        };
    }

    private static Wh40kDecorationSelectionData ToData(WH40KDecorationSelection selection)
    {
        return new Wh40kDecorationSelectionData(
            selection.SelectedGhostSkinId,
            selection.SelectedOocTitleId,
            selection.SelectedOocNameColorId);
    }

    private static WH40KDecorationEntry CreateEntry(WH40KMetaDecorationPrototype prototype)
    {
        return new WH40KDecorationEntry(
            prototype.ID,
            prototype.Category,
            prototype.TitleKey,
            prototype.PreviewKey,
            prototype.OocColorHex,
            prototype.OocGradientColors.Take(WH40KDecorationMarkup.MaxPaletteColors).ToArray(),
            prototype.OocGradientAnimated,
            prototype.OocGradientDurationMs,
            prototype.OocAuraHex,
            prototype.OocAuraRadius,
            prototype.OocAuraAlphaPercent,
            prototype.OocTitleEffect,
            prototype.OocTitleEffectRevealMs,
            prototype.OocTitleEffectHoldMs,
            prototype.OocTitleEffectDissolveMs,
            prototype.OocTitleOutlineHex,
            prototype.OocTitleOutlineWidth,
            prototype.OocTitleOutlineAlphaPercent,
            prototype.GhostRsiPath,
            prototype.GhostState,
            prototype.GhostTintHex,
            prototype.SortOrder,
            prototype.SuppressTitlePrefix);
    }

    private sealed class PlayerSelectionState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public WH40KDecorationSelection? Selection;
        private int _retired;

        public bool IsRetired => Volatile.Read(ref _retired) != 0;

        public void Retire()
        {
            Interlocked.Exchange(ref _retired, 1);
        }
    }

    private sealed class DecorationCatalog
    {
        public static readonly DecorationCatalog Empty = new([]);

        private readonly Dictionary<string, WH40KMetaDecorationPrototype> _byId;
        private readonly Dictionary<WH40KMetaDecorationCategory, WH40KMetaDecorationPrototype[]> _byCategory;
        public readonly WH40KDecorationEntry[] Entries;

        public DecorationCatalog(IEnumerable<WH40KMetaDecorationPrototype> prototypes)
        {
            _byId = new Dictionary<string, WH40KMetaDecorationPrototype>(StringComparer.Ordinal);
            var categories = new Dictionary<WH40KMetaDecorationCategory, List<WH40KMetaDecorationPrototype>>();

            foreach (var prototype in prototypes)
            {
                _byId[prototype.ID] = prototype;
                if (!categories.TryGetValue(prototype.Category, out var items))
                {
                    items = new List<WH40KMetaDecorationPrototype>();
                    categories.Add(prototype.Category, items);
                }

                items.Add(prototype);
            }

            _byCategory = new Dictionary<WH40KMetaDecorationCategory, WH40KMetaDecorationPrototype[]>();
            foreach (var (category, items) in categories)
            {
                _byCategory[category] = items
                    .OrderByDescending(prototype => prototype.DefaultSelected)
                    .ThenBy(prototype => prototype.SortOrder)
                    .ThenBy(prototype => prototype.ID, StringComparer.Ordinal)
                    .ToArray();
            }

            Entries = _byCategory
                .OrderBy(pair => pair.Key)
                .SelectMany(pair => pair.Value)
                .Select(CreateEntry)
                .ToArray();
        }

        public bool TryGet(string id, out WH40KMetaDecorationPrototype prototype)
            => _byId.TryGetValue(id, out prototype!);

        public IEnumerable<WH40KMetaDecorationPrototype> GetCategory(WH40KMetaDecorationCategory category)
            => _byCategory.TryGetValue(category, out var entries) ? entries : [];
    }
}
