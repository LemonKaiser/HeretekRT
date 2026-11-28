using Content.Server.GameTicking.Events;
using Content.Shared._WH40K.WolfChem.Purity;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Moves each graded reaction's optimal temperature by up to its <see cref="Wh40kReactionPurityPrototype.Drift"/> at
/// round start, so last round's numbers are only close.
/// </summary>
public sealed partial class Wh40kPurityDriftSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChemicalReactionSystem _reactions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(_ => Roll());
    }

    /// <summary>Rolls a new shift for every reaction that drifts.</summary>
    public void Roll()
    {
        _reactions.Wh40kOptimumShift.Clear();
        foreach (var purity in _proto.EnumeratePrototypes<Wh40kReactionPurityPrototype>())
        {
            if (purity.OptimalTemp != null && purity.Drift > 0f)
                _reactions.Wh40kOptimumShift[purity.ID] = _random.NextFloat(-purity.Drift, purity.Drift);
        }
    }
}
