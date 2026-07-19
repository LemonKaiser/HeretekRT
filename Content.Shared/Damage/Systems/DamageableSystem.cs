using Content.Shared._Shitmed.Targeting;
// Shitmed Change
using Content.Shared.Armor;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Chemistry;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Durability.Events;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radiation.Events;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using System.Linq;
using static Content.Shared.Damage.DamageableSystem;

namespace Content.Shared.Damage
{
    public sealed partial class DamageableSystem : EntitySystem
    {
        [Dependency] private IPrototypeManager _prototypeManager = default!;
        [Dependency] private SharedAppearanceSystem _appearance = default!;
        [Dependency] private INetManager _netMan = default!;
        [Dependency] private SharedBodySystem _body = default!; // Shitmed Change
        [Dependency] private IRobustRandom _random = default!; // Shitmed Change
        [Dependency] private MobThresholdSystem _mobThreshold = default!;
        [Dependency] private IConfigurationManager _config = default!;
        [Dependency] private SharedChemistryGuideDataSystem _chemistryGuideData = default!;
        [Dependency] private SharedExplosionSystem _explosion = default!;

        private EntityQuery<AppearanceComponent> _appearanceQuery;
        private EntityQuery<DamageableComponent> _damageableQuery;

        public float UniversalAllDamageModifier { get; private set; } = 1f;
        public float UniversalAllHealModifier { get; private set; } = 1f;
        public float UniversalMeleeDamageModifier { get; private set; } = 1f;
        public float UniversalProjectileDamageModifier { get; private set; } = 1f;
        public float UniversalHitscanDamageModifier { get; private set; } = 1f;
        public float UniversalReagentDamageModifier { get; private set; } = 1f;
        public float UniversalReagentHealModifier { get; private set; } = 1f;
        public float UniversalExplosionDamageModifier { get; private set; } = 1f;
        public float UniversalThrownDamageModifier { get; private set; } = 1f;
        public float UniversalTopicalsHealModifier { get; private set; } = 1f;

        public override void Initialize()
        {
            SubscribeLocalEvent<DamageableComponent, ComponentInit>(DamageableInit);
            SubscribeLocalEvent<DamageableComponent, ComponentHandleState>(DamageableHandleState);
            SubscribeLocalEvent<DamageableComponent, ComponentGetState>(DamageableGetState);
            SubscribeLocalEvent<DamageableComponent, OnIrradiatedEvent>(OnIrradiated);
            SubscribeLocalEvent<DamageableComponent, RejuvenateEvent>(OnRejuvenate);

            _appearanceQuery = GetEntityQuery<AppearanceComponent>();
            _damageableQuery = GetEntityQuery<DamageableComponent>();

            // Damage modifier CVars are updated and stored here to be queried in other systems.
            // Note that certain modifiers requires reloading the guidebook.
            Subs.CVar(_config, CCVars.PlaytestAllDamageModifier, value =>
            {
                UniversalAllDamageModifier = value;
                _chemistryGuideData.ReloadAllReagentPrototypes();
                _explosion.ReloadMap();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestAllHealModifier, value =>
            {
                UniversalAllHealModifier = value;
                _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestProjectileDamageModifier, value => UniversalProjectileDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestMeleeDamageModifier, value => UniversalMeleeDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestProjectileDamageModifier, value => UniversalProjectileDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestHitscanDamageModifier, value => UniversalHitscanDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestReagentDamageModifier, value =>
            {
                UniversalReagentDamageModifier = value;
                _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestReagentHealModifier, value =>
            {
                 UniversalReagentHealModifier = value;
                 _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestExplosionDamageModifier, value =>
            {
                UniversalExplosionDamageModifier = value;
                _explosion.ReloadMap();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestThrownDamageModifier, value => UniversalThrownDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestTopicalsHealModifier, value => UniversalTopicalsHealModifier = value, true);
        }

        /// <summary>
        ///     Initialize a damageable component
        /// </summary>
        private void DamageableInit(EntityUid uid, DamageableComponent component, ComponentInit _)
        {
            if (component.DamageContainerID != null &&
                _prototypeManager.TryIndex<DamageContainerPrototype>(component.DamageContainerID,
                out var damageContainerPrototype))
            {
                // Initialize damage dictionary, using the types and groups from the damage
                // container prototype
                foreach (var type in damageContainerPrototype.SupportedTypes)
                {
                    component.Damage.DamageDict.TryAdd(type, FixedPoint2.Zero);
                }

                foreach (var groupId in damageContainerPrototype.SupportedGroups)
                {
                    var group = _prototypeManager.Index<DamageGroupPrototype>(groupId);
                    foreach (var type in group.DamageTypes)
                    {
                        component.Damage.DamageDict.TryAdd(type, FixedPoint2.Zero);
                    }
                }
            }
            else
            {
                // No DamageContainerPrototype was given. So we will allow the container to support all damage types
                foreach (var type in _prototypeManager.EnumeratePrototypes<DamageTypePrototype>())
                {
                    component.Damage.DamageDict.TryAdd(type.ID, FixedPoint2.Zero);
                }
            }

            component.Damage.GetDamagePerGroup(_prototypeManager, component.DamagePerGroup);
            component.TotalDamage = component.Damage.GetTotal();
        }

        /// <summary>
        ///     Directly sets the damage specifier of a damageable component.
        /// </summary>
        /// <remarks>
        ///     Useful for some unfriendly folk. Also ensures that cached values are updated and that a damage changed
        ///     event is raised.
        /// </remarks>
        public void SetDamage(EntityUid uid, DamageableComponent damageable, DamageSpecifier damage)
        {
            damageable.Damage = damage;
            DamageChanged(uid, damageable);
        }

        /// <summary>
        ///     If the damage in a DamageableComponent was changed, this function should be called.
        /// </summary>
        /// <remarks>
        ///     This updates cached damage information, flags the component as dirty, and raises a damage changed event.
        ///     The damage changed event is used by other systems, such as damage thresholds.
        /// </remarks>
        public void DamageChanged(EntityUid uid, DamageableComponent component, DamageSpecifier? damageDelta = null,
            bool interruptsDoAfters = true, EntityUid? origin = null, bool? canSever = null, float armorPenetration = 0f) // Shitmed Change
        {
            component.Damage.GetDamagePerGroup(_prototypeManager, component.DamagePerGroup);
            component.TotalDamage = component.Damage.GetTotal();
            Dirty(uid, component);

            if (_appearanceQuery.TryGetComponent(uid, out var appearance) && damageDelta != null)
            {
                var data = new DamageVisualizerGroupData(component.DamagePerGroup.Keys.ToList());
                _appearance.SetData(uid, DamageVisualizerKeys.DamageUpdateGroups, data, appearance);
            }
            RaiseLocalEvent(uid, new DamageChangedEvent(component, damageDelta, interruptsDoAfters, origin, canSever ?? true)); // Shitmed Change
        }

        // Mono: damage origin flags for if we can't or don't want to discern by UID
        public enum DamageOriginFlag
        {
            Explosion, // flag set by ExplosionSystem.Processing
            Barotrauma // flag set by BarotraumaSystem
        }

        /// <summary>
        ///     Applies damage specified via a <see cref="DamageSpecifier"/>.
        /// </summary>
        /// <remarks>
        ///     <see cref="DamageSpecifier"/> is effectively just a dictionary of damage types and damage values. This
        ///     function just applies the container's resistances (unless otherwise specified) and then changes the
        ///     stored damage data. Division of group damage into types is managed by <see cref="DamageSpecifier"/>.
        /// </remarks>
        /// <returns>
        ///     Returns a <see cref="DamageSpecifier"/> with information about the actual damage changes. This will be
        ///     null if the user had no applicable components that can take damage.
        /// </returns>
        public DamageSpecifier? TryChangeDamage(EntityUid? uid, DamageSpecifier damage, bool ignoreResistances = false,
            bool interruptsDoAfters = true, DamageableComponent? damageable = null, EntityUid? origin = null, bool ignoreGlobalModifiers = false,
            float armorPenetration = 0f,
            // Shitmed Change
            bool? canSever = true, bool? canEvade = false, float? partMultiplier = 1.00f, TargetBodyPart? targetPart = null, EntityUid? tool = null,
            // Mono: arg to ID indirect damage sources
            DamageOriginFlag? originFlag = null)
        {
            if (!uid.HasValue || !_damageableQuery.Resolve(uid.Value, ref damageable, false))
            {
                // TODO BODY SYSTEM pass damage onto body system
                // BOBBY WHEN?
                return null;
            }

            if (damage.Empty)
            {
                return damage;
            }

            var before = new BeforeDamageChangedEvent(damage, origin, targetPart, //Shitmed Change
                false, originFlag); // Mono: originFlag
            RaiseLocalEvent(uid.Value, ref before);

            if (before.Cancelled)
                return null;

            // Shitmed Change Start
            var partDamage = new TryChangePartDamageEvent(damage, origin, targetPart, ignoreResistances, canSever ?? true, canEvade ?? false, partMultiplier ?? 1.00f)
            {
                ResolvedTargetPart = targetPart,
                ArmorPenetration = armorPenetration,
                Tool = tool,
            };
            RaiseLocalEvent(uid.Value, ref partDamage);

            if (partDamage.Evaded || partDamage.Cancelled)
                return null;

            // Shitmed Change End

            // Apply resistances
            if (!ignoreResistances)
            {
                if (damageable.DamageModifierSetId != null &&
                    _prototypeManager.Resolve(damageable.DamageModifierSetId, out var modifierSet))
                {
                    damage = DamageSpecifier.ApplyModifierSet(damage, modifierSet);
                }

                var ev = new DamageModifyEvent(damage,
                    origin,
                    armorPenetration,
                    targetPart,
                    tool,
                    partDamage.ResolvedTargetPart); // Shitmed Change
                RaiseLocalEvent(uid.Value, ev);
                var damageBeforeRating = ev.Damage;
                var armorResult = ArmorRatingMath.Apply(
                    damageBeforeRating,
                    ev.ArmorRating,
                    ev.ArmorPenetration);
                ev.Damage = armorResult.Damage;
                var ratingAbsorbedDamage = ev.CalculateArmorRatingAbsorbedDamage(damageBeforeRating, ev.Damage);
                ev.ArmorAbsorbedDamage = ev.CalculateArmorAbsorbedDamage(ratingAbsorbedDamage);
                var durabilityAbsorbedDamage = ev.SelectArmorDurabilityCandidate(ratingAbsorbedDamage);
                damage = ev.Damage;

                if (_netMan.IsServer &&
                    durabilityAbsorbedDamage > 0f &&
                    ev.ArmorDurabilityCandidate is { } armor &&
                    !TerminatingOrDeleted(armor))
                {
                    RaiseLocalEvent(armor,
                        new ArmorProtectionAppliedEvent(ev.ArmorTargetPart, durabilityAbsorbedDamage));
                }

                if (damage.Empty)
                {
                    return damage;
                }
            }

            if (!ignoreGlobalModifiers)
                damage = ApplyUniversalAllModifiers(damage);

            var delta = new DamageSpecifier();
            delta.DamageDict.EnsureCapacity(damage.DamageDict.Count);

            var dict = damageable.Damage.DamageDict;
            foreach (var (type, value) in damage.DamageDict)
            {
                // CollectionsMarshal my beloved.
                if (!dict.TryGetValue(type, out var oldValue))
                    continue;

                var newValue = FixedPoint2.Max(FixedPoint2.Zero, oldValue + value);
                if (newValue == oldValue)
                    continue;

                dict[type] = newValue;
                delta.DamageDict[type] = newValue - oldValue;
            }

            if (delta.DamageDict.Count > 0)
                DamageChanged(uid.Value, damageable, delta, interruptsDoAfters, origin, canSever); // Shitmed Change

            return delta;
        }

        /// <summary>
        /// Applies the two univeral "All" modifiers, if set.
        /// Individual damage source modifiers are set in their respective code.
        /// </summary>
        /// <param name="damage">The damage to be changed.</param>
        public DamageSpecifier ApplyUniversalAllModifiers(DamageSpecifier damage)
        {
            // Checks for changes first since they're unlikely in normal play.
            if (UniversalAllDamageModifier == 1f && UniversalAllHealModifier == 1f)
                return damage;

            foreach (var (key, value) in damage.DamageDict)
            {
                if (value == 0)
                    continue;

                if (value > 0)
                {
                    damage.DamageDict[key] *= UniversalAllDamageModifier;
                    continue;
                }

                if (value < 0)
                {
                    damage.DamageDict[key] *= UniversalAllHealModifier;
                }
            }

            return damage;
        }

        /// <summary>
        ///     Sets all damage types supported by a <see cref="DamageableComponent"/> to the specified value.
        /// </summary>
        /// <remakrs>
        ///     Does nothing If the given damage value is negative.
        /// </remakrs>
        public void SetAllDamage(EntityUid uid, DamageableComponent component, FixedPoint2 newValue)
        {
            if (newValue < 0)
            {
                // invalid value
                return;
            }

            foreach (var type in component.Damage.DamageDict.Keys)
            {
                component.Damage.DamageDict[type] = newValue;
            }

            // Setting damage does not count as 'dealing' damage, even if it is set to a larger value, so we pass an
            // empty damage delta.
            DamageChanged(uid, component, new DamageSpecifier());

            // Shitmed Change Start
            if (HasComp<TargetingComponent>(uid))
            {
                foreach (var (part, _) in _body.GetBodyChildren(uid))
                {
                    if (!TryComp(part, out DamageableComponent? damageComp))
                        continue;

                    SetAllDamage(part, damageComp, newValue);
                }
            }
            // Shitmed Change End
        }

        public void SetDamageModifierSetId(EntityUid uid, string damageModifierSetId, DamageableComponent? comp = null)
        {
            if (!_damageableQuery.Resolve(uid, ref comp))
                return;

            comp.DamageModifierSetId = damageModifierSetId;
            Dirty(uid, comp);
        }

        private void DamageableGetState(EntityUid uid, DamageableComponent component, ref ComponentGetState args)
        {
            if (_netMan.IsServer)
            {
                args.State = new DamageableComponentState(component.Damage.DamageDict, component.DamageContainerID, component.DamageModifierSetId, component.HealthBarThreshold);
            }
            else
            {
                // avoid mispredicting damage on newly spawned entities.
                args.State = new DamageableComponentState(component.Damage.DamageDict.ShallowClone(), component.DamageContainerID, component.DamageModifierSetId, component.HealthBarThreshold);
            }
        }

        private void OnIrradiated(EntityUid uid, DamageableComponent component, OnIrradiatedEvent args)
        {
            var damageValue = FixedPoint2.New(args.TotalRads);

            // Radiation should really just be a damage group instead of a list of types.
            DamageSpecifier damage = new();
            foreach (var typeId in component.RadiationDamageTypeIDs)
            {
                damage.DamageDict.Add(typeId, damageValue);
            }

            TryChangeDamage(uid, damage, interruptsDoAfters: false);
        }

        private void OnRejuvenate(EntityUid uid, DamageableComponent component, RejuvenateEvent args)
        {
            TryComp<MobThresholdsComponent>(uid, out var thresholds);
            _mobThreshold.SetAllowRevives(uid, true, thresholds); // do this so that the state changes when we set the damage
            SetAllDamage(uid, component, 0);
            _mobThreshold.SetAllowRevives(uid, false, thresholds);
        }

        private void DamageableHandleState(EntityUid uid, DamageableComponent component, ref ComponentHandleState args)
        {
            if (args.Current is not DamageableComponentState state)
            {
                return;
            }

            component.DamageContainerID = state.DamageContainerId;
            component.DamageModifierSetId = state.ModifierSetId;
            component.HealthBarThreshold = state.HealthBarThreshold;

            // Has the damage actually changed?
            DamageSpecifier newDamage = new() { DamageDict = new(state.DamageDict) };
            var delta = component.Damage - newDamage;
            delta.TrimZeros();

            if (!delta.Empty)
            {
                component.Damage = newDamage;
                DamageChanged(uid, component, delta);
            }
        }

        /// <summary>
        /// Goes through an entity damage's and saves them inside a dictionary if the value is higher than 0
        /// The dictionary is structured with a string for the name of the damage type, and a FixedPoint2 for the numeric damage value
        /// </summary>
        public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> GetDamages(Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2> damagePerGroup, DamageSpecifier damage)
        {
            var damageTypes = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2>();

            foreach (var (damageGroupId, _) in damagePerGroup)  //go through each group
            {
                var group = _prototypeManager.Index<DamageGroupPrototype>(damageGroupId);  //get group
                foreach (var type in group.DamageTypes) //go through each type inside that group
                {
                    if (!damage.DamageDict.TryGetValue(type, out var damageValue) || damageValue == 0) //get value and make sure it isn't 0
                        continue;

                    damageTypes.Add(type, damageValue);
                }
            }
            return damageTypes;
        }
    }


    /// <summary>
    ///     Raised before damage is done, so stuff can cancel it if necessary.
    /// </summary>
    [ByRefEvent]
    public record struct BeforeDamageChangedEvent(
        DamageSpecifier Damage,
        EntityUid? Origin = null,
        TargetBodyPart? TargetPart = null, // Shitmed Change
        bool Cancelled = false,
        DamageOriginFlag? OriginFlag = null); // Mono: OriginFlag

    /// <summary>
    ///     Shitmed Change: Raised on parts before damage is done so we can cancel the damage if they evade.
    /// </summary>
    [ByRefEvent]
    public record struct TryChangePartDamageEvent(
        DamageSpecifier Damage,
        EntityUid? Origin = null,
        TargetBodyPart? TargetPart = null,
        bool IgnoreResistances = false,
        bool CanSever = true,
        bool CanEvade = false,
        float PartMultiplier = 1.00f,
        bool Evaded = false,
        bool Cancelled = false)
    {
        /// <summary>
        /// Body targeting can resolve a concrete body part even when the original damage call did not specify one.
        /// This is kept separate from <see cref="TargetPart"/> so existing body damage calculations are unchanged.
        /// </summary>
        public TargetBodyPart? ResolvedTargetPart;

        /// <summary>
        /// Flat armor penetration that must be preserved when damage is relayed to a concrete body part.
        /// </summary>
        public float ArmorPenetration;

        /// <summary>
        /// Tool that caused the damage, preserved for armor and downstream damage handlers on a body part.
        /// </summary>
        public EntityUid? Tool;
    }

    /// <summary>
    ///     Raised on an entity when damage is about to be dealt,
    ///     in case anything else needs to modify it other than the base
    ///     damageable component.
    ///
    ///     For example, armor.
    /// </summary>
    public sealed class DamageModifyEvent : EntityEventArgs, IInventoryRelayEvent
    {
        // Whenever locational damage is a thing, this should just check only that bit of armour.
        public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

        public readonly DamageSpecifier OriginalDamage;
        public DamageSpecifier Damage;
        public EntityUid? Origin;
        /// <summary>
        ///     Flat armor points ignored by the incoming damage. Positive values reduce ArmorRating;
        ///     negative values increase effective armor.
        /// </summary>
        public float ArmorPenetration; // Goobstation
        public readonly TargetBodyPart? TargetPart; // Shitmed Change
        public EntityUid? Tool;

        /// <summary>
        /// Resolved body part used for armor coverage, numerical rating and durability selection.
        /// </summary>
        public readonly TargetBodyPart? ArmorTargetPart;

        /// <summary>
        /// Body-part damage relays armor a second time. Those relays still apply protection, but must not spend
        /// item durability again; the owning body's damage event handles the single durability cost.
        /// </summary>
        public bool TrackArmorDurability = true;

        /// <summary>
        /// Best matching protective item selected while the event is relayed through equipped armor.
        /// </summary>
        public EntityUid? ArmorDurabilityCandidate { get; private set; }

        /// <summary>
        /// Strongest matching numerical armor selected by equipped armor relays.
        /// </summary>
        public EntityUid? ArmorRatingCandidate { get; private set; }

        public float ArmorRating { get; private set; }
        public float ArmorAbsorbedDamage;

        private float _armorModifierAbsorbedDamage;
        private int _armorRatingPriority = int.MaxValue;
        private EntityUid? _armorRatingDurabilityCandidate;
        private float _armorRatingDurabilityRating = float.MinValue;
        private int _armorRatingDurabilityPriority = int.MaxValue;
        private EntityUid? _armorModifierDurabilityCandidate;
        private float _armorModifierDurabilityRating = float.MinValue;
        private int _armorModifierDurabilityPriority = int.MaxValue;

        public DamageModifyEvent(
            DamageSpecifier damage,
            EntityUid? origin = null,
            float armorPenetration = 0,
            TargetBodyPart? targetPart = null,
            EntityUid? tool = null,
            TargetBodyPart? armorTargetPart = null) // Shitmed Change
        {
            OriginalDamage = damage;
            Damage = damage;
            Origin = origin;
            TargetPart = targetPart; // Shitmed Change
            ArmorPenetration = armorPenetration; // Goobstation
            Tool = tool;
            ArmorTargetPart = armorTargetPart ?? targetPart;
        }

        /// <summary>
        /// Keeps the strongest and then most specific matching numerical armor.
        /// </summary>
        public void ConsiderArmorRatingCandidate(EntityUid uid, float armorRating, int priority)
        {
            if (armorRating <= 0f ||
                armorRating < ArmorRating ||
                armorRating == ArmorRating && priority >= _armorRatingPriority)
            {
                return;
            }

            ArmorRatingCandidate = uid;
            ArmorRating = armorRating;
            _armorRatingPriority = priority;
        }

        /// <summary>
        /// Records only the change made by an ArmorComponent, excluding other DamageModifyEvent subscribers.
        /// Vulnerabilities are not treated as absorption and do not subtract from protection provided by another
        /// armor layer.
        /// </summary>
        public float RecordArmorModifier(DamageSpecifier before, DamageSpecifier after)
        {
            var absorbed = CalculatePositiveDamageReduction(before, after);
            _armorModifierAbsorbedDamage += absorbed;
            return absorbed;
        }

        /// <summary>
        /// Returns positive damage prevented by the numerical armor layer alone.
        /// </summary>
        public float CalculateArmorRatingAbsorbedDamage(DamageSpecifier beforeRating, DamageSpecifier afterRating)
        {
            return CalculatePositiveDamageReduction(beforeRating, afterRating);
        }

        /// <summary>
        /// Returns all positive damage absorbed by armor modifiers and the numerical armor layer.
        /// </summary>
        public float CalculateArmorAbsorbedDamage(float ratingAbsorbedDamage)
        {
            return _armorModifierAbsorbedDamage + Math.Max(0f, ratingAbsorbedDamage);
        }

        private static float CalculatePositiveDamageReduction(DamageSpecifier before, DamageSpecifier after)
        {
            var absorbed = 0f;
            foreach (var (damageType, beforeAmount) in before.DamageDict)
            {
                if (beforeAmount <= FixedPoint2.Zero)
                    continue;

                var afterAmount = after.DamageDict.GetValueOrDefault(damageType, FixedPoint2.Zero);
                afterAmount = FixedPoint2.Max(FixedPoint2.Zero, afterAmount);
                if (afterAmount < beforeAmount)
                    absorbed += (beforeAmount - afterAmount).Float();
            }

            return absorbed;
        }

        /// <summary>
        /// Keeps the strongest and then most specific matching armor item so one incoming damage event spends
        /// durability once.
        /// </summary>
        public void ConsiderArmorDurabilityCandidate(
            EntityUid uid,
            float armorRating,
            int priority,
            float modifierAbsorbedDamage)
        {
            if (!TrackArmorDurability)
                return;

            if (armorRating > _armorRatingDurabilityRating ||
                armorRating == _armorRatingDurabilityRating && priority < _armorRatingDurabilityPriority)
            {
                _armorRatingDurabilityCandidate = uid;
                _armorRatingDurabilityRating = armorRating;
                _armorRatingDurabilityPriority = priority;
            }

            if (modifierAbsorbedDamage <= 0f ||
                armorRating < _armorModifierDurabilityRating ||
                armorRating == _armorModifierDurabilityRating && priority >= _armorModifierDurabilityPriority)
            {
                return;
            }

            _armorModifierDurabilityCandidate = uid;
            _armorModifierDurabilityRating = armorRating;
            _armorModifierDurabilityPriority = priority;
        }

        /// <summary>
        /// Selects the one item that spends durability for this root hit and returns the amount attributed to it.
        /// Numerical absorption belongs to the item that supplied the selected rating. If the numerical layer did
        /// not protect from this hit, only an item whose own modifiers absorbed damage may be selected.
        /// </summary>
        public float SelectArmorDurabilityCandidate(float ratingAbsorbedDamage)
        {
            ArmorDurabilityCandidate = null;
            if (!TrackArmorDurability)
                return 0f;

            if (ratingAbsorbedDamage > 0f &&
                ArmorRatingCandidate is { } ratingCandidate &&
                _armorRatingDurabilityCandidate == ratingCandidate)
            {
                ArmorDurabilityCandidate = ratingCandidate;
                return CalculateArmorAbsorbedDamage(ratingAbsorbedDamage);
            }

            if (_armorModifierDurabilityCandidate is not { } modifierCandidate)
                return 0f;

            ArmorDurabilityCandidate = modifierCandidate;
            return _armorModifierAbsorbedDamage;
        }
    }

    public sealed class DamageChangedEvent : EntityEventArgs
    {
        /// <summary>
        ///     This is the component whose damage was changed.
        /// </summary>
        /// <remarks>
        ///     Given that nearly every component that cares about a change in the damage, needs to know the
        ///     current damage values, directly passing this information prevents a lot of duplicate
        ///     Owner.TryGetComponent() calls.
        /// </remarks>
        public readonly DamageableComponent Damageable;

        /// <summary>
        ///     The amount by which the damage has changed. If the damage was set directly to some number, this will be
        ///     null.
        /// </summary>
        public readonly DamageSpecifier? DamageDelta;

        /// <summary>
        ///     Was any of the damage change dealing damage, or was it all healing?
        /// </summary>
        public readonly bool DamageIncreased;

        /// <summary>
        ///     Does this event interrupt DoAfters?
        ///     Note: As provided in the constructor, this *does not* account for DamageIncreased.
        ///     As written into the event, this *does* account for DamageIncreased.
        /// </summary>
        public readonly bool InterruptsDoAfters;

        /// <summary>
        ///     Contains the entity which caused the change in damage, if any was responsible.
        /// </summary>
        public readonly EntityUid? Origin;

        /// <summary>
        ///     Shitmed Change: Can this damage event sever parts?
        /// </summary>
        public readonly bool CanSever;

        public DamageChangedEvent(DamageableComponent damageable, DamageSpecifier? damageDelta, bool interruptsDoAfters, EntityUid? origin, bool canSever = true) // Shitmed Change
        {
            Damageable = damageable;
            DamageDelta = damageDelta;
            Origin = origin;
            CanSever = canSever; // Shitmed Change
            if (DamageDelta == null)
                return;

            foreach (var damageChange in DamageDelta.DamageDict.Values)
            {
                if (damageChange > 0)
                {
                    DamageIncreased = true;
                    break;
                }
            }
            InterruptsDoAfters = interruptsDoAfters && DamageIncreased;
        }
    }

}
