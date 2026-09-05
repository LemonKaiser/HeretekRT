using Content.Shared.Body.Systems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared._Shitmed.Body.Organ;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Common bookkeeping for a battery installed as an organ. Concrete client/server systems are
/// used so the dependency resolves correctly in both content assemblies.
/// </summary>
public abstract class SharedAugmentPowerCellSystem : EntitySystem
{
    [Dependency] protected readonly AugmentSystem Augment = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] protected readonly SharedPowerCellSystem PowerCell = default!;

    private EntityQuery<PowerCellDrawComponent> _drawQuery;

    public override void Initialize()
    {
        _drawQuery = GetEntityQuery<PowerCellDrawComponent>();

        SubscribeLocalEvent<AugmentPowerCellSlotComponent, OrganEnableChangedEvent>(OnEnableChanged);
        SubscribeLocalEvent<AugmentPowerCellSlotComponent, PowerCellSlotEmptyEvent>(OnCellEmpty);
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

    public float GetBodyDraw(EntityUid body)
    {
        var ev = new GetAugmentsPowerDrawEvent(body);
        Augment.RelayEvent(body, ref ev);
        return ev.TotalDraw;
    }

    public void UpdateDrawRate(Entity<PowerCellDrawComponent?> ent)
    {
        if (!_drawQuery.Resolve(ent, ref ent.Comp))
            return;

        var rate = Augment.GetBody(ent) is { } body ? GetBodyDraw(body) : 0f;
        if (ent.Comp.DrawRate == rate)
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
}
