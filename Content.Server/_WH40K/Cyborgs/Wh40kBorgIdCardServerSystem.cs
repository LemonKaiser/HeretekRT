using Content.Server._NF.SectorServices;
using Content.Server.Silicons.Borgs;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._NF.Interaction.Components;
using Content.Shared._NF.Interaction.Systems;
using Content.Shared._NF.Silicons.Borgs;
using Content.Shared._WH40K.Cyborgs;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.StationRecords;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Issues a cyborg its ID card, registers a sector station record for ship access/deeds,
/// and transfers the chassis-issued card into the tool module when installed.
/// </summary>
public sealed partial class Wh40kBorgIdCardServerSystem : EntitySystem
{
    private static readonly ProtoId<JobPrototype> RecordJob = "Borg";
    private static readonly EntProtoId IssuedCard = "Wh40kBorgIdCard";

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private HandPlaceholderSystem _placeholder = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private StationRecordKeyStorageSystem _keyStorage = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private Wh40kBorgIdCardSystem _cards = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<Wh40kBorgIdCardComponent, GotEquippedHandEvent>(OnCardEquipped);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<DroppableBorgModuleComponent, BorgModuleInstalledEvent>(OnModuleInstalled, before: new[] { typeof(BorgSystem) });
    }

    private void OnPlayerAttached(Entity<BorgChassisComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!_cards.TryGetCard(ent, out var card))
            return;

        EnsureRecord(ent, card);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!HasComp<BorgChassisComponent>(args.Mob) || _cards.TryGetCard(args.Mob, out _))
            return;

        _container.EnsureContainer<Container>(args.Mob, Wh40kBorgIdCardSystem.ContainerId);
        if (!TrySpawnInContainer(IssuedCard, args.Mob, Wh40kBorgIdCardSystem.ContainerId, out var card))
            return;

        if (args.JobId != null && _proto.TryIndex<JobPrototype>(args.JobId, out var job))
            _access.SetAccessToJob(card.Value, job, extended: false);

        EnsureRecord(args.Mob, card.Value);
    }

    private void OnModuleInstalled(Entity<DroppableBorgModuleComponent> ent, ref BorgModuleInstalledEvent args)
    {
        if (!_container.TryGetContainer(args.ChassisEnt, Wh40kBorgIdCardSystem.ContainerId, out var own)
            || own.ContainedEntities.Count == 0
            || !_container.TryGetContainer(ent, ent.Comp.ContainerId, out var stowed))
            return;

        var issued = own.ContainedEntities[0];
        foreach (var blank in stowed.ContainedEntities)
        {
            if (!HasComp<Wh40kBorgIdCardComponent>(blank) || !TryComp<HandPlaceholderRemoveableComponent>(blank, out var removeable))
                continue;

            _placeholder.SetPlaceholder(issued, removeable.Placeholder);
            _placeholder.SetEnabled(issued, false);
            _container.Insert(issued, stowed, force: true);
            Del(blank);
            return;
        }
    }

    private void OnCardEquipped(Entity<Wh40kBorgIdCardComponent> ent, ref GotEquippedHandEvent args)
    {
        if (!HasComp<BorgChassisComponent>(args.User))
            return;

        EnsureRecord(args.User, ent);
    }

    private void EnsureRecord(EntityUid borg, EntityUid card)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(card, out var storage) || storage.Key != null)
            return;

        var service = _sectorService.GetServiceEntity();
        if (!TryComp<StationRecordsComponent>(service, out var records) || !_proto.TryIndex(RecordJob, out var job))
            return;

        var name = Name(borg);
        var key = _records.AddRecordEntry(service, new GeneralStationRecord
        {
            Name = name,
            JobTitle = job.LocalizedName,
            JobIcon = job.Icon,
            JobPrototype = job.ID,
            DisplayPriority = job.RealDisplayWeight,
        }, records);

        if (!key.IsValid())
            return;

        _keyStorage.AssignKey(card, key, storage);
        _idCard.TryChangeFullName(card, name);
    }
}
