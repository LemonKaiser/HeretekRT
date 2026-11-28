using System.Linq;
using Content.Shared._WH40K.Biotype;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._WH40K.Biotype;

/// <summary>
/// Converts a spawned humanoid mob to its selected <see cref="Wh40kBiotype"/> (for example,
/// equipping the Mechanicus cybernetic body with cheap WH40K prosthetics while retaining an organic human brain).
/// </summary>
public sealed partial class Wh40kBiotypeSystem : SharedWh40kBiotypeSystem
{
    private static readonly ProtoId<AlertCategoryPrototype>[] OrganicAlertCategories =
    {
        "Hunger",
        "Thirst",
        "Breathing",
        "Pressure",
        "Toxins",
    };

    private static readonly ProtoId<AlertPrototype> BleedAlert = "Bleed";

    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private TagSystem _tags = default!;

    private readonly Dictionary<Wh40kBiotype, Wh40kBiotypePrototype> _biotypes = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        LoadPrototypes();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<Wh40kBiotypePrototype>())
            LoadPrototypes();
    }

    private void LoadPrototypes()
    {
        _biotypes.Clear();
        foreach (var proto in _proto.EnumeratePrototypes<Wh40kBiotypePrototype>())
        {
            if (!_biotypes.TryAdd(proto.Biotype, proto))
                Log.Error($"Duplicate wh40kBiotype prototype for {proto.Biotype}: {proto.ID}");
        }
    }

    public void Apply(EntityUid mob, HumanoidCharacterProfile profile)
    {
        if (TerminatingOrDeleted(mob))
            return;

        var biotype = Wh40kBiotypeGates.Effective(profile, _proto);
        Apply(mob, biotype);
    }

    public void Apply(EntityUid mob, Wh40kBiotype biotype)
    {
        if (TerminatingOrDeleted(mob))
            return;

        var existing = CompOrNull<Wh40kBiotypeComponent>(mob);
        var current = GetBiotype(mob);
        if (existing != null && existing.Biotype == biotype)
        {
            _body.RefreshBaseLayersFromParts(mob);
            return;
        }

        if (current != biotype && _biotypes.TryGetValue(biotype, out var proto))
            Convert(mob, proto);

        var marker = EnsureComp<Wh40kBiotypeComponent>(mob);
        if (marker.Biotype != biotype)
        {
            marker.Biotype = biotype;
            Dirty(mob, marker);
        }

        var ev = new Wh40kBiotypeAppliedEvent(biotype);
        RaiseLocalEvent(mob, ref ev);
    }

    private void Convert(EntityUid mob, Wh40kBiotypePrototype proto)
    {
        foreach (var name in proto.RemoveComponents)
        {
            if (_factory.TryGetRegistration(name, out var reg) && HasComp(mob, reg.Type))
                RemComp(mob, reg.Type);
        }

        foreach (var category in OrganicAlertCategories)
        {
            if (_proto.HasIndex(category))
                _alerts.ClearAlertCategory(mob, category);
        }

        if (_proto.HasIndex(BleedAlert))
            _alerts.ClearAlert(mob, BleedAlert);

        foreach (var (name, entry) in proto.EnsureComponents)
        {
            if (entry.Component is ItemSlotsComponent slots && TryComp(mob, out ItemSlotsComponent? own))
                MergeItemSlots(mob, own, slots);
            else if (!HasComp(mob, _factory.GetRegistration(name).Type))
                EntityManager.AddComponents(mob, new ComponentRegistry { [name] = entry }, removeExisting: false);
        }

        if (proto.AddComponents.Count > 0)
            EntityManager.AddComponents(mob, proto.AddComponents, removeExisting: true);

        if (proto.StartingPowerCell is { } startingCell &&
            _itemSlots.TryGetSlot(mob, "cell_slot", out var cellSlot) &&
            cellSlot.ContainerSlot is { } container &&
            !cellSlot.HasItem)
        {
            var cell = SpawnAttachedTo(startingCell, Transform(mob).Coordinates);
            if (!_containers.Insert(cell, container, force: true))
                Del(cell);
        }

        if (proto.DamageModifierSet is { } modifierId &&
            _proto.HasIndex(modifierId) &&
            TryComp<DamageableComponent>(mob, out var damageable))
        {
            _damageable.SetDamageModifierSetId(mob, modifierId.Id, damageable);
        }

        if (proto.RemoveTags.Count > 0)
            _tags.RemoveTags(mob, proto.RemoveTags);

        if (proto.AddTags.Count > 0)
            _tags.AddTags(mob, proto.AddTags);

        if (proto.BodyPrototype is { } bodyProto)
        {
            _body.Wh40kRebuildBody(mob, bodyProto);
            DropStaleHands(mob);
        }
    }

    private void MergeItemSlots(EntityUid mob, ItemSlotsComponent own, ItemSlotsComponent slots)
    {
        foreach (var (id, template) in slots.Slots)
        {
            if (_itemSlots.TryGetSlot(mob, id, out _, own))
                continue;

            var slot = _serialization.CreateCopy(template, notNullableOverride: true);
            _itemSlots.AddItemSlot(mob, id, slot, own);

            if (LifeStage(mob) < EntityLifeStage.MapInitialized ||
                slot.StartingItem is not { } starting ||
                slot.ContainerSlot is not { } container)
            {
                continue;
            }

            var item = SpawnAttachedTo(starting, Transform(mob).Coordinates);
            if (!_containers.Insert(item, container, force: true))
                Del(item);
        }
    }

    private void DropStaleHands(EntityUid mob)
    {
        if (!TryComp(mob, out HandsComponent? hands))
            return;

        var kept = new HashSet<string>();
        foreach (var (part, _) in _body.GetBodyChildrenOfType(mob, BodyPartType.Hand))
        {
            if (_containers.TryGetContainingContainer((part, null, null), out var container))
                kept.Add(container.ID);
        }

        foreach (var name in hands.Hands.Keys.ToList())
        {
            if (name.StartsWith(SharedBodySystem.PartSlotContainerIdPrefix) && !kept.Contains(name))
                _hands.RemoveHand(mob, name, hands);
        }
    }
}
