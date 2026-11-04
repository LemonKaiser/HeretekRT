using System.Linq;
using Content.Shared.Atmos.Components;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._Mono.Fuel;

/// <summary>
/// Not compatible with multiple material compositions.
/// Prefer to use without stacks when possible.
/// </summary>
public sealed partial class MaterialFuelSystem : EntitySystem
{
    [Dependency] private SharedStackSystem _stackSystem = default!;
    [Dependency] private INetManager _net = default!;

    private float _updateInterval = 1f;
    private float _updateAccumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MaterialFuelComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<MaterialFuelComponent, StackSplitEvent>(OnSplit);
        SubscribeLocalEvent<MaterialFuelComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
    }

    private void OnAfterHandleState(Entity<MaterialFuelComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateComposition(ent);
    }

    public void SetRemainingMaterial(Entity<MaterialFuelComponent> ent, int remaining)
    {
        ent.Comp.RemainingMaterial = remaining;
        UpdateComposition(ent);
        Dirty(ent);
    }

    private void UpdateComposition(Entity<MaterialFuelComponent> ent)
    {
        if (ent.Comp.Material is { } material && TryComp<PhysicalCompositionComponent>(ent, out var composition))
            composition.MaterialComposition[material] = ent.Comp.RemainingMaterial ?? ent.Comp.BaseMaterialComposition;
    }

    private void OnSplit(EntityUid uid, MaterialFuelComponent fuel, ref StackSplitEvent args)
    {
        if (!TryComp<MaterialFuelComponent>(args.NewId, out var splitFuel))
            return;

        SetRemainingMaterial((args.NewId, splitFuel), fuel.RemainingMaterial ?? fuel.BaseMaterialComposition);
        SetRemainingMaterial((uid, fuel), fuel.BaseMaterialComposition);
    }

    private void OnInit(EntityUid uid, MaterialFuelComponent component, MapInitEvent args)
    {
        if (!TryComp<PhysicalCompositionComponent>(uid, out var composition))
            return;

        component.Material ??= composition.MaterialComposition.First().Key;
        if (component.BaseMaterialComposition <= 0)
            component.BaseMaterialComposition = composition.MaterialComposition[component.Material];
        SetRemainingMaterial((uid, component), component.RemainingMaterial ?? composition.MaterialComposition[component.Material]);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;
        _updateAccumulator += frameTime;
        if (_updateAccumulator < _updateInterval)
            return;
        _updateAccumulator -= _updateInterval;

        var eqe = EntityQueryEnumerator<MaterialFuelComponent, FlammableComponent>();
        while (eqe.MoveNext(out var uid, out var fuel, out var flame))
        {
            if (!flame.OnFire)
                continue;

            Burn(uid, fuel);
        }
    }

    private void Burn(EntityUid uid, MaterialFuelComponent fuel)
    {
        var remaining = (fuel.RemainingMaterial ?? fuel.BaseMaterialComposition) - fuel.ConsumptionRate;
        if (remaining <= 0)
        {
            if (!TryComp<StackComponent>(uid, out var stack) || stack.Count <= 1)
            {
                QueueDel(uid);
                return;
            }

            _stackSystem.SetCount(uid, stack.Count - 1, stack);
            remaining += fuel.BaseMaterialComposition;
        }
        SetRemainingMaterial((uid, fuel), remaining);
    }

    public int CalculateComposition(EntityUid uid,
        MaterialFuelComponent? fuel,
        string mat,
        int multiplier,
        int volume)
    {
        if (multiplier <= 0)
            return 0;

        if (!Resolve(uid, ref fuel, false) ||
            fuel.Material != mat)
            return multiplier * volume;

        return (fuel.RemainingMaterial ?? fuel.BaseMaterialComposition) + (multiplier - 1) * fuel.BaseMaterialComposition;
    }
}
