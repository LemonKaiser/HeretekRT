using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed partial class Wh40kNutrimentPumpSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<Wh40kNutrimentPumpComponent, HungerComponent>();
        while (query.MoveNext(out var uid, out var pump, out var hunger))
        {
            if (now < pump.NextCheck)
                continue;

            pump.NextCheck = now + pump.Interval;
            Feed((uid, pump), hunger);
        }
    }

    public bool Feed(Entity<Wh40kNutrimentPumpComponent> ent, HungerComponent hunger)
    {
        if (_mobState.IsDead(ent) || _hunger.GetHungerThreshold(hunger) > ent.Comp.Threshold)
            return false;

        _hunger.ModifyHunger(ent, ent.Comp.Amount, hunger);
        _popup.PopupEntity(Loc.GetString("wh40k-augment-nutriment-pump-feed"), ent, ent);
        return true;
    }
}
