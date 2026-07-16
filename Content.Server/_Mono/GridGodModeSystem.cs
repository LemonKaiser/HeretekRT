using System.Linq;
using Content.Server.Damage.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Spreader;
using Content.Shared._Mono;
using Content.Shared.Damage.Components;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;

namespace Content.Server._Mono;

/// <summary>
/// System that handles the GridGodModeComponent, which applies GodMode to all non-organic entities on a grid.
/// </summary>
public sealed partial class GridGodModeSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GodmodeSystem _godmode = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    // Applying Godmode can raise damage events. Defer protection until the next tick so
    // entities spawned during map initialization have finished starting all components
    // (notably MechComponent, which creates its containers in ComponentStartup).
    private readonly HashSet<EntityUid> _pendingProtection = new();

    // Godmode is a runtime effect of GridGodMode, not map-authored state. Temporarily remove
    // only the components owned by this system while the map serializer walks the entity tree;
    // otherwise a save taken a few ticks after map init gains thousands of transient components.
    private readonly Dictionary<EntityUid, GodmodeSaveState> _temporarilyRemovedGodmode = new();

    private readonly record struct GodmodeSaveState(bool WasMovedByPressure, DamageSpecifier? OldDamage);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridGodModeComponent, MapInitEvent>(OnGridGodModeMapInit);
        SubscribeLocalEvent<GridGodModeComponent, ComponentStartup>(OnGridGodModeStartup);
        SubscribeLocalEvent<GridGodModeComponent, ComponentShutdown>(OnGridGodModeShutdown);
        SubscribeLocalEvent<DamageableComponent, ComponentStartup>(OnDamageableStartup);
        SubscribeLocalEvent<DamageableComponent, EntParentChangedMessage>(OnDamageableParentChanged);
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization);
        SubscribeLocalEvent<AfterSerializationEvent>(OnAfterSerialization);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingProtection.Count == 0)
            return;

        var pending = _pendingProtection.ToArray();
        _pendingProtection.Clear();

        foreach (var entity in pending)
        {
            if (TerminatingOrDeleted(entity))
                continue;

            RefreshEntityProtection(entity);
        }
    }

    private void OnGridGodModeMapInit(EntityUid uid, GridGodModeComponent component, MapInitEvent args)
    {
        // Verify this is applied to a grid
        if (!HasComp<MapGridComponent>(uid))
        {
            Log.Warning($"GridGodModeComponent applied to non-grid entity {ToPrettyString(uid)}");
            return;
        }

        // Find all entities on the grid and apply GodMode to them if they're not organic
        var allEntitiesOnGrid = _lookup.GetEntitiesIntersecting(uid).ToHashSet();

        foreach (var entity in allEntitiesOnGrid)
        {
            // Skip the grid itself
            if (entity == uid)
                continue;

            QueueProtection(entity);
        }
    }

    private void OnGridGodModeStartup(EntityUid uid, GridGodModeComponent component, ComponentStartup args)
    {
        if (!HasComp<MapGridComponent>(uid))
            return;

        // Components added by sector bootstrap run after the grid's MapInit event. A startup pass
        // makes those facilities just as protected as map-authored ones.
        foreach (var entity in _lookup.GetEntitiesIntersecting(uid).ToHashSet())
        {
            if (entity != uid)
                QueueProtection(entity);
        }
    }

    private void OnDamageableStartup(EntityUid uid, DamageableComponent component, ComponentStartup args)
    {
        QueueProtection(uid);
    }

    private void OnDamageableParentChanged(EntityUid uid, DamageableComponent component, ref EntParentChangedMessage args)
    {
        QueueProtection(uid);
    }

    private void QueueProtection(EntityUid entity)
    {
        if (!TerminatingOrDeleted(entity))
            _pendingProtection.Add(entity);
    }

    private void OnBeforeSerialization(BeforeSerializationEvent ev)
    {
        // Serialization is synchronous, so the matching AfterSerializationEvent restores
        // the runtime components before the next game tick can observe the temporary state.
        if (_temporarilyRemovedGodmode.Count != 0)
            return;

        var grids = EntityQueryEnumerator<GridGodModeComponent>();
        while (grids.MoveNext(out _, out var component))
        {
            foreach (var entity in component.ProtectedEntities)
            {
                if (TerminatingOrDeleted(entity) ||
                    !TryComp<TransformComponent>(entity, out var transform) ||
                    !ev.MapIds.Contains(transform.MapID) ||
                    !TryComp<GodmodeComponent>(entity, out var godmode))
                {
                    continue;
                }

                _temporarilyRemovedGodmode[entity] = new(
                    godmode.WasMovedByPressure,
                    godmode.OldDamage == null ? null : new DamageSpecifier(godmode.OldDamage));
                RemComp<GodmodeComponent>(entity);
            }
        }
    }

    private void OnAfterSerialization(AfterSerializationEvent ev)
    {
        if (_temporarilyRemovedGodmode.Count == 0)
            return;

        foreach (var (entity, state) in _temporarilyRemovedGodmode)
        {
            if (TerminatingOrDeleted(entity))
                continue;

            EnsureComp<GodmodeComponent>(entity);
            _godmode.RestoreGodmodeState(entity, state.WasMovedByPressure, state.OldDamage);
        }

        _temporarilyRemovedGodmode.Clear();
    }

    private void RefreshEntityProtection(EntityUid entity)
    {
        if (!TryComp<TransformComponent>(entity, out var transform))
            return;

        var currentGrid = transform.GridUid;
        var grids = EntityQueryEnumerator<GridGodModeComponent>();
        while (grids.MoveNext(out var grid, out var component))
        {
            if (currentGrid == grid || !component.ProtectedEntities.Remove(entity))
                continue;

            RemoveGodMode(entity);
        }

        if (currentGrid is { } protectedGrid && TryComp<GridGodModeComponent>(protectedGrid, out var godMode))
            ProcessEntityOnGrid(protectedGrid, entity, godMode);
    }

    private void OnGridGodModeShutdown(EntityUid uid, GridGodModeComponent component, ComponentShutdown args)
    {
        // When the component is removed, remove GodMode from all protected entities
        foreach (var entity in component.ProtectedEntities.ToList())
        {
            if (EntityManager.EntityExists(entity))
            {
                RemoveGodMode(entity);
            }
        }

        component.ProtectedEntities.Clear();
    }

    /// <summary>
    /// Process an entity on a grid and apply GodMode if appropriate
    /// </summary>
    private void ProcessEntityOnGrid(EntityUid gridUid, EntityUid entityUid, GridGodModeComponent component)
    {
        // Spatial lookup can include an overlapping shuttle or a neighbouring grid. Protection
        // belongs only to entities actually parented to this authored facility grid.
        if (!TryComp<TransformComponent>(entityUid, out var transform) || transform.GridUid != gridUid)
            return;

        // Don't apply GodMode to organic entities, ghosts, npcs, or kudzu
        if (IsOrganic(entityUid) || HasComp<GhostComponent>(entityUid) || HasComp<KudzuComponent>(entityUid))
            return;

        ApplyGodMode(gridUid, entityUid, component);
    }

    /// <summary>
    /// Applies GodMode to an entity and adds it to the protected entities list
    /// </summary>
    private void ApplyGodMode(EntityUid gridUid, EntityUid entityUid, GridGodModeComponent component)
    {
        // Skip if the entity is already protected
        if (component.ProtectedEntities.Contains(entityUid))
            return;

        // Do not take ownership of a godmode component provided by another system.
        if (HasComp<GodmodeComponent>(entityUid))
            return;

        // Apply GodMode
        _godmode.EnableGodmode(entityUid);
        component.ProtectedEntities.Add(entityUid);
    }

    /// <summary>
    /// Removes GodMode from an entity
    /// </summary>
    private void RemoveGodMode(EntityUid entityUid)
    {
        if (HasComp<GodmodeComponent>(entityUid))
        {
            _godmode.DisableGodmode(entityUid);
        }
    }

    /// <summary>
    /// Checks if an entity is organic (i.e., has a mind or is a mob)
    /// </summary>
    private bool IsOrganic(EntityUid entityUid)
    {
        // Skip ghosts
        if (HasComp<GhostComponent>(entityUid))
            return false;

        // Check if we have a player entity that's either still around or alive and may come back
        if (_mind.TryGetMind(entityUid, out var mind, out var mindComp) && !_mind.IsCharacterDeadPhysically(mindComp))
        {
            return true;
        }

        // Also consider anything with a MobStateComponent as organic
        if (HasComp<MobStateComponent>(entityUid))
        {
            return true;
        }

        // Also check for anything with HTN such as NPCs, such as turrets.
        if (HasComp<HTNComponent>(entityUid))
        {
            return true;
        }

        return false;
    }
}
