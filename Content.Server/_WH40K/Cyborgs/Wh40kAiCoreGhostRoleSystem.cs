using Content.Server.Ghost.Roles.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Clothing;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.NameIdentifier;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Reopens abandoned ghost-role AI cores every 5 seconds, releases any borgs left bound to the departed AI,
/// and renames ghost-role AI players from their character's Borg loadout name when configured.
/// </summary>
public sealed partial class Wh40kAiCoreGhostRoleSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private static readonly string AiLoadout = LoadoutSystem.GetJobPrototype("Borg");

    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;
    [Dependency] private Wh40kBorgRadioKeysServerSystem _radioKeys = default!;

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationAiHeldComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(EntityUid uid, StationAiHeldComponent component, MindAddedMessage args)
    {
        if (!HasComp<GhostRoleComponent>(uid) || args.Mind.Comp.UserId is not { } userId)
            return;

        if (_prefs.GetPreferencesOrNull(userId)?.SelectedCharacter is not HumanoidCharacterProfile profile
            || !profile.Loadouts.TryGetValue(AiLoadout, out var loadout)
            || string.IsNullOrWhiteSpace(loadout.EntityName)
            || !_proto.TryIndex<RoleLoadoutPrototype>(AiLoadout, out var role)
            || !role.CanCustomizeName)
            return;

        var name = loadout.EntityName.Trim();
        if (TryComp<NameIdentifierComponent>(uid, out var identifier) && !string.IsNullOrEmpty(identifier.FullIdentifier))
            name = $"{name} {identifier.FullIdentifier}";

        _metaData.SetEntityName(uid, name);
        args.Mind.Comp.CharacterName = name;
        Dirty(args.Mind);

        if (_stationAi.TryGetCore(uid, out var core))
            _metaData.SetEntityName(core.Owner, name);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        CheckAbandonedCores();
    }

    public void CheckAbandonedCores()
    {
        var abandoned = new List<EntityUid>();
        var query = EntityQueryEnumerator<StationAiHeldComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!HasComp<GhostRoleComponent>(uid)
                && !_mind.TryGetMind(uid, out _, out _)
                && _stationAi.TryGetCore(uid, out _)
                && !IsInBorg(uid))
                abandoned.Add(uid);
        }

        foreach (var uid in abandoned)
            Reopen(uid);
    }

    private bool IsInBorg(EntityUid ai)
    {
        var query = EntityQueryEnumerator<AiRemoteControllerComponent>();
        while (query.MoveNext(out var borg, out var remote))
        {
            if (remote.AiHolder == ai
                && remote.LinkedMind is { } linked
                && _mind.TryGetMind(borg, out var mind, out _)
                && mind == linked)
                return true;
        }

        return false;
    }

    private void Reopen(EntityUid uid)
    {
        if (Prototype(uid) is not { } proto)
            return;

        var roleName = _factory.GetComponentName<GhostRoleComponent>();
        var takeoverName = _factory.GetComponentName<GhostTakeoverAvailableComponent>();
        if (!proto.Components.TryGetValue(roleName, out var role)
            || !proto.Components.TryGetValue(takeoverName, out var takeover))
            return;

        var remotes = EntityQueryEnumerator<AiRemoteControllerComponent>();
        while (remotes.MoveNext(out var borg, out var remote))
        {
            if (remote.AiHolder != uid)
                continue;

            remote.AiHolder = null;
            remote.LinkedMind = null;
            _radioKeys.RestoreAfterAi(borg);
        }

        EntityManager.AddComponents(uid, new ComponentRegistry
        {
            [takeoverName] = takeover,
            [roleName] = role,
        });

        Log.Info($"Reopened the ghost role of abandoned AI {ToPrettyString(uid)}");
    }
}
