using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server._WH40K.CharacterCreation;
using Content.Server._WH40K.Progression;
using Content.Shared.CCVar;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Preferences.Managers
{
    /// <summary>
    /// Sends <see cref="MsgPreferencesAndSettings"/> before the client joins the lobby.
    /// Receives <see cref="MsgSelectCharacter"/> and <see cref="MsgUpdateCharacter"/> at any time.
    /// </summary>
    public sealed partial class ServerPreferencesManager : IServerPreferencesManager, IPostInjectInit
    {
        [Dependency] private IServerNetManager _netManager = default!;
        [Dependency] private IConfigurationManager _cfg = default!;
        [Dependency] private IServerDbManager _db = default!;
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private IDependencyCollection _dependencies = default!;
        [Dependency] private IPrototypeManager _protos = default!;
        [Dependency] private ILogManager _log = default!;
        [Dependency] private UserDbDataManager _userDb = default!;
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private Wh40kPlayerProgressManager _wh40kOnboarding = default!;
        [Dependency] private Wh40kAccountRpgManager _wh40kRpg = default!;
        [Dependency] private Wh40kProgressManager _wh40kRpgProgress = default!;
        [Dependency] private Wh40kPartyManager _wh40kParties = default!;

        // Cache player prefs on the server so we don't need as much async hell related to them.
        private readonly Dictionary<NetUserId, PlayerPrefData> _cachedPlayerPrefs =
            new();

        // Completing onboarding writes both the profile and progress row. Keep one request per account in
        // flight so a duplicated or delayed client message cannot race the same temporary slot.
        private readonly HashSet<NetUserId> _wh40kOnboardingCompletions = new();
        private readonly object _wh40kOnboardingCompletionsLock = new();

        private ISawmill _sawmill = default!;

        private int MaxCharacterSlots => _cfg.GetCVar(CCVars.GameMaxCharacterSlots);

        public void Init()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>(HandleSelectCharacterMessage);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>(HandleUpdateCharacterMessage);
            _netManager.RegisterNetMessage<MsgDeleteCharacter>(HandleDeleteCharacterMessage);
            _netManager.RegisterNetMessage<MsgCompleteWh40kOnboarding>(HandleCompleteWh40kOnboardingMessage);
            _netManager.RegisterNetMessage<MsgWh40kOnboardingCompleted>();
            _sawmill = _log.GetSawmill("prefs");
        }

        private async void HandleSelectCharacterMessage(MsgSelectCharacter message)
        {
            var index = message.SelectedCharacterIndex;
            var userId = message.MsgChannel.UserId;

            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                Logger.WarningS("prefs", $"User {userId} tried to modify preferences before they loaded.");
                return;
            }

            if (index < 0 || index >= MaxCharacterSlots)
            {
                return;
            }

            var curPrefs = prefsData.Prefs!;

            var progress = _wh40kOnboarding.Get(userId);
            if (!progress.CanUseLegacyPersonalization &&
                index != progress.OnboardingProfileSlot)
            {
                _sawmill.Warning($"User {userId} tried to select character slot {index} before completing WH40K onboarding.");
                return;
            }

            if (!curPrefs.Characters.ContainsKey(index))
            {
                // Non-existent slot.
                return;
            }

            prefsData.Prefs = new PlayerPreferences(curPrefs.Characters, index, curPrefs.AdminOOCColor);

            if (ShouldStorePrefs(message.MsgChannel.AuthType))
            {
                await _db.SaveSelectedCharacterIndexAsync(message.MsgChannel.UserId, message.SelectedCharacterIndex);
            }
        }

        private async void HandleUpdateCharacterMessage(MsgUpdateCharacter message)
        {
            var userId = message.MsgChannel.UserId;

            if (IsWh40kOnboardingRequired(userId))
            {
                _sawmill.Warning($"User {userId} tried to update character slot {message.Slot} before completing WH40K onboarding.");
                return;
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (message.Profile == null)
                _sawmill.Error($"User {userId} sent a {nameof(MsgUpdateCharacter)} with a null profile in slot {message.Slot}.");
            else
                await SetProfile(userId, message.Slot, message.Profile, false);
        }

        private async void HandleCompleteWh40kOnboardingMessage(MsgCompleteWh40kOnboarding message)
        {
            var userId = message.MsgChannel.UserId;
            var result = new Wh40kOnboardingCompletionResult(
                Wh40kOnboardingCompletionStatus.NotAllowed,
                _wh40kOnboarding.Get(userId),
                -1);
            HumanoidCharacterProfile? confirmedProfile = null;

            lock (_wh40kOnboardingCompletionsLock)
            {
                if (!_wh40kOnboardingCompletions.Add(userId))
                {
                    _sawmill.Warning($"User {userId} sent a duplicate WH40K onboarding completion while the previous save is still running.");
                    SendWh40kOnboardingCompletion(message.MsgChannel, result, null);
                    return;
                }
            }

            try
            {
                if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) ||
                    !prefsData.PrefsLoaded ||
                    !ShouldStorePrefs(message.MsgChannel.AuthType))
                {
                    SendWh40kOnboardingCompletion(message.MsgChannel, result, null);
                    return;
                }

                var progress = _wh40kOnboarding.Get(userId);
                if (progress.ActStage != Wh40kActStage.Act1NotStarted ||
                    progress.OnboardingStatus != Wh40kOnboardingStatus.Required ||
                    !prefsData.Prefs!.Characters.TryGetValue(progress.OnboardingProfileSlot, out var existing) ||
                    existing is not HumanoidCharacterProfile existingHumanoid)
                {
                    SendWh40kOnboardingCompletion(message.MsgChannel, result, null);
                    return;
                }

                if (message.Profile is null)
                {
                    result = new Wh40kOnboardingCompletionResult(
                        Wh40kOnboardingCompletionStatus.InvalidBuild,
                        progress,
                        -1);
                    SendWh40kOnboardingCompletion(message.MsgChannel, result, null);
                    return;
                }

                if (!TryPrepareWh40kOnboardingProfile(message.Profile, existingHumanoid, out var preparedProfile, out var status))
                {
                    result = new Wh40kOnboardingCompletionResult(status, progress, -1);
                    SendWh40kOnboardingCompletion(message.MsgChannel, result, null);
                    return;
                }

                preparedProfile.EnsureValid(_playerManager.GetSessionById(userId), _dependencies);
                result = await _db.CompleteWh40kOnboardingAsync(userId, preparedProfile);
                if (result.IsSuccess)
                {
                    _wh40kRpg.CacheCompletedOnboarding(userId, preparedProfile.Wh40kBuild);
                    await _wh40kRpgProgress.LoadAsync(userId);
                    var profiles = new Dictionary<int, ICharacterProfile>(prefsData.Prefs.Characters)
                    {
                        [result.ProfileSlot] = preparedProfile,
                    };
                    prefsData.Prefs = new PlayerPreferences(profiles, result.ProfileSlot, prefsData.Prefs.AdminOOCColor);
                    _wh40kOnboarding.SetTransient(userId, result.Progress);
                    confirmedProfile = preparedProfile;
                }
            }
            catch (Exception exception)
            {
                _sawmill.Error($"Failed to complete WH40K onboarding for {userId}: {exception}");
                result = new Wh40kOnboardingCompletionResult(
                    Wh40kOnboardingCompletionStatus.PersistenceFailed,
                    _wh40kOnboarding.Get(userId),
                    -1);
            }
            finally
            {
                lock (_wh40kOnboardingCompletionsLock)
                {
                    _wh40kOnboardingCompletions.Remove(userId);
                }
            }

            SendWh40kOnboardingCompletion(message.MsgChannel, result, confirmedProfile);
        }

        private bool TryPrepareWh40kOnboardingProfile(
            HumanoidCharacterProfile submitted,
            HumanoidCharacterProfile existing,
            out HumanoidCharacterProfile prepared,
            out Wh40kOnboardingCompletionStatus status)
        {
            var submittedBuild = submitted.Wh40kBuild;
            var build = submittedBuild.Validated();
            if (!build.Equals(submittedBuild) ||
                !build.IsCompleteFoundation ||
                build.HomeworldId is null ||
                build.OriginId is null ||
                build.ClassId is null ||
                build.PortraitId is null ||
                !_protos.TryIndex<Wh40kHomeworldPrototype>(build.HomeworldId, out _) ||
                !_protos.TryIndex<Wh40kOriginPrototype>(build.OriginId, out _) ||
                !_protos.TryIndex<Wh40kCharacterClassPrototype>(build.ClassId, out _) ||
                !_protos.TryIndex<Wh40kPortraitPrototype>(build.PortraitId, out _))
            {
                prepared = default!;
                status = Wh40kOnboardingCompletionStatus.InvalidBuild;
                return false;
            }

            if (!ValidateWh40kOnboardingTraits(submitted.TraitPreferences, submitted.Species))
            {
                prepared = default!;
                status = Wh40kOnboardingCompletionStatus.InvalidBuild;
                return false;
            }

            prepared = existing
                .WithName(submitted.Name)
                .WithFlavorText(submitted.FlavorText)
                .WithAge(submitted.Age)
                .WithSex(submitted.Sex)
                .WithGender(submitted.Gender)
                .WithSpecies(submitted.Species)
                .WithCharacterAppearance(submitted.Appearance.Clone())
                .WithSpawnPriorityPreference(submitted.SpawnPriority)
                .WithTraitPreferences(submitted.TraitPreferences)
                .WithWh40kCharacterBuild(build);
            status = Wh40kOnboardingCompletionStatus.Success;
            return true;
        }

        /// <summary>
        /// The onboarding only exposes Physical traits. The normal profile sanitizer deliberately preserves a
        /// wider set of preferences, so this separate check is required at the untrusted network boundary.
        /// </summary>
        private bool ValidateWh40kOnboardingTraits(
            IReadOnlySet<ProtoId<TraitPrototype>> traits,
            ProtoId<SpeciesPrototype> species)
        {
            const string physicalCategory = "Physical";
            var selected = new List<TraitPrototype>(traits.Count);
            TraitCategoryPrototype? category = null;

            foreach (var traitId in traits)
            {
                if (!_protos.TryIndex<TraitPrototype>(traitId, out var trait) ||
                    trait.Category is not { } traitCategoryId ||
                    traitCategoryId.Id != physicalCategory ||
                    trait.Cost == 0 ||
                    trait.SpeciesBlacklist.Contains(species))
                {
                    return false;
                }

                if (!_protos.TryIndex<TraitCategoryPrototype>(traitCategoryId, out category) ||
                    category.MaxTraitPoints is null || category.MaxTraitPoints < 0)
                {
                    return false;
                }

                selected.Add(trait);
            }

            if (category is not null && selected.Sum(trait => trait.Cost) > category.MaxTraitPoints)
                return false;

            for (var current = 0; current < selected.Count; current++)
            {
                for (var other = current + 1; other < selected.Count; other++)
                {
                    if (selected[current].MutuallyExclusiveTraits.Contains(selected[other].ID) ||
                        selected[other].MutuallyExclusiveTraits.Contains(selected[current].ID))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void SendWh40kOnboardingCompletion(
            INetChannel channel,
            Wh40kOnboardingCompletionResult result,
            HumanoidCharacterProfile? profile)
        {
            _netManager.ServerSendMessage(new MsgWh40kOnboardingCompleted
            {
                Status = result.Status,
                Progress = result.Progress,
                ProfileSlot = result.ProfileSlot,
                Profile = profile,
            }, channel);
        }

        public async Task SetProfile(NetUserId userId, int slot, ICharacterProfile profile,
            bool authoritative = true) // Mono
        {
            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                _sawmill.Error($"Tried to modify user {userId} preferences before they loaded.");
                return;
            }

            if (slot < 0 || slot >= MaxCharacterSlots)
                return;

            var curPrefs = prefsData.Prefs!;
            var session = _playerManager.GetSessionById(userId);

            profile.EnsureValid(session, _dependencies);
            // Mono
            if (!authoritative && profile is HumanoidCharacterProfile humanoid)
            {
                if (curPrefs.Characters.TryGetValue(slot, out var oldProfile) && oldProfile is HumanoidCharacterProfile oldHumanoid)
                {
                    profile = humanoid
                        .WithBankBalance(oldHumanoid.BankBalance)
                        .WithWh40kCharacterBuild(oldHumanoid.Wh40kBuild);
                }
                else
                {
                    profile = humanoid
                        .WithBankBalance(HumanoidCharacterProfile.DefaultBalance)
                        .WithWh40kCharacterBuild(new Wh40kCharacterBuild());
                }
            }

            if (profile is HumanoidCharacterProfile foundationProfile &&
                _wh40kRpg.TryGetFoundationBuild(userId, out var foundationBuild))
            {
                profile = foundationProfile.WithWh40kCharacterBuild(foundationBuild);
            }

            var profiles = new Dictionary<int, ICharacterProfile>(curPrefs.Characters)

            {
                [slot] = profile
            };

            prefsData.Prefs = new PlayerPreferences(profiles, slot, curPrefs.AdminOOCColor);

            if (ShouldStorePrefs(session.Channel.AuthType))
                await _db.SaveCharacterSlotAsync(userId, profile, slot);
        }

        private async void HandleDeleteCharacterMessage(MsgDeleteCharacter message)
        {
            var slot = message.Slot;
            var userId = message.MsgChannel.UserId;

            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                Logger.WarningS("prefs", $"User {userId} tried to modify preferences before they loaded.");
                return;
            }

            if (slot < 0 || slot >= MaxCharacterSlots)
            {
                return;
            }

            var curPrefs = prefsData.Prefs!;

            if (IsWh40kOnboardingRequired(userId))
            {
                _sawmill.Warning($"User {userId} tried to delete character slot {slot} before completing WH40K onboarding.");
                return;
            }

            // If they try to delete the slot they have selected then we switch to another one.
            // Of course, that's only if they HAVE another slot.
            int? nextSlot = null;
            if (curPrefs.SelectedCharacterIndex == slot)
            {
                // That ! on the end is because Rider doesn't like .NET 5.
                var (ns, profile) = curPrefs.Characters.FirstOrDefault(p => p.Key != message.Slot)!;
                if (profile == null)
                {
                    // Only slot left, can't delete.
                    return;
                }

                nextSlot = ns;
            }

            var arr = new Dictionary<int, ICharacterProfile>(curPrefs.Characters);
            arr.Remove(slot);

            prefsData.Prefs = new PlayerPreferences(arr, nextSlot ?? curPrefs.SelectedCharacterIndex, curPrefs.AdminOOCColor);

            if (ShouldStorePrefs(message.MsgChannel.AuthType))
            {
                if (nextSlot != null)
                {
                    await _db.DeleteSlotAndSetSelectedIndex(userId, slot, nextSlot.Value);
                }
                else
                {
                    await _db.SaveCharacterSlotAsync(userId, null, slot);
                }
            }
        }

        // Should only be called via UserDbDataManager.
        public async Task LoadData(ICommonSession session, CancellationToken cancel)
        {
            if (!ShouldStorePrefs(session.Channel.AuthType))
            {
                // Don't store data for guests.
                var prefsData = new PlayerPrefData
                {
                    PrefsLoaded = true,
                    Prefs = new PlayerPreferences(
                        new[] {new KeyValuePair<int, ICharacterProfile>(0, HumanoidCharacterProfile.Random())},
                        0, Color.Transparent)
                };

                _cachedPlayerPrefs[session.UserId] = prefsData;
                _wh40kOnboarding.SetTransient(session.UserId, Wh40kPlayerProgressSnapshot.LegacyCompleted);
                _wh40kRpgProgress.Cache(_wh40kRpg.CreateTransientLegacyAccount(session.UserId));
            }
            else
            {
                var prefsData = new PlayerPrefData();
                var loadTask = LoadPrefs();
                _cachedPlayerPrefs[session.UserId] = prefsData;

                await loadTask;

                async Task LoadPrefs()
                {
                    var prefs = await GetOrCreatePreferencesAsync(session.UserId, cancel);
                    prefsData.Prefs = prefs;
                    var onboarding = await _wh40kOnboarding.LoadForExistingPreferencesAsync(session.UserId, cancel);
                    var account = await _wh40kRpg.LoadForExistingPreferencesAsync(session.UserId, onboarding, cancel);
                    if (account != null)
                    {
                        _wh40kRpgProgress.Cache(account);
                        await _wh40kParties.LoadAsync(session.UserId, cancel);
                    }
                }
            }
        }

        public void FinishLoad(ICommonSession session)
        {
            // This is a separate step from the actual database load.
            // Sanitizing preferences requires play time info due to loadouts.
            // And play time info is loaded concurrently from the DB with preferences.
            var prefsData = _cachedPlayerPrefs[session.UserId];
            DebugTools.Assert(prefsData.Prefs != null);
            prefsData.Prefs = SanitizePreferences(session, prefsData.Prefs, _dependencies);

            prefsData.PrefsLoaded = true;

            var msg = new MsgPreferencesAndSettings();
            msg.Preferences = prefsData.Prefs;
            msg.Wh40kProgress = _wh40kOnboarding.Get(session.UserId);
            msg.Settings = new GameSettings
            {
                MaxCharacterSlots = MaxCharacterSlots
            };
            _netManager.ServerSendMessage(msg, session.Channel);

            // Frontier: notify other entities that your player data is loaded.
            if (session.AttachedEntity != null)
                _entityManager.EventBus.RaiseLocalEvent(session.AttachedEntity.Value, new PreferencesLoadedEvent(session, prefsData.Prefs));
        }

        public void OnClientDisconnected(ICommonSession session)
        {
            _cachedPlayerPrefs.Remove(session.UserId);
            _wh40kOnboarding.Remove(session.UserId);
            _wh40kRpg.Remove(session.UserId);
            _wh40kRpgProgress.Remove(session.UserId);
            _wh40kParties.OnDisconnected(session.UserId);
            lock (_wh40kOnboardingCompletionsLock)
            {
                _wh40kOnboardingCompletions.Remove(session.UserId);
            }
        }

        public bool IsWh40kOnboardingRequired(NetUserId userId)
        {
            // A row is loaded before a persistent player can enter the lobby. If it is missing, corrupt, or
            // not loaded yet, fail closed instead of allowing a half-created account into a round.
            return !_wh40kOnboarding.Get(userId).CanUseLegacyPersonalization;
        }

        public bool HavePreferencesLoaded(ICommonSession session)
        {
            return _cachedPlayerPrefs.ContainsKey(session.UserId);
        }


        /// <summary>
        /// Tries to get the preferences from the cache
        /// </summary>
        /// <param name="userId">User Id to get preferences for</param>
        /// <param name="playerPreferences">The user preferences if true, otherwise null</param>
        /// <returns>If preferences are not null</returns>
        public bool TryGetCachedPreferences(NetUserId userId,
            [NotNullWhen(true)] out PlayerPreferences? playerPreferences)
        {
            if (_cachedPlayerPrefs.TryGetValue(userId, out var prefs))
            {
                playerPreferences = prefs.Prefs;
                return prefs.Prefs != null;
            }

            playerPreferences = null;
            return false;
        }

        /// <summary>
        /// Retrieves preferences for the given username from storage.
        /// </summary>
        public PlayerPreferences GetPreferences(NetUserId userId)
        {
            var prefs = _cachedPlayerPrefs[userId].Prefs;
            if (prefs == null)
            {
                throw new InvalidOperationException("Preferences for this player have not loaded yet.");
            }

            return prefs;
        }

        /// <summary>
        /// Retrieves preferences for the given username from storage or returns null.
        /// </summary>
        public PlayerPreferences? GetPreferencesOrNull(NetUserId? userId)
        {
            if (userId == null)
                return null;

            if (_cachedPlayerPrefs.TryGetValue(userId.Value, out var pref))
                return pref.Prefs;
            return null;
        }

        private async Task<PlayerPreferences> GetOrCreatePreferencesAsync(NetUserId userId, CancellationToken cancel)
        {
            var prefs = await _db.GetPlayerPreferencesAsync(userId, cancel);
            if (prefs is null)
            {
                return await _db.InitPrefsAsync(userId, HumanoidCharacterProfile.Random(), cancel);
            }

            return prefs;
        }

        public async Task RefreshPreferencesAsync(ICommonSession session, CancellationToken cancel)
        {
            if (!_cachedPlayerPrefs.TryGetValue(session.UserId, out var prefsData))
                return;

            var loadTask = LoadPrefs();
            _cachedPlayerPrefs[session.UserId] = prefsData;

            await loadTask;
            return;

            async Task LoadPrefs()
            {
                var prefs = await _db.GetPlayerPreferencesAsync(session.UserId, cancel);

                if (prefs != null)
                {
                    prefsData.Prefs = prefs;
                    prefsData.PrefsLoaded = true;
                    var onboarding = await _wh40kOnboarding.LoadForExistingPreferencesAsync(session.UserId, cancel);
                    var account = await _wh40kRpg.LoadForExistingPreferencesAsync(session.UserId, onboarding, cancel);
                    if (account != null)
                    {
                        _wh40kRpgProgress.Cache(account);
                        await _wh40kParties.LoadAsync(session.UserId, cancel);
                    }

                    var msg = new MsgPreferencesAndSettings
                    {
                        Preferences = prefs,
                        Wh40kProgress = _wh40kOnboarding.Get(session.UserId),
                        Settings = new GameSettings
                        {
                            MaxCharacterSlots = MaxCharacterSlots
                        }
                    };

                    _netManager.ServerSendMessage(msg, session.Channel);
                }
            }
        }


        private PlayerPreferences SanitizePreferences(ICommonSession session, PlayerPreferences prefs, IDependencyCollection collection)
        {
            // Clean up preferences in case of changes to the game,
            // such as removed jobs still being selected.

            return new PlayerPreferences(prefs.Characters.Select(p =>
            {
                return new KeyValuePair<int, ICharacterProfile>(p.Key, p.Value.Validated(session, collection));
            }), prefs.SelectedCharacterIndex, prefs.AdminOOCColor);
        }

        public IEnumerable<KeyValuePair<NetUserId, ICharacterProfile>> GetSelectedProfilesForPlayers(
            List<NetUserId> usernames)
        {
            return usernames
                .Select(p => (_cachedPlayerPrefs[p].Prefs, p))
                .Where(p => p.Prefs != null)
                .Select(p => new KeyValuePair<NetUserId, ICharacterProfile>(p.p, p.Prefs!.SelectedCharacter));
        }

        internal static bool ShouldStorePrefs(LoginType loginType)
        {
            return loginType.HasStaticUserId();
        }

        private sealed class PlayerPrefData
        {
            public bool PrefsLoaded;
            public PlayerPreferences? Prefs;
        }

        void IPostInjectInit.PostInject()
        {
            _userDb.AddOnLoadPlayer(LoadData);
            _userDb.AddOnFinishLoad(FinishLoad);
            _userDb.AddOnPlayerDisconnect(OnClientDisconnected);
        }
    }

    // Frontier: event for notifying that preferences for a particular player have loaded in.
    public sealed class PreferencesLoadedEvent : EntityEventArgs
    {
        public readonly ICommonSession Session;
        public readonly PlayerPreferences Prefs;

        public PreferencesLoadedEvent(ICommonSession session, PlayerPreferences prefs)
        {
            Session = session;
            Prefs = prefs;
        }
    }
    // End Frontier
}
