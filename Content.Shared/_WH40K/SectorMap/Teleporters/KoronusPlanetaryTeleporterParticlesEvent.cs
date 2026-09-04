using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.Teleporters;

/// <summary>
/// PVS-only client presentation state for the finite teleporter spin-up.
/// It deliberately carries no gameplay data and never controls a teleporter.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTeleporterChargeParticlesEvent(
    NetEntity teleporter,
    bool active,
    float duration) : EntityEventArgs
{
    public NetEntity Teleporter { get; } = teleporter;
    public bool Active { get; } = active;
    public float Duration { get; } = duration;
}
