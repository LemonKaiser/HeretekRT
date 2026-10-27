using System.Linq;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Progression;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Factions;

/// <summary>
/// Binds account membership and applications to a catalogue. Each surface supplies its own views.
/// </summary>
[Virtual]
public class FactionCatalog : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    private Wh40kFactionUiSnapshot? _snapshot;
    private bool _pending;
    private IFactionProfileWindow? _details;
    private string? _detailsId;

    public event Action<Wh40kFactionUiAction, string>? Request;

    public FactionCatalog()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public void Update(Wh40kFactionUiSnapshot? snapshot, bool pending)
    {
        _snapshot = snapshot;
        _pending = pending;
        RemoveAllChildren();
        foreach (var faction in _prototypes.EnumeratePrototypes<CompanyPrototype>()
                     .Where(f => !f.Disabled && f.ID != "None").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var state = GetCardState(faction.ID);
            var card = CreateCard();
            card.SetFaction(faction, state.Membership, state.Action, state.Disabled, faction.ID == snapshot?.FactionId);
            card.UpdateOverview(FactionUiPresentation.Overview(GetOption(faction.ID)));
            card.DetailsPressed += () => OpenDetails(faction);
            card.ActionPressed += () => Act(faction.ID);
            AddChild(card.Root);
        }
        if (_details != null && _detailsId != null)
            UpdateDetails();
    }

    private (string Membership, string Action, string Hint, bool Disabled) GetCardState(string id)
    {
        var option = GetOption(id);
        if (_snapshot == null)
            return State("loading", "join", "loading", true);
        if (option?.Role is { } role)
        {
            var membership = Loc.GetString("wh40k-faction-membership-role", ("role", FactionUiPresentation.Role(role, option.Profile)));
            return (membership, Loc.GetString("wh40k-faction-member-label"),
                Loc.GetString("wh40k-faction-approved-hint"), true);
        }
        if (option?.HasRequest == true)
            return State("awaiting", "withdraw", "awaiting-hint", _pending);
        if (!_snapshot.AccountAvailable)
            return State("guest", "join", "status-account-unavailable", true);
        if (_snapshot.FactionId != null)
            return State("other-membership", "join", "other-membership", true);
        return State("application-required", "join", "apply-hint", _pending);
    }

    private static (string Membership, string Action, string Hint, bool Disabled) State(string membership, string action, string hint, bool disabled) =>
        (Loc.GetString($"wh40k-faction-{membership}"), Loc.GetString($"wh40k-faction-{action}"),
            Loc.GetString($"wh40k-faction-{hint}"), disabled);

    private Wh40kFactionOptionUiSnapshot? GetOption(string id) => _snapshot?.AvailableFactions.FirstOrDefault(f => f.Id == id);

    private void Act(string id)
    {
        if (GetCardState(id).Disabled)
            return;
        var option = GetOption(id);
        Request?.Invoke(option?.HasRequest == true ? Wh40kFactionUiAction.CancelRequest : Wh40kFactionUiAction.RequestJoin, id);
    }

    private void OpenDetails(CompanyPrototype faction)
    {
        _details?.Dispose();
        _detailsId = faction.ID;
        _details = CreateProfile(faction);
        _details.ActionPressed += () => Act(faction.ID);
        UpdateDetails();
        _details.Show();
    }

    protected virtual IFactionCatalogCard CreateCard() => new FactionCard();

    protected virtual IFactionProfileWindow CreateProfile(CompanyPrototype faction) => new FactionDetailsWindow(faction);

    public void ShowDetails(string id)
    {
        if (_prototypes.TryIndex<CompanyPrototype>(id, out var faction))
            OpenDetails(faction);
    }

    private void UpdateDetails()
    {
        var state = GetCardState(_detailsId!);
        _details!.UpdateAction(state.Membership, state.Action, state.Hint, state.Disabled);
        var option = GetOption(_detailsId!);
        _details.UpdateOverview(FactionUiPresentation.Overview(option, detailed: true));
        _details.UpdateProfile(option?.Profile ?? new Content.Shared._WH40K.Factions.FactionProfile());
        _details.UpdateRelations(_snapshot?.Relations ?? []);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _details?.Dispose();
        }
        base.Dispose(disposing);
    }
}
