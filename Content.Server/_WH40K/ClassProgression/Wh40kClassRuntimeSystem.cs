using System.Linq;
using Content.Server._WH40K.CharacterCreation;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Buckle.Components;
using Content.Shared.GameTicking;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._WH40K.ItemRarity.Systems;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Projects permanent class entitlements onto the currently attached living body.
/// The projection is rebuilt from cached records and reconciled by effect id, never applied as a blind delta.
/// </summary>
public sealed partial class Wh40kClassRuntimeSystem : EntitySystem
{
    private static readonly TimeSpan MaximumTemporaryEffectDuration = TimeSpan.FromMinutes(10);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedItemRaritySystem _rarity = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private Wh40kClassProgressManager _classProgress = default!;
    [Dependency] private Wh40kProgressManager _accountProgress = default!;
    [Dependency] private Wh40kCharacterStatsSpawnSystem _stats = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private readonly HashSet<EntityUid> _pendingEquipmentReconcile = new();
    private readonly Dictionary<NetUserId, long> _loadGenerations = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, ComponentShutdown>(OnProfileShutdown);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, DidEquipEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, DidUnequipEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, DidEquipHandEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, DidUnequipHandEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, BuckledEvent>(OnBuckleChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, UnbuckledEvent>(OnBuckleChanged);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, StunnedEvent>(OnStunned);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, KnockedDownEvent>(OnKnockedDown);
        SubscribeLocalEvent<Wh40kClassGrantedActionComponent, ActionAttemptEvent>(OnClassActionAttempt);
        SubscribeLocalEvent<ItemDurabilityComponent, DurabilityChangedEvent>(OnItemDurabilityChanged);

        _classProgress.ProgressChanged += OnClassProgressChanged;
        _accountProgress.ProgressChanged += OnAccountProgressChanged;
    }

    public override void Shutdown()
    {
        _classProgress.ProgressChanged -= OnClassProgressChanged;
        _accountProgress.ProgressChanged -= OnAccountProgressChanged;
        _pendingEquipmentReconcile.Clear();
        _loadGenerations.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var uid in _pendingEquipmentReconcile.ToArray())
        {
            _pendingEquipmentReconcile.Remove(uid);
            if (TryComp<Wh40kClassRuntimeProfileComponent>(uid, out var profile))
                ReconcileProfile((uid, profile));
        }

        var query = EntityQueryEnumerator<Wh40kClassRuntimeProfileComponent>();
        while (query.MoveNext(out var uid, out var profile))
        {
            if (!profile.TimedModifierLayers.Values.Any(layer => layer.ExpiresAt <= _timing.CurTime))
                continue;

            foreach (var key in profile.TimedModifierLayers
                         .Where(pair => pair.Value.ExpiresAt <= _timing.CurTime)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                profile.TimedModifierLayers.Remove(key);
            }

            RecalculateModifierLayers((uid, profile), true);
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        BeginLoad(args.Player.UserId, args.Mob);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        BeginLoad(args.Player.UserId, args.Entity);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        _loadGenerations[args.Player.UserId] = _loadGenerations.GetValueOrDefault(args.Player.UserId) + 1;
        _pendingEquipmentReconcile.Remove(args.Entity);
        RemComp<Wh40kClassRuntimeProfileComponent>(args.Entity);
        _classProgress.Remove(args.Player.UserId);
    }

    private async void BeginLoad(NetUserId userId, EntityUid body)
    {
        if (!Exists(body) || !HasComp<MobStateComponent>(body))
            return;

        var generation = _loadGenerations.GetValueOrDefault(userId) + 1;
        _loadGenerations[userId] = generation;
        var result = await _classProgress.GetSnapshotAsync(userId);
        if (_loadGenerations.GetValueOrDefault(userId) != generation ||
            result.Account == null ||
            result.ClassProgress == null ||
            !_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity != body ||
            !Exists(body))
        {
            return;
        }

        ApplyAccountProfile(body, result.Account, result.ClassProgress);
    }

    private void OnClassProgressChanged(
        NetUserId userId,
        Wh40kAccountRpgRecord account,
        Wh40kAccountClassProgressRecord progress)
    {
        if (_players.TryGetSessionById(userId, out var session) &&
            session.AttachedEntity is { Valid: true } body)
        {
            ApplyAccountProfile(body, account, progress);
        }
    }

    private void OnAccountProgressChanged(NetUserId userId, Wh40kAccountRpgRecord account)
    {
        if (!_classProgress.TryGetProgress(userId, out var progress) ||
            !_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { Valid: true } body)
        {
            return;
        }

        ApplyAccountProfile(body, account, progress);
    }

    public void ApplyAccountProfile(
        EntityUid body,
        Wh40kAccountRpgRecord account,
        Wh40kAccountClassProgressRecord progress)
    {
        if (!Exists(body) || !HasComp<MobStateComponent>(body))
            return;

        var profile = EnsureComp<Wh40kClassRuntimeProfileComponent>(body);
        profile.UserId = account.Foundation.UserId;
        profile.Account = account;
        profile.Progress = progress;
        ReconcileProfile((body, profile));
    }

    private void ReconcileProfile(Entity<Wh40kClassRuntimeProfileComponent> ent)
    {
        if (ent.Comp.Account == null || ent.Comp.Progress == null || !IsLiving(ent.Owner))
        {
            ApplyDesiredEffects(ent, Array.Empty<Wh40kResolvedClassEffect>(), true);
            return;
        }

        var equipment = BuildEquipmentSnapshot(ent.Owner);
        var rarityByEffect = _prototypes.EnumeratePrototypes<Wh40kClassRarityModifierPrototype>()
            .GroupBy(modifier => modifier.Effect.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Wh40kClassRarityModifierPrototype>) group.ToArray(), StringComparer.Ordinal);
        var allowedSpecializations = _prototypes.Index<Content.Shared._WH40K.CharacterCreation.Wh40kCharacterClassPrototype>(
                ent.Comp.Account.Foundation.ClassId)
            .Specializations
            .Select(id => id.Id)
            .ToHashSet(StringComparer.Ordinal);
        var desired = new List<Wh40kResolvedClassEffect>();

        if (ent.Comp.Progress.TreeVersion == Wh40kClassProgressionConstants.TreeVersion)
        {
            foreach (var skillId in ent.Comp.Progress.PurchasedSkillIds.Order(StringComparer.Ordinal))
            {
                if (!_prototypes.TryIndex<Wh40kClassSkillPrototype>(skillId, out var skill) ||
                    skill.Availability != Wh40kClassContentAvailability.Enabled ||
                    !allowedSpecializations.Contains(skill.Specialization.Id) ||
                    !Wh40kClassRuntimePolicy.TryGetSupportingItems(
                        equipment,
                        skill.RequiredEquipmentTags,
                        out var supportingItems))
                {
                    continue;
                }

                foreach (var effectId in skill.Effects)
                {
                    if (!_prototypes.TryIndex(effectId, out Wh40kClassSkillEffectPrototype? effect) ||
                        effect.Availability != Wh40kClassContentAvailability.Enabled)
                    {
                        continue;
                    }

                    desired.Add(Wh40kClassRuntimePolicy.ResolveEffect(
                        effect,
                        supportingItems,
                        rarityByEffect.GetValueOrDefault(effect.ID) ?? Array.Empty<Wh40kClassRarityModifierPrototype>(),
                        skill.RequiredEquipmentTags.Count == 0
                            ? Wh40kClassRuntimeModifierLayer.Talents
                            : Wh40kClassRuntimeModifierLayer.Equipment,
                        skill.RequiredEquipmentTags.Count > 0));
                }
            }
        }

        ApplyDesiredEffects(ent, desired, true);
    }

    internal void ApplyDesiredEffectsForTest(EntityUid body, IReadOnlyList<Wh40kResolvedClassEffect> desired)
    {
        var profile = EnsureComp<Wh40kClassRuntimeProfileComponent>(body);
        ApplyDesiredEffects((body, profile), desired, false);
    }

    private void ApplyDesiredEffects(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        IReadOnlyList<Wh40kResolvedClassEffect> desired,
        bool applyStats)
    {
        var desiredById = desired.ToDictionary(effect => effect.EffectId, StringComparer.Ordinal);
        ent.Comp.ActiveEffects.Clear();
        foreach (var (effectId, effect) in desiredById)
            ent.Comp.ActiveEffects.Add(effectId, effect);

        ReconcileEquipmentRelays(ent, desired);

        var grantedActions = Wh40kClassRuntimePolicy.SelectGrantedActionEffects(desired)
            .SelectMany(EnumerateGrantedActions)
            .ToDictionary(entry => entry.Key, StringComparer.Ordinal);

        foreach (var effectId in ent.Comp.GrantedActions.Keys
                     .Where(effectId => !grantedActions.TryGetValue(effectId, out var entry) ||
                                        ent.Comp.GrantedActionPrototypes.GetValueOrDefault(effectId) != entry.Action ||
                                        !Exists(ent.Comp.GrantedActions[effectId]))
                     .ToArray())
        {
            RemoveGrantedAction(ent, effectId);
        }

        foreach (var entry in grantedActions.Values)
        {
            if (ent.Comp.GrantedActions.ContainsKey(entry.Key))
                continue;

            EntityUid? action = null;
            if (!_actions.AddAction(ent.Owner, ref action, entry.Action.Id) || action is not { } actionUid)
                continue;

            var marker = EnsureComp<Wh40kClassGrantedActionComponent>(actionUid);
            marker.Body = ent.Owner;
            marker.EffectId = entry.Effect.EffectId;
            marker.Safety = entry.Effect.Safety;
            marker.IsSecondary = entry.IsSecondary;
            ent.Comp.GrantedActions.Add(entry.Key, actionUid);
            ent.Comp.GrantedActionPrototypes.Add(entry.Key, entry.Action);
        }

        ent.Comp.ProfileModifierLayers.Clear();
        foreach (var effect in desired)
        {
            if (effect.Kind != Wh40kClassSkillEffectKind.StatModifier ||
                effect.Characteristic is not { } characteristic)
            {
                continue;
            }

            var key = new Wh40kClassRuntimeModifierKey(
                effect.EffectId,
                characteristic,
                effect.ModifierCategory);
            ent.Comp.ProfileModifierLayers[key] = new Wh40kClassRuntimeModifier(
                key,
                effect.ModifierLayer,
                effect.Magnitude);
        }

        foreach (var key in ent.Comp.TimedModifierLayers.Keys
                     .Where(key => !desiredById.ContainsKey(key.SourceEffectId))
                     .ToArray())
        {
            ent.Comp.TimedModifierLayers.Remove(key);
        }

        RecalculateModifierLayers(ent, applyStats);
        ApplyBodyModifiers(ent, desired);
        RaiseLocalEvent(ent.Owner, new Wh40kClassProfileReconciledEvent());
    }

    private static IEnumerable<GrantedActionEntry> EnumerateGrantedActions(Wh40kResolvedClassEffect effect)
    {
        if (effect.Action is { } primary)
            yield return new GrantedActionEntry(effect.EffectId, effect, primary, false);
        if (effect.SecondaryAction is { } secondary)
            yield return new GrantedActionEntry($"{effect.EffectId}#secondary", effect, secondary, true);
    }

    private readonly record struct GrantedActionEntry(
        string Key,
        Wh40kResolvedClassEffect Effect,
        EntProtoId Action,
        bool IsSecondary);

    private void ApplyBodyModifiers(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        IReadOnlyList<Wh40kResolvedClassEffect> desired)
    {
        RestoreBodyModifiers(ent);

        var criticalBonus = desired
            .Where(effect => effect.Mechanic == Wh40kClassRuntimeMechanic.CriticalThresholdModifier)
            .Sum(effect => effect.Magnitude);
        if (criticalBonus != 0 &&
            TryComp<MobThresholdsComponent>(ent, out var thresholds) &&
            _mobThresholds.TryGetThresholdForState(ent, MobState.Critical, out var criticalThreshold, thresholds) &&
            criticalThreshold is { } baseline)
        {
            ent.Comp.CriticalThresholdBaseline = baseline;
            _mobThresholds.SetMobStateThreshold(ent, baseline + FixedPoint2.New(criticalBonus), MobState.Critical, thresholds);
        }

        if (!TryComp<StaminaComponent>(ent, out var stamina))
            return;

        var staminaCriticalBonus = desired
            .Where(effect => effect.Mechanic == Wh40kClassRuntimeMechanic.StaminaCriticalThresholdModifier)
            .Sum(effect => effect.Magnitude);
        var staminaDecayBonus = desired
            .Where(effect => effect.Mechanic == Wh40kClassRuntimeMechanic.StaminaDecayModifier)
            .Sum(effect => effect.Magnitude);
        var changed = false;
        if (staminaCriticalBonus != 0)
        {
            ent.Comp.StaminaCriticalThresholdBaseline = stamina.CritThreshold;
            stamina.CritThreshold = Math.Max(1f, stamina.CritThreshold + staminaCriticalBonus);
            changed = true;
        }
        if (staminaDecayBonus != 0)
        {
            ent.Comp.StaminaDecayBaseline = stamina.Decay;
            stamina.Decay = Math.Max(0f, stamina.Decay + staminaDecayBonus);
            changed = true;
        }
        if (changed)
            Dirty(ent, stamina);
    }

    private void RestoreBodyModifiers(Entity<Wh40kClassRuntimeProfileComponent> ent)
    {
        if (ent.Comp.CriticalThresholdBaseline is { } criticalBaseline &&
            TryComp<MobThresholdsComponent>(ent, out var thresholds))
        {
            _mobThresholds.SetMobStateThreshold(ent, criticalBaseline, MobState.Critical, thresholds);
        }
        ent.Comp.CriticalThresholdBaseline = null;

        if (!TryComp<StaminaComponent>(ent, out var stamina))
        {
            ent.Comp.StaminaCriticalThresholdBaseline = null;
            ent.Comp.StaminaDecayBaseline = null;
            return;
        }

        var changed = false;
        if (ent.Comp.StaminaCriticalThresholdBaseline is { } staminaCriticalBaseline)
        {
            stamina.CritThreshold = staminaCriticalBaseline;
            ent.Comp.StaminaCriticalThresholdBaseline = null;
            changed = true;
        }
        if (ent.Comp.StaminaDecayBaseline is { } staminaDecayBaseline)
        {
            stamina.Decay = staminaDecayBaseline;
            ent.Comp.StaminaDecayBaseline = null;
            changed = true;
        }
        if (changed)
            Dirty(ent, stamina);
    }

    public bool TryApplyTemporaryModifier(
        EntityUid body,
        string sourceEffectId,
        Wh40kCharacteristic characteristic,
        Wh40kClassModifierCategory category,
        int magnitude,
        int maximumMagnitude,
        TimeSpan duration)
    {
        if (!TryComp<Wh40kClassRuntimeProfileComponent>(body, out var profile) ||
            !profile.ActiveEffects.ContainsKey(sourceEffectId) ||
            !IsLiving(body) ||
            duration <= TimeSpan.Zero ||
            duration > MaximumTemporaryEffectDuration ||
            maximumMagnitude <= 0)
        {
            return false;
        }

        var key = new Wh40kClassRuntimeModifierKey(sourceEffectId, characteristic, category);
        profile.TimedModifierLayers[key] = new Wh40kClassRuntimeModifier(
            key,
            Wh40kClassRuntimeModifierLayer.TemporaryEffects,
            Math.Clamp(magnitude, -maximumMagnitude, maximumMagnitude),
            _timing.CurTime + duration);
        RecalculateModifierLayers((body, profile), true);
        return true;
    }

    private void RecalculateModifierLayers(Entity<Wh40kClassRuntimeProfileComponent> ent, bool applyStats)
    {
        var selected = Wh40kClassRuntimePolicy.SelectStrongestModifiers(
            ent.Comp.ProfileModifierLayers.Values.Concat(ent.Comp.TimedModifierLayers.Values),
            _timing.CurTime);
        ent.Comp.TalentModifiers = Wh40kClassRuntimePolicy.SumLayer(
            selected,
            Wh40kClassRuntimeModifierLayer.Talents);
        ent.Comp.EquipmentModifiers = Wh40kClassRuntimePolicy.SumLayer(
            selected,
            Wh40kClassRuntimeModifierLayer.Equipment);
        ent.Comp.TemporaryModifiers = Wh40kClassRuntimePolicy.SumLayer(
            selected,
            Wh40kClassRuntimeModifierLayer.TemporaryEffects);

        if (applyStats && ent.Comp.Account != null && Exists(ent.Owner))
            _stats.ApplyAccountStats(ent.Owner, ent.Comp.Account);
    }

    internal IReadOnlyList<Wh40kClassEquipmentSnapshot> BuildEquipmentSnapshot(EntityUid body)
    {
        var items = new HashSet<EntityUid>(_hands.EnumerateHeld(body));
        var slots = _inventory.GetSlotEnumerator(body);
        while (slots.NextItem(out var item))
            items.Add(item);

        if (TryComp<BuckleComponent>(body, out var buckle) &&
            buckle.BuckledTo is { Valid: true } mountedGun &&
            HasComp<BuckleMountedGunComponent>(mountedGun))
        {
            items.Add(mountedGun);
        }

        var relevantTags = _prototypes.EnumeratePrototypes<Wh40kClassSkillPrototype>()
            .SelectMany(skill => skill.RequiredEquipmentTags)
            .Concat(_prototypes.EnumeratePrototypes<Wh40kClassRarityModifierPrototype>()
                .Select(modifier => modifier.EquipmentTag))
            .ToHashSet();
        var result = new List<Wh40kClassEquipmentSnapshot>(items.Count);
        foreach (var item in items)
        {
            if (TryComp<ItemDurabilityComponent>(item, out var durability) &&
                (durability.Broken || durability.CurrentDurability <= 0f))
            {
                continue;
            }

            if (!_rarity.TryGetRarity(item, out var rarityId) ||
                !_prototypes.TryIndex(rarityId, out ItemRarityPrototype? rarityPrototype))
            {
                continue;
            }

            var tags = relevantTags.Where(tag => _tags.HasTag(item, tag)).ToHashSet();
            var bonus = TryComp<ItemRarityComponent>(item, out var rarity) && rarity.IsRolled
                ? rarity.BonusPercent
                : 0f;
            result.Add(new Wh40kClassEquipmentSnapshot(
                item,
                tags,
                rarityId,
                rarityPrototype.Tier,
                bonus));
        }

        return result;
    }

    private void OnEquipmentChanged(EntityUid uid, Wh40kClassRuntimeProfileComponent component, DidEquipEvent args)
    {
        _pendingEquipmentReconcile.Add(uid);
    }

    private void OnEquipmentChanged(EntityUid uid, Wh40kClassRuntimeProfileComponent component, DidUnequipEvent args)
    {
        _pendingEquipmentReconcile.Add(uid);
    }

    private void OnEquipmentChanged(EntityUid uid, Wh40kClassRuntimeProfileComponent component, DidEquipHandEvent args)
    {
        _pendingEquipmentReconcile.Add(uid);
    }

    private void OnEquipmentChanged(EntityUid uid, Wh40kClassRuntimeProfileComponent component, DidUnequipHandEvent args)
    {
        _pendingEquipmentReconcile.Add(uid);
    }

    private void OnBuckleChanged(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent component,
        ref BuckledEvent args)
    {
        _pendingEquipmentReconcile.Add(uid);
    }

    private void OnBuckleChanged(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent component,
        ref UnbuckledEvent args)
    {
        _pendingEquipmentReconcile.Add(uid);
    }

    private void OnItemDurabilityChanged(
        EntityUid uid,
        ItemDurabilityComponent component,
        DurabilityChangedEvent args)
    {
        if (args.User is { Valid: true } user && HasComp<Wh40kClassRuntimeProfileComponent>(user))
        {
            _pendingEquipmentReconcile.Add(user);
            return;
        }

        var current = uid;
        for (var depth = 0; depth < 4 && Exists(current); depth++)
        {
            var parent = Transform(current).ParentUid;
            if (!parent.IsValid() || parent == current)
                break;
            if (HasComp<Wh40kClassRuntimeProfileComponent>(parent))
            {
                _pendingEquipmentReconcile.Add(parent);
                break;
            }

            current = parent;
        }
    }

    private void OnMobStateChanged(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent component,
        MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            ReconcileProfile((uid, component));
        else if (args.NewMobState == MobState.Dead)
            ApplyDesiredEffects((uid, component), Array.Empty<Wh40kResolvedClassEffect>(), true);
    }

    private void OnStunned(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent component,
        ref StunnedEvent args)
    {
        ReduceFreshStatusDuration(uid, component, "Stun", Wh40kClassRuntimeMechanic.StunDurationReduction);
    }

    private void OnKnockedDown(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent component,
        ref KnockedDownEvent args)
    {
        ReduceFreshStatusDuration(uid, component, "KnockedDown", Wh40kClassRuntimeMechanic.KnockedDownDurationReduction);
    }

    private void ReduceFreshStatusDuration(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent profile,
        string statusKey,
        Wh40kClassRuntimeMechanic mechanic)
    {
        var reduction = profile.ActiveEffects.Values
            .Where(effect => effect.Mechanic == mechanic)
            .Sum(effect => Math.Max(0, effect.Magnitude));
        if (reduction <= 0 || !TryComp<StatusEffectsComponent>(uid, out var statuses) ||
            !_statusEffects.TryGetTime(uid, statusKey, out var cooldown, statuses) || cooldown is not { } state)
        {
            return;
        }

        var remaining = state.Item2 - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
            return;

        _statusEffects.TryRemoveTime(
            uid,
            statusKey,
            remaining * Math.Clamp(reduction / 100d, 0d, 0.9d),
            statuses);
    }

    private void OnClassActionAttempt(
        EntityUid uid,
        Wh40kClassGrantedActionComponent component,
        ref ActionAttemptEvent args)
    {
        if (args.Cancelled ||
            args.User != component.Body ||
            !TryComp<Wh40kClassRuntimeProfileComponent>(component.Body, out var profile) ||
            !profile.ActiveEffects.TryGetValue(component.EffectId, out var effect) ||
            effect.Action == null ||
            !IsLiving(component.Body) ||
            !_safety.IsClassActionAllowed(component.Body, null, component.Safety))
        {
            args.Cancelled = true;
        }
    }

    private void OnProfileShutdown(
        EntityUid uid,
        Wh40kClassRuntimeProfileComponent component,
        ComponentShutdown args)
    {
        _pendingEquipmentReconcile.Remove(uid);
        RestoreBodyModifiers((uid, component));
        foreach (var effectId in component.GrantedActions.Keys.ToArray())
            RemoveGrantedAction((uid, component), effectId);
        component.ActiveEffects.Clear();
        RemoveEquipmentRelays((uid, component));
        component.CooldownEnds.Clear();
        component.RuntimeStates.Clear();
        component.ProfileModifierLayers.Clear();
        component.TimedModifierLayers.Clear();
        component.TalentModifiers = new Dictionary<Wh40kCharacteristic, int>();
        component.EquipmentModifiers = new Dictionary<Wh40kCharacteristic, int>();
        component.TemporaryModifiers = new Dictionary<Wh40kCharacteristic, int>();

        if (component.Account != null &&
            Exists(uid) &&
            MetaData(uid).EntityLifeStage < EntityLifeStage.Terminating)
        {
            _stats.ApplyBaseAccountStats(uid, component.Account);
        }
    }

    private void RemoveGrantedAction(Entity<Wh40kClassRuntimeProfileComponent> ent, string effectId)
    {
        if (!ent.Comp.GrantedActions.Remove(effectId, out var action))
            return;

        ent.Comp.GrantedActionPrototypes.Remove(effectId);
        if (!Exists(action))
            return;

        _actions.RemoveAction(ent.Owner, action);
        QueueDel(action);
    }

    private void ReconcileEquipmentRelays(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        IReadOnlyList<Wh40kResolvedClassEffect> desired)
    {
        RemoveEquipmentRelays(ent);

        foreach (var group in desired
                     .Where(effect => effect.RequiresEquipment && effect.SupportingItem is { Valid: true })
                     .GroupBy(effect => effect.SupportingItem!.Value))
        {
            if (!Exists(group.Key))
                continue;

            var relay = EnsureComp<Wh40kClassEquipmentRelayComponent>(group.Key);
            relay.Body = ent.Owner;
            relay.EffectIds.Clear();
            foreach (var effect in group)
                relay.EffectIds.Add(effect.EffectId);
            ent.Comp.RelayedEquipment.Add(group.Key);
        }
    }

    private void RemoveEquipmentRelays(Entity<Wh40kClassRuntimeProfileComponent> ent)
    {
        foreach (var item in ent.Comp.RelayedEquipment)
        {
            if (TryComp<Wh40kClassEquipmentRelayComponent>(item, out var relay) && relay.Body == ent.Owner)
                RemComp<Wh40kClassEquipmentRelayComponent>(item);
        }

        ent.Comp.RelayedEquipment.Clear();
    }

    private bool IsLiving(EntityUid uid)
    {
        return !TryComp<MobStateComponent>(uid, out var mobState) || mobState.CurrentState == MobState.Alive;
    }
}
