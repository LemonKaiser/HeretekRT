/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._CE.ZLevels.PVS;
using Content.Shared._CE.ZLevels.Core.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>
    /// Creates a new zLevel Map Network entity
    /// </summary>
    [PublicAPI]
    public Entity<CEZMapNetworkComponent> CreateMapNetwork(ComponentRegistry? components = null)
    {
        var ent = Spawn();

        var zLevel = EnsureComp<CEZMapNetworkComponent>(ent);
        EnsureComp<CEPvsOverrideComponent>(ent);

        zLevel.Components = components ?? new ComponentRegistry();

        return (ent, zLevel);
    }

    /// <summary>
    /// Attempts to add the specified maps to the zNetwork network at the specified depths
    /// </summary>
    [PublicAPI]
    public bool TryAddMapsIntoNetwork(Entity<CEZMapNetworkComponent> network, Dictionary<EntityUid, int> maps)
    {
        var success = true;
        var addedAny = false;
        foreach (var (mapUid, depth) in maps)
        {
            if (TryGetMapNetwork(mapUid, out var otherNetwork))
            {
                Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network}: This map is already in another network {otherNetwork}.");
                success = false;
                continue;
            }

            if (network.Comp.ZLevels.ContainsKey(depth))
            {
                Log.Error($"Failed to add map {mapUid} to ZLevelNetwork {network}: This depth is already occupied.");
                success = false;
                continue;
            }

            if (network.Comp.ZLevels.ContainsValue(mapUid))
            {
                Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network} at depth {depth}: This map is already in this network.");
                success = false;
                continue;
            }

            network.Comp.ZLevels[depth] = mapUid;
            network.Comp.ZLevelByEntity[mapUid] = depth;

            var levelMapComponent = EnsureComp<CEZMapComponent>(mapUid);
            levelMapComponent.Depth = depth;
            levelMapComponent.NetworkUid = network;

            if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveMapUid) &&
                aboveMapUid is { } aboveUid && TryComp<CEZMapComponent>(aboveUid, out var above))
            {
                levelMapComponent.MapAbove = aboveUid;
                above.MapBelow = mapUid;
                Dirty(aboveUid, above);
            }

            if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowMapUid) &&
                belowMapUid is { } belowUid && TryComp<CEZMapComponent>(belowUid, out var below))
            {
                levelMapComponent.MapBelow = belowUid;
                below.MapAbove = mapUid;
                Dirty(belowUid, below);
            }

            Dirty(mapUid, levelMapComponent);
            addedAny = true;

            var ev = new CEMapAddedIntoZNetworkEvent(network, depth);
            RaiseLocalEvent(mapUid, ref ev);
        }

        if (addedAny)
        {
            RebuildSortedCache(network);
            RaiseLocalEvent(network, new CEZLevelMapNetworkUpdatedEvent());
        }

        return success;
    }

    /// <summary>
    /// Returns the map entity at a specific depth within a z-network, or false if none exists.
    /// </summary>
    [PublicAPI]
    public bool TryGetMapAtDepth(Entity<CEZMapNetworkComponent?> network, int depth, out EntityUid mapUid)
    {
        mapUid = EntityUid.Invalid;

        if (!Resolve(network, ref network.Comp, false) ||
            !network.Comp.ZLevels.TryGetValue(depth, out var uid) ||
            uid is not { } validUid)
            return false;

        mapUid = validUid;
        return true;
    }

    /// <summary>
    /// Deletes a map z-network: queues deletion of all maps in the network, then the network entity itself.
    /// </summary>
    [PublicAPI]
    public void DeleteMapNetwork(EntityUid networkUid)
    {
        if (!TryComp<CEZMapNetworkComponent>(networkUid, out var zNet))
        {
            Log.Error($"CEZLevelsSystem: entity {networkUid} does not have CEZLevelsNetworkComponent.");
            return;
        }

        foreach (var (_, mapUid) in zNet.ZLevels)
        {
            if (mapUid != null)
                QueueDel(mapUid.Value);
        }

        QueueDel(networkUid);
    }

    private void OnLevelTerminating(Entity<CEZMapComponent> level, ref EntityTerminatingEvent args)
    {
        var networkUid = level.Comp.NetworkUid;
        if (TerminatingOrDeleted(networkUid) || !TryComp<CEZMapNetworkComponent>(networkUid, out var network))
            return;

        if (!network.ZLevelByEntity.Remove(level.Owner, out var depth))
            return;

        network.ZLevels.Remove(depth);
        if (level.Comp.MapAbove is { } above && !TerminatingOrDeleted(above) &&
            TryComp<CEZMapComponent>(above, out var upper))
        {
            upper.MapBelow = null;
            Dirty(above, upper);
        }

        if (level.Comp.MapBelow is { } below && !TerminatingOrDeleted(below) &&
            TryComp<CEZMapComponent>(below, out var lower))
        {
            lower.MapAbove = null;
            Dirty(below, lower);
        }

        RebuildSortedCache((networkUid, network));
        RaiseLocalEvent(networkUid, new CEZLevelMapNetworkUpdatedEvent());
    }

    private void RebuildSortedCache(Entity<CEZMapNetworkComponent> network)
    {
        var comp = network.Comp;
        var list = comp.SortedZLevels;
        list.Clear();
        var min = int.MaxValue;
        var max = int.MinValue;

        foreach (var (depth, mapUid) in comp.ZLevels)
        {
            if (mapUid is not { } uid || !uid.IsValid())
                continue;

            min = Math.Min(min, depth);
            max = Math.Max(max, depth);
        }

        if (min <= max)
        {
            for (var depth = min; depth < max; depth++)
                list.Add(comp.ZLevels.GetValueOrDefault(depth) ?? EntityUid.Invalid);
            list.Add(comp.ZLevels.GetValueOrDefault(max) ?? EntityUid.Invalid);
        }
        else
        {
            min = max = 0;
        }

        comp.SortedMin = min;
        comp.SortedMax = max;
        Dirty(network);
    }
}

/// <summary>
/// Called on ZLevel Network Entity, when maps added or removed from network
/// </summary>
public sealed class CEZLevelMapNetworkUpdatedEvent : EntityEventArgs;

/// <summary>
/// Called on map, when it added to ZNetwork
/// </summary>
[ByRefEvent]
public readonly struct CEMapAddedIntoZNetworkEvent(Entity<CEZMapNetworkComponent> network, int depth)
{
    public readonly Entity<CEZMapNetworkComponent> Network = network;
    public readonly int Depth = depth;
}
