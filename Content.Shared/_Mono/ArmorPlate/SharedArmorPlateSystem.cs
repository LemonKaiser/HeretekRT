using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
/// Handles all armor plate behavior
/// </summary>
public sealed partial class SharedArmorPlateSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorPlateHolderComponent, EntInsertedIntoContainerMessage>(OnPlateInserted);
        SubscribeLocalEvent<ArmorPlateHolderComponent, EntRemovedFromContainerMessage>(OnPlateRemoved);
        SubscribeLocalEvent<ArmorPlateHolderComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ArmorPlateHolderComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<ArmorPlateItemComponent, GetVerbsEvent<ExamineVerb>>(OnPlateVerbExamine);
        SubscribeLocalEvent<ArmorPlateItemComponent, EntityTerminatingEvent>(OnPlateDestroyed);
        SubscribeLocalEvent<ArmorPlateItemComponent, ExaminedEvent>(OnPlateExamined);
    }

    /// <summary>
    /// Transfers armor wear to the active plate. Plates extend durability without adding a damage-resistance layer.
    /// </summary>
    public float AbsorbArmorWear(EntityUid armor, DamageSpecifier absorbedDamage, float wear)
    {
        if (wear <= 0f || !TryGetActivePlate((armor, null), out var plate) ||
            plate.Comp.MaxDurability <= 0 || !TryComp<DamageableComponent>(plate, out var damageable))
        {
            return wear;
        }

        var available = MathF.Max(0f, plate.Comp.MaxDurability - damageable.TotalDamage.Float());
        var total = absorbedDamage.GetTotal().Float();
        if (available <= 0f || total <= 0f)
            return wear;

        var spent = 0f;
        var transferred = 0f;
        foreach (var (type, amount) in absorbedDamage.DamageDict)
        {
            var ratio = Math.Clamp(plate.Comp.AbsorptionRatios.GetValueOrDefault(type), 0f, 1f);
            var multiplier = plate.Comp.DamageToPlate.GetValueOrDefault(type);
            if (!float.IsFinite(ratio) || ratio <= 0f || !float.IsFinite(multiplier) || multiplier <= 0f)
                continue;

            var cost = MathF.Min(available - spent, wear * amount.Float() / total * ratio * multiplier);
            spent += cost;
            transferred += cost / multiplier;
        }

        if (spent > 0f)
        {
            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = FixedPoint2.New(spent);
            var applied = _damageable.TryChangeDamage(plate.Owner, damage, ignoreResistances: true);
            // Damage attempts may be cancelled. Only wear actually paid by the plate leaves the carrier.
            transferred *= Math.Clamp((applied?.GetTotal().Float() ?? 0f) / spent, 0f, 1f);
        }

        return MathF.Max(0f, wear - transferred);
    }

    private void OnPlateInserted(Entity<ArmorPlateHolderComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StorageComponent.ContainerId)
            return;

        var insertedEntity = args.Entity;

        if (!TryComp<ArmorPlateItemComponent>(insertedEntity, out var plateComp))
            return;

        var holder = ent.Comp;

        if (holder.ActivePlate == null)
        {
            SetActivePlate(ent, insertedEntity, plateComp, holder);
        }
    }

    private void OnPlateRemoved(Entity<ArmorPlateHolderComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != StorageComponent.ContainerId)
            return;

        var removedEntity = args.Entity;
        var holder = ent.Comp;

        if (holder.ActivePlate != removedEntity)
            return;

        ClearActivePlate(ent, holder);

        if (TryComp<StorageComponent>(ent, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
            {
                if (TryComp<ArmorPlateItemComponent>(item, out var plateComp))
                {
                    SetActivePlate(ent, item, plateComp, holder);
                    break;
                }
            }
        }
    }

    private void OnExamined(Entity<ArmorPlateHolderComponent> ent, ref ExaminedEvent args)
    {
        var holder = ent.Comp;

        if (!TryComp<StorageComponent>(ent, out _))
        {
            args.PushMarkup(Loc.GetString("armor-plate-examine-no-storage"));
            return;
        }

        if (holder.ActivePlate == null)
        {
            args.PushMarkup(Loc.GetString("armor-plate-examine-no-plate"));
            return;
        }

        var plateName = MetaData(holder.ActivePlate.Value).EntityName;

        if (!TryComp<ArmorPlateItemComponent>(holder.ActivePlate.Value, out var plateItem))
        {
            args.PushMarkup(Loc.GetString("armor-plate-examine-with-plate-simple", ("plateName", plateName)));
            return;
        }

        if (plateItem.MaxDurability > 0 && TryComp<DamageableComponent>(holder.ActivePlate.Value, out var damageable))
        {
            var totalDamage = damageable.TotalDamage.Int();
            var maxDurability = plateItem.MaxDurability;

            var durabilityPercent = ((maxDurability - totalDamage) / (float)maxDurability) * 100f;
            durabilityPercent = Math.Clamp(durabilityPercent, 0f, 100f);

            var durabilityColor = durabilityPercent switch
            {
                > 66f => "green",
                >= 33f => "yellow",
                _ => "red",
            };

            args.PushMarkup(Loc.GetString("armor-plate-examine-with-plate",
                ("plateName", plateName),
                ("percent", (int)durabilityPercent),
                ("durabilityColor", durabilityColor)));
        }
        else
        {
            args.PushMarkup(Loc.GetString("armor-plate-examine-with-plate-simple", ("plateName", plateName)));
        }
    }

    private void OnRefreshMoveSpeed(EntityUid uid, ArmorPlateHolderComponent component, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        args.Args.ModifySpeed(component.WalkSpeedModifier, component.SprintSpeedModifier);
    }

    /// <summary>
    /// Sets the active plate and updates speed modifiers.
    /// </summary>
    private void SetActivePlate(EntityUid holderUid, EntityUid plateUid, ArmorPlateItemComponent plateComp, ArmorPlateHolderComponent holder)
    {
        holder.ActivePlate = plateUid;
        holder.WalkSpeedModifier = plateComp.WalkSpeedModifier;
        holder.SprintSpeedModifier = plateComp.SprintSpeedModifier;

        Dirty(holderUid, holder);
        RefreshMovementSpeed(holderUid);
    }

    /// <summary>
    /// Clears the active plate and resets speed modifiers.
    /// </summary>
    private void ClearActivePlate(EntityUid holderUid, ArmorPlateHolderComponent holder)
    {
        holder.ActivePlate = null;
        holder.WalkSpeedModifier = 1.0f;
        holder.SprintSpeedModifier = 1.0f;

        Dirty(holderUid, holder);
        RefreshMovementSpeed(holderUid);
    }

    /// <summary>
    /// Refreshes movement speed for the entity wearing this armor.
    /// </summary>
    private void RefreshMovementSpeed(EntityUid armorUid)
    {
        if (_inventory.TryGetContainingEntity(armorUid, out var wearer))
        {
            _movementSpeed.RefreshMovementSpeedModifiers(wearer.Value);
        }
    }

    /// <summary>
    /// Tries to get the active plate from an armor holder.
    /// </summary>
    public bool TryGetActivePlate(Entity<ArmorPlateHolderComponent?> holder, out Entity<ArmorPlateItemComponent> plate)
    {
        plate = default;

        if (!Resolve(holder, ref holder.Comp, logMissing: false))
            return false;

        if (holder.Comp.ActivePlate == null)
            return false;

        if (TerminatingOrDeleted(holder.Comp.ActivePlate.Value) ||
            !TryComp<ArmorPlateItemComponent>(holder.Comp.ActivePlate.Value, out var plateComp))
            return false;

        plate = (holder.Comp.ActivePlate.Value, plateComp);
        return true;
    }

    /// <summary>
    /// Examine tooltip handler
    /// </summary>
    private void OnPlateVerbExamine(EntityUid uid, ArmorPlateItemComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var examineMarkup = GetPlateExamine(component);

        var ev = new ArmorExamineEvent(examineMarkup);
        RaiseLocalEvent(uid, ref ev);

        _examine.AddDetailedExamineVerb(args, component, examineMarkup,
            Loc.GetString("armor-plate-examinable-verb-text"),
            "/Textures/Interface/VerbIcons/dot.svg.192dpi.png",
            Loc.GetString("armor-plate-examinable-verb-message"));
    }

    //Speed tooltip generating method
    private void AddSpeedDisplay(FormattedMessage msg, string gaitType, float speedCalc)
    {
        var stringClause = MathF.Sign(speedCalc);

        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("armor-plate-speed-display",
            ("gait", gaitType),
            ("stringClause", stringClause),
            ("speedPercent", Math.Abs(speedCalc))
        ));
    }

    private FormattedMessage GetPlateExamine(ArmorPlateItemComponent plate)
    {
        //Examine header
        var msg = new FormattedMessage();
        msg.AddMarkupOrThrow(Loc.GetString("armor-plate-attributes-examine"));
        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("armor-plate-wear-buffer"));

        //Durability info (if plate can break)
        if (plate.MaxDurability != -1)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString("armor-plate-initial-durability",
                ("durability", plate.MaxDurability)
            ));
        }

        //Speed (if it is affected)
        var walkModifierCalc = MathF.Round((plate.WalkSpeedModifier - 1.0f) * 100f, 1);
        var sprintModifierCalc = MathF.Round((plate.SprintSpeedModifier - 1.0f) * 100f, 1);

        if (!(walkModifierCalc == 0.0f && sprintModifierCalc == 0.0f))
        {
            if (MathHelper.CloseTo(walkModifierCalc, sprintModifierCalc, 0.5f))
            {
                AddSpeedDisplay(msg, Loc.GetString("armor-plate-gait-speed"), walkModifierCalc);
            }
            else
            {
                AddSpeedDisplay(msg, Loc.GetString("armor-plate-gait-sprint"), sprintModifierCalc);
                AddSpeedDisplay(msg, Loc.GetString("armor-plate-gait-walk"), walkModifierCalc);
            }
        }

        foreach (var (type, ratio) in plate.AbsorptionRatios)
        {
            //Damage absorption per type
            msg.PushNewline();

            var dmgType = Loc.GetString("armor-damage-type-" + type.ToLower());
            var ratioPercent = MathF.Round(ratio * 100, 1);

            var stringClause = MathF.Sign(ratio);

            msg.AddMarkupOrThrow(Loc.GetString("armor-plate-ratios-display",
                ("stringClause", stringClause),
                ("dmgType", dmgType),
                ("ratioPercent", Math.Abs(ratioPercent))
            ));

            //Append damagetoplate information to the current absorption line (if plate can break)
            var multiplier = plate.DamageToPlate.GetValueOrDefault(type, 0f);

            if (plate.MaxDurability == -1)
                continue;

            if(multiplier > 0)
            {
                var multiplierPercent = MathF.Round(multiplier * 100, 1);
                msg.AddMarkupOrThrow(" " + Loc.GetString("armor-plate-multiplier-display",
                    ("multiplier", multiplierPercent),
                    ("dmgType", dmgType)));
            }
            else
            {
                msg.AddMarkupOrThrow(" " + Loc.GetString("armor-plate-multiplier-none"));
            }
        }

        return msg;
    }

    private void OnPlateDestroyed(Entity<ArmorPlateItemComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_container.TryGetContainingContainer(ent.Owner, out var container))
            return;

        var holderUid = container.Owner;
        if (!TryComp<ArmorPlateHolderComponent>(holderUid, out var holder))
            return;

        if (holder.ActivePlate != ent.Owner)
            return;

        if (holder.ShowBreakPopup)
        {
            if (_inventory.TryGetContainingEntity(holderUid, out var wearer))
            {
                var plateName = MetaData(ent).EntityName;
                _popup.PopupEntity(
                    Loc.GetString("armor-plate-break", ("plateName", plateName)),
                    wearer.Value,
                    wearer.Value,
                    PopupType.MediumCaution
                );
            }
        }
    }

    private void OnPlateExamined(EntityUid uid, ArmorPlateItemComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (component.MaxDurability > 0 && TryComp<DamageableComponent>(uid, out var damageable))
        {
            var totalDamage = damageable.TotalDamage.Int();
            var maxDurability = component.MaxDurability;
            var durabilityPercent = ((maxDurability - totalDamage) / (float)maxDurability) * 100f;
            durabilityPercent = Math.Clamp(durabilityPercent, 0f, 100f);

            var durabilityColor = durabilityPercent switch
            {
                > 66f => "green",
                >= 33f => "yellow",
                _ => "red",
            };

            args.PushMarkup(Loc.GetString("armor-plate-item-durability",
                ("percent", (int)durabilityPercent),
                ("durabilityColor", durabilityColor)));
        }
    }
}
