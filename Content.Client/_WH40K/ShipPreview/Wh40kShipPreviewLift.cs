using Content.Client.Shuttles;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.ShipPreview;

/// <summary>
/// Estimates hull mass load capacity and thruster lift ratio from client-loaded vessel thruster prototypes.
/// </summary>
public static class Wh40kShipPreviewLift
{
    private const float TileMass = 0.5f;
    private const float DefaultThrust = 150f;
    private const float OrdinaryAtmosphereThrust = 0.5f;
    private const float StandardGravity = 9.81f;

    public static float RatedLoad(int tileCount)
    {
        return tileCount * TileMass / 3f;
    }

    public static float MaxLiftGravity(IEntityManager entMan, IPrototypeManager protoMan, EntityUid grid, int tileCount)
    {
        if (tileCount <= 0)
            return 0f;

        var lift = 0f;
        var expedition = HasExpeditionConsole(entMan, grid);
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (!entMan.HasComponent<ThrusterComponent>(uid)
                || entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype is not { } proto
                || !protoMan.GetPrototypeData(proto).TryGetValue("Thruster", out var data))
            {
                continue;
            }

            if (data.TryGet<ValueDataNode>("thrusterType", out var type) && type.Value != "Linear")
                continue;

            if (data.TryGet<ValueDataNode>("enabled", out var enabled) && enabled.Value == "false")
                continue;

            var thrust = DefaultThrust;
            if (data.TryGet<ValueDataNode>("thrust", out var rated) && Parse.TryFloat(rated.Value, out var parsed))
                thrust = parsed;

            lift += thrust * (expedition ? 1f : OrdinaryAtmosphereThrust) / StandardGravity;
        }

        return lift / (tileCount * TileMass);
    }

    private static bool HasExpeditionConsole(IEntityManager entMan, EntityUid grid)
    {
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (entMan.HasComponent<SalvageExpeditionConsoleComponent>(uid))
                return true;
        }

        return false;
    }
}
