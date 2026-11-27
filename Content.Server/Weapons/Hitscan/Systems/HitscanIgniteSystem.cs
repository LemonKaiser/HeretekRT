using Content.Server.Atmos.EntitySystems;
using Content.Server.Weapons.Hitscan.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Random;

namespace Content.Server.Weapons.Hitscan.Systems;

public sealed partial class HitscanIgniteSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private FlammableSystem _flammable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanIgniteComponent, HitscanRaycastFiredEvent>(OnHitscanHit);
    }

    private void OnHitscanHit(Entity<HitscanIgniteComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled)
            return;

        foreach (var target in args.HitEntities)
        {
            if (!_robustRandom.Prob(ent.Comp.IgniteChance))
                continue;

            if (TryComp<FlammableComponent>(target, out var flammable))
                _flammable.AdjustFireStacks(target, ent.Comp.FireStacks, flammable, true);
        }
    }
}
