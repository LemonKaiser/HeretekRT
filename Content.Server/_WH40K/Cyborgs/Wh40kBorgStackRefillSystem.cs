using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stack;
using Content.Shared._WH40K.Cyborgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Restocks <see cref="Wh40kBorgStackRefillComponent"/> stacks one unit every 5 seconds from the cyborg's cell,
/// keeping the final 25% of battery capacity reserved so restocking cannot drain the cyborg dry.
/// </summary>
public sealed partial class Wh40kBorgStackRefillSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private StackSystem _stack = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Share of cell capacity that stack refilling never spends.
    /// </summary>
    public const float Reserve = 0.25f;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        RefillAll();
    }

    /// <summary>
    /// Adds one unit to every short refilling stack carried by or stowed inside a powered cyborg.
    /// </summary>
    public void RefillAll()
    {
        var query = EntityQueryEnumerator<Wh40kBorgStackRefillComponent, StackComponent>();
        while (query.MoveNext(out var uid, out var refill, out var stack))
        {
            if (stack.Count >= refill.Target
                || GetChassis(uid) is not { } chassis
                || !_powerCell.TryGetBatteryFromSlot(chassis, out var cell, out var battery)
                || battery.CurrentCharge - refill.ChargePerUnit < battery.MaxCharge * Reserve
                || !_battery.TryUseCharge(cell.Value, refill.ChargePerUnit, battery))
                continue;

            _stack.SetCount(uid, stack.Count + 1, stack);
        }
    }

    private EntityUid? GetChassis(EntityUid stack)
    {
        var parent = Transform(stack).ParentUid;
        if (HasComp<BorgChassisComponent>(parent))
            return parent;

        if (!parent.IsValid())
            return null;

        var chassis = Transform(parent).ParentUid;
        return HasComp<BorgChassisComponent>(chassis) ? chassis : null;
    }
}
