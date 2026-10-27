using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Company;

/// <summary>
/// Minimal hierarchy used by player-run factions.
/// </summary>
[Serializable, NetSerializable]
public enum FactionRole : byte
{
    Member,
    Officer,
    Leader,
}
