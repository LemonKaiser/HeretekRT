using Content.Shared.Shuttles.Events;

namespace Content.Client.Shuttles.Systems;

/// <summary>
/// Renders short-lived shuttle impact sparks locally so they do not exist as server entities.
/// </summary>
public sealed class ShuttleImpactEffectsSystem : EntitySystem
{
    private const string SparkEffectPrototype = "EffectSparks";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ShuttleImpactEffectsEvent>(OnImpactEffects);
    }

    private void OnImpactEffects(ShuttleImpactEffectsEvent args)
    {
        foreach (var coordinates in args.Coordinates)
        {
            var entityCoordinates = GetCoordinates(coordinates);
            if (!entityCoordinates.IsValid(EntityManager))
                continue;

            Spawn(SparkEffectPrototype, entityCoordinates);
        }
    }
}
