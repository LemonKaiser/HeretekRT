using Content.Server.Destructible;
using Content.Shared._WH40K.StructureIntegrity;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;

namespace Content.Server._WH40K.StructureIntegrity;

/// <summary>
/// Sends structural integrity telemetry for anchored destructible structures on examine so clients display
/// a styled WH40K integrity bar inside the examine tooltip.
/// </summary>
public sealed partial class Wh40kStructureIntegritySystem : EntitySystem
{
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestructibleComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<DestructibleComponent> ent, ref ExaminedEvent args)
    {
        if (!TryGetIntegrity(ent, out var current, out var max))
            return;

        if (!_players.TryGetSessionByEntity(args.Examiner, out var session))
            return;

        RaiseNetworkEvent(new Wh40kStructureIntegrityMessage(GetNetEntity(ent.Owner), current, max), session);
    }

    public bool TryGetIntegrity(Entity<DestructibleComponent> ent, out int current, out int max)
    {
        current = max = 0;
        if (!Transform(ent).Anchored
            || HasComp<MobStateComponent>(ent)
            || !TryComp<DamageableComponent>(ent, out var damageable))
            return false;

        var destroyedAt = _destructible.DestroyedAt(ent, ent.Comp);
        if (destroyedAt == FixedPoint2.MaxValue || destroyedAt <= FixedPoint2.Zero)
            return false;

        max = destroyedAt.Int();
        current = Math.Clamp((int) MathF.Ceiling((destroyedAt - damageable.TotalDamage).Float()), 0, max);
        return max > 0;
    }
}
