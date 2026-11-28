using Content.Server.Atmos.EntitySystems;
using Content.Shared._WH40K.SuitTank;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SuitTank;

/// <summary>
/// Automatically switches internals to a fuller non-low tank stored in the worn suit when the current suit tank drops to low pressure.
/// </summary>
public sealed partial class Wh40kSuitTankSwitchSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GasTankSystem _gasTank = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private Wh40kSuitTankStorageSystem _suitTank = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<InternalsComponent>();
        while (query.MoveNext(out var uid, out var internals))
        {
            if (internals.GasTankEntity is not { } tank
                || !TryComp<GasTankComponent>(tank, out var current)
                || !current.IsLowPressure
                || !_suitTank.TryGetWearer(tank, out var wearer)
                || wearer != uid)
            {
                continue;
            }

            if (_suitTank.FindFullestTank(uid, except: tank) is not { } next || next.Comp.IsLowPressure)
                continue;

            _gasTank.DisconnectFromInternals((tank, current), forced: true);
            if (_gasTank.ConnectToInternals(next))
                _popup.PopupEntity(Loc.GetString("wh40k-suit-tank-switched"), uid, uid);
        }
    }
}
