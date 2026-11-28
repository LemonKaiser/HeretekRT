using Content.Shared.FixedPoint;
using Content.Shared.Materials;

namespace Content.Server.Power.Generator;

public sealed partial class GeneratorSystem
{
    /// <summary>Reports the fill level of the installed generator's own fuel storage.</summary>
    public float? GetLocalFuelFraction(EntityUid uid)
    {
        if (TryComp<SolidFuelGeneratorAdapterComponent>(uid, out var solid))
        {
            if (!TryComp<MaterialStorageComponent>(uid, out var storage) || storage.StorageLimit is not > 0 ||
                !float.IsFinite(solid.Multiplier) || solid.Multiplier <= 0f)
                return null;

            var remaining = _materialStorage.GetMaterialAmount(uid, solid.FuelMaterial, storage, localOnly: true) + solid.FractionalMaterial;
            return float.IsFinite(remaining) ? Math.Clamp(remaining / storage.StorageLimit.Value, 0f, 1f) : null;
        }

        if (!TryComp<ChemicalFuelGeneratorAdapterComponent>(uid, out var chemical) ||
            !_solutionContainer.ResolveSolution(uid, chemical.SolutionName, ref chemical.Solution, out var solution) ||
            solution.MaxVolume <= FixedPoint2.Zero)
            return null;

        var fuel = 0f;
        var hasUsableFuelType = false;
        foreach (var (reagent, multiplier) in chemical.Reagents)
        {
            if (!float.IsFinite(multiplier) || multiplier <= 0f)
                continue;

            hasUsableFuelType = true;
            fuel += solution.GetTotalPrototypeQuantity(reagent).Float() +
                chemical.FractionalReagents.GetValueOrDefault(reagent) * FixedPoint2.Epsilon.Float();
        }

        return hasUsableFuelType && float.IsFinite(fuel) ? Math.Clamp(fuel / solution.MaxVolume.Float(), 0f, 1f) : null;
    }
}
