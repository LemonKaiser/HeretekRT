using Content.Server.Ame.Components;
using Content.Shared.Ame.Components;

namespace Content.Server.Ame.EntitySystems;

public sealed partial class AmeControllerSystem
{
    public float? GetLocalFuelFraction(AmeControllerComponent controller)
    {
        if (!TryComp<AmeFuelContainerComponent>(controller.FuelSlot.Item, out var fuel))
            return 0f;

        return fuel.FuelCapacity > 0 ? Math.Clamp((float) fuel.FuelAmount / fuel.FuelCapacity, 0f, 1f) : null;
    }
}
