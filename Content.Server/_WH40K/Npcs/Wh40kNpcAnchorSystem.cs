using System.Linq;
using Content.Server._WH40K.Quests;
using Content.Server._WH40K.Dialogue.Components;
using Content.Shared._WH40K.Npcs;
using Content.Shared.Interaction;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Npcs;

/// <summary>Fail-closed registry of authored markers and their replaceable bodies. Never spawns NPCs.</summary>
public sealed partial class Wh40kNpcAnchorSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Wh40kQuestContentSystem _content = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    private readonly Dictionary<ProtoId<Wh40kNpcAnchorPrototype>, HashSet<EntityUid>> _anchors = new();
    private readonly Dictionary<ProtoId<Wh40kNpcAnchorPrototype>, HashSet<EntityUid>> _bodies = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kNpcAnchorComponent, ComponentStartup>(OnAnchorStartup);
        SubscribeLocalEvent<Wh40kNpcAnchorComponent, ComponentShutdown>(OnAnchorShutdown);
        SubscribeLocalEvent<Wh40kNpcIdentityComponent, ComponentStartup>(OnBodyStartup);
        SubscribeLocalEvent<Wh40kNpcIdentityComponent, ComponentShutdown>(OnBodyShutdown);
    }

    public bool TryGetAnchor(ProtoId<Wh40kNpcAnchorPrototype> key, out EntityUid uid)
    {
        uid = default;
        if (!_content.IsValidAnchor(key) || !_prototypes.TryIndex(key, out var anchor) ||
            !_prototypes.TryIndex(anchor.Profile, out var profile) ||
            !_anchors.TryGetValue(key, out var markers) || markers.Count != 1)
            return false;

        // Unique authority cannot be duplicated under a different anchor key either.
        if (profile.RequiresUniqueAnchor)
        {
            foreach (var (otherKey, otherMarkers) in _anchors)
            {
                if (otherKey == key || otherMarkers.Count == 0)
                    continue;
                if (_prototypes.TryIndex(otherKey, out var other) && other.Profile == anchor.Profile)
                    return false;
            }
        }

        uid = markers.Single();
        return !TerminatingOrDeleted(uid);
    }

    public bool HasRegisteredBody(ProtoId<Wh40kNpcAnchorPrototype> key)
        => _bodies.TryGetValue(key, out var bodies) && bodies.Count > 0;

    public bool IsInRange(EntityUid actor, EntityUid npc)
        => !TerminatingOrDeleted(npc) && (TryComp(npc, out DialogueInteractableComponent? dialogue)
            ? _interaction.InRangeUnobstructed(actor, npc, dialogue.InteractionRange)
            : _interaction.InRangeUnobstructed(actor, npc));

    public bool TryGetNpc(ProtoId<Wh40kNpcAnchorPrototype> key, out EntityUid uid)
    {
        uid = default;
        if (!TryGetAnchor(key, out _) || !_bodies.TryGetValue(key, out var bodies) || bodies.Count != 1)
            return false;
        uid = bodies.Single();
        return !TerminatingOrDeleted(uid);
    }

    public bool TryGetIdentity(EntityUid uid, out ProtoId<Wh40kNpcAnchorPrototype> key)
    {
        key = default;
        if (!TryComp<Wh40kNpcIdentityComponent>(uid, out var identity) ||
            !TryGetNpc(identity.Anchor, out var resolved) || resolved != uid)
            return false;
        key = identity.Anchor;
        return true;
    }

    private void OnAnchorStartup(EntityUid uid, Wh40kNpcAnchorComponent component, ComponentStartup args)
        => Register(_anchors, component.Anchor, uid);

    private void OnAnchorShutdown(EntityUid uid, Wh40kNpcAnchorComponent component, ComponentShutdown args)
        => Unregister(_anchors, component.Anchor, uid);

    private void OnBodyStartup(EntityUid uid, Wh40kNpcIdentityComponent component, ComponentStartup args)
        => Register(_bodies, component.Anchor, uid);

    private void OnBodyShutdown(EntityUid uid, Wh40kNpcIdentityComponent component, ComponentShutdown args)
        => Unregister(_bodies, component.Anchor, uid);

    private void Register(Dictionary<ProtoId<Wh40kNpcAnchorPrototype>, HashSet<EntityUid>> entries,
        ProtoId<Wh40kNpcAnchorPrototype> key, EntityUid uid)
    {
        if (string.IsNullOrWhiteSpace(key.Id) || !_prototypes.HasIndex(key))
        {
            Log.Warning($"Unknown NPC anchor '{key}' on {ToPrettyString(uid)}; identity is unavailable.");
            return;
        }

        if (!entries.TryGetValue(key, out var entities))
            entries[key] = entities = [];
        entities.Add(uid);
        if (entities.Count > 1)
            Log.Warning($"Duplicate NPC anchor/body '{key}'; identity is unavailable until the conflict is removed.");
    }

    private static void Unregister(Dictionary<ProtoId<Wh40kNpcAnchorPrototype>, HashSet<EntityUid>> entries,
        ProtoId<Wh40kNpcAnchorPrototype> key, EntityUid uid)
    {
        if (!entries.TryGetValue(key, out var entities))
            return;
        entities.Remove(uid);
        if (entities.Count == 0)
            entries.Remove(key);
    }
}
