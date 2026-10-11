using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Shared._WH40K.Merchant;

namespace Content.Server._WH40K.Merchant;

public sealed class MerchantItemStateAdapter(IEntityManager entities) : IItemStateAdapter
{
    public string ComponentId => "MerchantItemToken";
    public bool Handles(IComponent component) => component is MerchantItemTokenComponent;
    public bool TryCapture(EntityUid uid, IComponent component, out PersistentInventoryComponentState state, out string? error)
    {
        var token = (MerchantItemTokenComponent) component;
        state = new(ComponentId, new Dictionary<string, string> { ["token"] = token.Token.ToString("D") });
        error = token.Token == Guid.Empty ? "Missing merchant item title." : null;
        return error == null;
    }
    public bool TryRestore(EntityUid uid, PersistentInventoryComponentState state, out string? error)
    {
        error = "Invalid merchant item title.";
        if (state.Fields.Count != 1 || !state.Fields.TryGetValue("token", out var text) ||
            !Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty) return false;
        var token = entities.EnsureComponent<MerchantItemTokenComponent>(uid);
        token.Token = id; token.Verified = false; error = null; return true;
    }
}
