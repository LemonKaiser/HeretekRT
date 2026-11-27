using Content.Shared.Body.Systems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared._Shitmed.Body.Organ;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Common bookkeeping for augment power sources: supports both an implanted
/// <see cref="AugmentPowerCellSlotComponent"/> organ on organic bodies and the native chassis
/// <see cref="PowerCellSlotComponent"/> on Cybernetic (Mechanicus) or IPC bodies.
/// </summary>
public abstract class SharedAugmentPowerCellSystem : EntitySystem
{
    [Dependency] protected readonly AugmentSystem Augment = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] protected readonly SharedPowerCellSystem PowerCell = default!;

    private EntityQuery<PowerCellDrawComponent> _drawQuery;
    private EntityQuery<PowerCellSlotComponent> _slotQuery;

    public override void Initialize()
    {
        _drawQuery = GetEntityQuery<PowerCellDrawComponent>();
        _slotQuery = GetEntityQuery<PowerCellSlotComponent>();

        SubscribeLocalEvent<AugmentPowerCellSlotComponent, OrganEnableChangedEvent>(OnEnableChanged);
        SubscribeLocalEvent<AugmentPowerCellSlotComponent, PowerCellSlotEmptyEvent>(OnCellEmpty);
        SubscribeLocalEvent<InstalledAugmentsComponent, PowerCellSlotEmptyEvent>(OnBodyCellEmpty);
    }

    private void OnEnableChanged(Entity<AugmentPowerCellSlotComponent> ent, ref OrganEnableChangedEvent args)
    {
        if (!_drawQuery.TryComp(ent, out var draw))
            return;

        UpdateDrawRate((ent.Owner, draw));
        PowerCell.SetDrawEnabled((ent.Owner, draw), args.Enabled);

        if (Augment.GetBody(ent) is not { } body)
            return;

        if (args.Enabled && PowerCell.HasDrawCharge(ent.Owner, draw))
        {
            var gained = new AugmentGainedPowerEvent(body);
            Augment.RelayEvent(body, ref gained);
        }
        else
        {
            var lost = new AugmentLostPowerEvent(body);
            Augment.RelayEvent(body, ref lost);
        }
    }

    private void OnCellEmpty(Entity<AugmentPowerCellSlotComponent> ent, ref PowerCellSlotEmptyEvent args)
    {
        if (Augment.GetBody(ent) is not { } body)
            return;

        var lost = new AugmentLostPowerEvent(body);
        Augment.RelayEvent(body, ref lost);
        UpdateDrawRate(ent.Owner);
    }

    private void OnBodyCellEmpty(Entity<InstalledAugmentsComponent> body, ref PowerCellSlotEmptyEvent args)
    {
        // Only cut augment power if the body is relying on its own chassis cell rather than an implanted slot.
        if (GetBodyAugment(body.Owner) != null)
            return;

        var lost = new AugmentLostPowerEvent(body.Owner);
        Augment.RelayEvent(body, ref lost);
        UpdateDrawRate(body.Owner);
    }

    public float GetBodyDraw(EntityUid body)
    {
        var ev = new GetAugmentsPowerDrawEvent(body);
        Augment.RelayEvent(body, ref ev);
        return ev.TotalDraw;
    }

    public void UpdateDrawRate(Entity<PowerCellDrawComponent?> ent)
    {
        if (!_drawQuery.Resolve(ent, ref ent.Comp, false))
            return;

        var isAugmentSlot = HasComp<AugmentPowerCellSlotComponent>(ent);
        var body = isAugmentSlot
            ? Augment.GetBody(ent)
            : _slotQuery.HasComp(ent)
                ? ent.Owner
                : (EntityUid?) null;
        var rate = body != null ? GetBodyDraw(body.Value) : 0f;

        if (!isAugmentSlot)
            PowerCell.SetDrawEnabled((ent.Owner, ent.Comp), rate > 0f);

        if (MathF.Abs(ent.Comp.DrawRate - rate) < 0.0001f)
            return;

        ent.Comp.DrawRate = rate;
        Dirty(ent, ent.Comp);
        PowerCell.QueueUpdate(ent);
    }

    public Entity<AugmentPowerCellSlotComponent>? GetBodyAugment(EntityUid body)
    {
        foreach (var organ in _body.GetBodyOrganEntityComps<AugmentPowerCellSlotComponent>(body))
        {
            return (organ.Owner, organ.Comp1);
        }

        return null;
    }

    /// <summary>
    /// Resolves the entity providing augment power for a body: an implanted
    /// <see cref="AugmentPowerCellSlotComponent"/> organ if present, otherwise the body's own
    /// chassis <see cref="PowerCellSlotComponent"/> (Cybernetic / IPC).
    /// </summary>
    public EntityUid? GetPowerSource(EntityUid body)
    {
        if (GetBodyAugment(body) is { } organ)
            return organ.Owner;

        if (_slotQuery.HasComp(body))
            return body;

        return null;
    }
}
