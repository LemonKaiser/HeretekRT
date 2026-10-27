using Content.Shared._Mono.Company;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

/// <summary>
/// Applies account faction membership to spawned and reattached characters.
/// </summary>
public sealed partial class CompanySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private CompanyManager _manager = default!;
    [Dependency] private IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to player spawn event to add the company component
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        _manager.FactionChanged += OnFactionChanged;
    }

    public override void Shutdown()
    {
        _manager.FactionChanged -= OnFactionChanged;
        base.Shutdown();
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (_manager.Ready.IsCompletedSuccessfully)
            SynchronizeMembership(args.Player.UserId, args.Entity);
    }

    private void OnFactionChanged(ProtoId<CompanyPrototype> faction, NetUserId player)
    {
        if (_players.TryGetSessionById(player, out var session) && session.AttachedEntity is { } entity)
            SynchronizeMembership(player, entity);
    }

    private void SynchronizeMembership(NetUserId player, EntityUid entity)
    {
        if (!TryComp<CompanyComponent>(entity, out var component))
            return;
        var faction = _manager.GetCharacterFaction(player, component.CompanyName.Id);
        if (component.CompanyName != faction)
            SetFaction(entity, faction);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var companyComp = EnsureComp<CompanyComponent>(args.Mob);
        var membership = _manager.GetActiveFaction(args.Player.UserId, args.Profile.Company);
        var company = _manager.GetCharacterFaction(args.Player.UserId, args.Profile.Company);
        if (!_manager.IsAllowed(args.Player, company))
            company = "None";
        if (membership == null && args.JobId != null)
        {
            var job = _prototypeManager.Index<JobPrototype>(args.JobId);
            if (job.AssignedCompany != "None" && _manager.IsAllowed(args.Player, job.AssignedCompany))
                company = job.AssignedCompany.Id;
        }
        companyComp.CompanyName = company;

        // Ensure the component is networked to clients
        Dirty(args.Mob, companyComp);

        // Update the player's ID card with the company information
        if (args.LoadoutMode == PlayerSpawnLoadoutMode.Default)
            UpdateIdCardCompany(args.Mob, companyComp.CompanyName);
    }

    /// <summary>
    /// Updates the player's ID card with their company information
    /// </summary>
    private void UpdateIdCardCompany(EntityUid playerEntity, string companyName)
    {
        // Try to get the player's ID card
        if (!_inventorySystem.TryGetSlotEntity(playerEntity, "id", out var idUid))
            return;

        var cardId = idUid.Value;

        // Check if it's a PDA with an ID card inside
        if (TryComp<PdaComponent>(idUid, out var pdaComponent) && pdaComponent.ContainedId != null)
            cardId = pdaComponent.ContainedId.Value;

        // Update the ID card with company information
        if (TryComp<IdCardComponent>(cardId, out var idCard))
        {
            _idCardSystem.TryChangeCompanyName(cardId, companyName, idCard);
        }
    }

    public void SetFaction(EntityUid player, ProtoId<CompanyPrototype> faction)
    {
        var component = EnsureComp<CompanyComponent>(player);
        component.CompanyName = faction;
        Dirty(player, component);
        UpdateIdCardCompany(player, faction.Id);
    }
}
