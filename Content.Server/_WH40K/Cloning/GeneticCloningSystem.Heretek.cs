using Content.Server._WH40K.CharacterCreation;
using Content.Server._WH40K.Progression;
using Robust.Shared.Network;

namespace Content.Server._Mono.Cloning;

public sealed partial class GeneticCloningSystem
{
    [Dependency] private Wh40kProgressManager _heretekProgress = default!;
    [Dependency] private Wh40kCharacterStatsSpawnSystem _heretekStats = default!;

    private void ApplyHeretekAccountStats(EntityUid body, NetUserId userId)
    {
        if (_heretekProgress.TryGetAccount(userId, out var account))
            _heretekStats.ApplyAccountStats(body, account);
    }
}
