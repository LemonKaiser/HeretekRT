using System.Numerics;
using Content.Shared._WH40K.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.ShipShields;

public sealed class BastionShieldSystem : EntitySystem
{
    public const int MaxImpacts = 4;
    private static readonly TimeSpan ImpactLifetime = TimeSpan.FromSeconds(2.8);

    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, List<Impact>> _impacts = new();
    private BastionShieldOverlay _overlay = default!;

    public override void Initialize()
    {
        _overlay = new BastionShieldOverlay();
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<BastionShieldImpactEvent>(OnImpact);
        SubscribeLocalEvent<BastionShieldComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        _overlay.Dispose();
        _impacts.Clear();
    }

    private void OnImpact(BastionShieldImpactEvent args)
    {
        if (!TryGetEntity(args.Shield, out var uid) || !HasComp<BastionShieldComponent>(uid) ||
            _timing.CurTime - args.Time > ImpactLifetime)
            return;

        if (!_impacts.TryGetValue(uid.Value, out var impacts))
        {
            impacts = new List<Impact>(MaxImpacts);
            _impacts.Add(uid.Value, impacts);
        }

        if (impacts.Count == MaxImpacts)
            impacts.RemoveAt(0);

        impacts.Add(new Impact(args.Position, args.Time));
    }

    public Vector4 GetImpact(EntityUid uid, int index, Vector2 center, Vector2 radii)
    {
        if (!_impacts.TryGetValue(uid, out var impacts) || index >= impacts.Count)
            return new Vector4(0f, 0f, 10f, 0f);

        var impact = impacts[index];
        var age = (float) (_timing.CurTime - impact.Time).TotalSeconds;
        if (age < 0f || age > ImpactLifetime.TotalSeconds)
            return new Vector4(0f, 0f, 10f, 0f);

        var point = (impact.Position - center) / radii;
        return new Vector4(point, age, 1f);
    }

    private void OnShutdown(EntityUid uid, BastionShieldComponent component, ComponentShutdown args)
    {
        _impacts.Remove(uid);
        _overlay.RemoveShield(uid);
    }

    private readonly record struct Impact(Vector2 Position, TimeSpan Time);
}
