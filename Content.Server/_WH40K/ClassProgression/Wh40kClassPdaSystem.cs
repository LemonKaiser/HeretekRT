using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Progression;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared._WH40K.ClassProgression;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Adapts class progression to private, actor-addressed PDA BUI messages.
/// No account or runtime class data is stored on a networked PDA component.
/// </summary>
public sealed class Wh40kClassPdaSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private Wh40kClassProgressManager _classProgress = default!;
    [Dependency] private Wh40kProgressManager _accountProgress = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kClassCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<Wh40kClassCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        _classProgress.ProgressChanged += OnClassProgressChanged;
        _accountProgress.ProgressChanged += OnAccountProgressChanged;
    }

    public override void Shutdown()
    {
        _classProgress.ProgressChanged -= OnClassProgressChanged;
        _accountProgress.ProgressChanged -= OnAccountProgressChanged;
        base.Shutdown();
    }

    private async void OnUiReady(
        EntityUid uid,
        Wh40kClassCartridgeComponent component,
        CartridgeUiReadyEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        await SendFreshSnapshotAsync(
            args.Loader,
            args.Actor,
            session,
            Wh40kClassUiOperationStatus.None);
    }

    private async void OnUiMessage(
        EntityUid uid,
        Wh40kClassCartridgeComponent component,
        CartridgeMessageEvent args)
    {
        if (args is not Wh40kClassUiMessage message ||
            !_players.TryGetSessionByEntity(args.Actor, out var session))
        {
            return;
        }

        var loader = GetEntity(args.LoaderUid);
        switch (message.Action)
        {
            case Wh40kClassUiAction.Refresh:
                await SendFreshSnapshotAsync(
                    loader,
                    args.Actor,
                    session,
                    Wh40kClassUiOperationStatus.None);
                return;
            case Wh40kClassUiAction.Purchase:
                var result = await _classProgress.PurchaseAsync(
                    session.UserId,
                    message.SkillId,
                    message.ExpectedRevision);
                SendSnapshot(
                    loader,
                    args.Actor,
                    ToUiStatus(result.Status),
                    CreateUiSnapshot(args.Actor, result));
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task SendFreshSnapshotAsync(
        EntityUid loader,
        EntityUid actor,
        ICommonSession session,
        Wh40kClassUiOperationStatus status)
    {
        var result = await _classProgress.GetSnapshotAsync(session.UserId);
        SendSnapshot(
            loader,
            actor,
            result.Snapshot == null ? Wh40kClassUiOperationStatus.AccountUnavailable : status,
            CreateUiSnapshot(actor, result));
    }

    private Wh40kClassUiSnapshot? CreateUiSnapshot(
        EntityUid actor,
        Wh40kClassTreeOperationResult result)
    {
        if (result.Snapshot == null || result.ClassProgress == null)
            return null;

        var recent = result.ClassProgress.Skills
            .OrderByDescending(skill => skill.PurchasedAt)
            .ThenBy(skill => skill.SkillId, StringComparer.Ordinal)
            .Take(3)
            .Select(skill => skill.SkillId)
            .ToList();
        var activeEffects = TryComp<Wh40kClassRuntimeProfileComponent>(actor, out var profile)
            ? profile.ActiveEffects
            : null;
        var bonuses = new List<Wh40kClassBonusUiSnapshot>();
        foreach (var purchased in result.ClassProgress.Skills.OrderBy(skill => skill.SkillId, StringComparer.Ordinal))
        {
            if (!_prototypes.TryIndex<Wh40kClassSkillPrototype>(purchased.SkillId, out var skill))
                continue;

            foreach (var effectId in skill.Effects)
            {
                if (!_prototypes.TryIndex(effectId, out Wh40kClassSkillEffectPrototype? effect))
                    continue;

                var active = activeEffects?.GetValueOrDefault(effect.ID);
                var state = active != null
                    ? Wh40kClassBonusActivationState.Active
                    : skill.Availability != Wh40kClassContentAvailability.Enabled ||
                      effect.Availability != Wh40kClassContentAvailability.Enabled
                        ? Wh40kClassBonusActivationState.ContentUnavailable
                        : Wh40kClassBonusActivationState.MissingEquipment;
                bonuses.Add(new Wh40kClassBonusUiSnapshot(
                    skill.ID,
                    effect.ID,
                    effect.Kind,
                    state,
                    active?.AppliedRarityPercent ?? 0f));
            }
        }

        return new Wh40kClassUiSnapshot(result.Snapshot, recent, bonuses);
    }

    private void SendSnapshot(
        EntityUid loader,
        EntityUid actor,
        Wh40kClassUiOperationStatus status,
        Wh40kClassUiSnapshot? snapshot)
    {
        _ui.ServerSendUiMessage(
            loader,
            PdaUiKey.Key,
            new Wh40kClassSnapshotBuiMessage(status, snapshot),
            actor);
    }

    private void OnClassProgressChanged(
        NetUserId userId,
        Wh40kAccountRpgRecord account,
        Wh40kAccountClassProgressRecord progress)
    {
        _ = RefreshOpenUiAsync(userId);
    }

    private void OnAccountProgressChanged(NetUserId userId, Wh40kAccountRpgRecord account)
    {
        _ = RefreshOpenUiAsync(userId);
    }

    private async Task RefreshOpenUiAsync(NetUserId userId)
    {
        if (!_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { Valid: true } actor)
        {
            return;
        }

        var result = await _classProgress.GetSnapshotAsync(userId);
        var snapshot = CreateUiSnapshot(actor, result);
        foreach (var (loader, key) in _ui.GetActorUis(actor))
        {
            if (!PdaUiKey.Key.Equals(key) ||
                !TryComp(loader, out CartridgeLoaderComponent? cartridgeLoader) ||
                cartridgeLoader.ActiveProgram is not { } program ||
                !HasComp<Wh40kClassCartridgeComponent>(program))
            {
                continue;
            }

            SendSnapshot(loader, actor, Wh40kClassUiOperationStatus.None, snapshot);
        }
    }

    internal static Wh40kClassUiOperationStatus ToUiStatus(Wh40kClassSkillPurchaseStatus status)
    {
        return status switch
        {
            Wh40kClassSkillPurchaseStatus.Success => Wh40kClassUiOperationStatus.PurchaseSucceeded,
            Wh40kClassSkillPurchaseStatus.AccountNotFound => Wh40kClassUiOperationStatus.AccountUnavailable,
            Wh40kClassSkillPurchaseStatus.SkillNotFound => Wh40kClassUiOperationStatus.SkillNotFound,
            Wh40kClassSkillPurchaseStatus.ClassMismatch => Wh40kClassUiOperationStatus.ClassMismatch,
            Wh40kClassSkillPurchaseStatus.ContentUnavailable => Wh40kClassUiOperationStatus.ContentUnavailable,
            Wh40kClassSkillPurchaseStatus.InsufficientLevel => Wh40kClassUiOperationStatus.InsufficientLevel,
            Wh40kClassSkillPurchaseStatus.MissingPrerequisite => Wh40kClassUiOperationStatus.MissingPrerequisite,
            Wh40kClassSkillPurchaseStatus.InsufficientPoints => Wh40kClassUiOperationStatus.InsufficientPoints,
            Wh40kClassSkillPurchaseStatus.AlreadyPurchased => Wh40kClassUiOperationStatus.AlreadyPurchased,
            Wh40kClassSkillPurchaseStatus.RevisionMismatch => Wh40kClassUiOperationStatus.RevisionMismatch,
            _ => Wh40kClassUiOperationStatus.AccountUnavailable,
        };
    }
}
