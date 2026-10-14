using Content.Shared._WH40K.Activities;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Releases finite physical cargo after a successful expedition or closed warp breach.
/// Recovered cargo uses ordinary storage, with no activity ownership or automatic payout.
/// </summary>
public sealed class KoronusActivitySalvageSystem : EntitySystem
{
    [Dependency] private SharedEntityStorageSystem _storage = default!;

    public EntityUid? SpawnSalvage(KoronusActivitySalvageProfile profile, KoronusActivityRisk risk,
        EntityCoordinates coordinates)
    {
        if (profile == KoronusActivitySalvageProfile.None)
            return null;
        var crate = Spawn($"KoronusSalvage{profile}", coordinates);
        if (risk == KoronusActivityRisk.High)
        {
            var rare = Spawn("MaterialDiamond1", coordinates);
            if (!_storage.Insert(rare, crate))
                QueueDel(rare);
        }
        return crate;
    }
}
