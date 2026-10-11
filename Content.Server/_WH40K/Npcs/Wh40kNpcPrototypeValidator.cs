using System.Linq;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Dialogue;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Content.Shared.Dataset;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using static Content.Server._WH40K.Quests.Wh40kQuestPrototypeValidator;

namespace Content.Server._WH40K.Npcs;

public static class Wh40kNpcPrototypeValidator
{
    public static List<Wh40kContentDiagnostic> ValidateAnchor(Wh40kNpcAnchorPrototype anchor,
        IReadOnlyCollection<Wh40kNpcAnchorPrototype> anchors, IPrototypeManager prototypes)
    {
        var errors = new List<Wh40kContentDiagnostic>();
        Reference(anchor.Profile, "profile", prototypes, errors);
        if (prototypes.TryIndex(anchor.Profile, out var profile) && profile.RequiresUniqueAnchor &&
            anchors.Count(other => other.Profile == anchor.Profile) > 1)
            errors.Add(new("duplicate-unique-anchor", "profile", "A unique profile has more than one authored anchor."));
        return errors;
    }

    public static List<Wh40kContentDiagnostic> ValidateStructure(Wh40kNpcProfilePrototype profile)
    {
        var errors = new List<Wh40kContentDiagnostic>();
        if (string.IsNullOrWhiteSpace(profile.Role) || string.IsNullOrWhiteSpace(profile.Department))
            errors.Add(new("missing-npc-role", "role/department", "NPC role and department are required."));
        if (!Enum.IsDefined(profile.Kind) || !Enum.IsDefined(profile.Exclusivity) ||
            profile.Services.Any(service => !Enum.IsDefined(service)) ||
            profile.AllowedObjectives.Any(kind => !Enum.IsDefined(kind)))
            errors.Add(new("invalid-npc-kind", "kind/services", "Unknown NPC identity or service kind."));
        if (profile.Kind != Wh40kNpcKind.Special &&
            (profile.Exclusivity != Wh40kNpcExclusivity.NonExclusive ||
             profile.Services.Any(service => service != Wh40kNpcService.Social) || profile.AllowedObjectives.Count > 0))
            errors.Add(new("ambient-authority", "services", "Workers/residents cannot own quests, trading or unique authority."));
        var protection = profile.Protection;
        if (!protection.Godmode || !protection.PreventCarry || !protection.LockEquipment || !protection.ReturnAcrossMaps ||
            !float.IsFinite(protection.HomeRadius) || protection.HomeRadius <= 0 || protection.HomeRadius > 10)
            errors.Add(new("invalid-npc-protection", "protection", "Hub NPCs require permanent protection and a bounded valid home radius."));
        return errors;
    }

    public static List<Wh40kContentDiagnostic> Validate(Wh40kNpcProfilePrototype profile,
        IPrototypeManager prototypes, Func<string, bool> hasString)
    {
        var errors = ValidateStructure(profile);
        Localization(profile.Name, "name", hasString, errors);
        Localization(profile.Description, "description", hasString, errors);
        if (profile.Faction is {} faction) Reference(faction, "faction", prototypes, errors);
        if (profile.Dialogue is {} dialogue) Reference(dialogue, "dialogue", prototypes, errors);
        if (profile.Gear is {} gear)
        {
            Reference(gear, "gear", prototypes, errors);
            if (prototypes.TryIndex(gear, out var loadout))
            {
                foreach (var item in loadout.Equipment.Values.Concat(loadout.Inhand).Concat(loadout.Storage.Values.SelectMany(items => items)))
                    Entity(item, "gear.items", prototypes, errors);
            }
        }
        if (profile.Chatter is {} chatter)
        {
            Reference(chatter, "chatter", prototypes, errors);
            if (prototypes.TryIndex(chatter, out var dataset))
            {
                if (dataset.Values.Count is <= 0 or > 64)
                    errors.Add(new("invalid-chatter-count", "chatter", "Ambient phrase count must be between 1 and 64."));
                foreach (var line in dataset.Values) Localization(line, "chatter.values", hasString, errors);
            }
        }
        return errors;
    }

    public static List<Wh40kContentDiagnostic> ValidateMerchant(MerchantComponent merchant,
        IPrototypeManager prototypes, Func<string, bool> hasString)
    {
        var errors = new List<Wh40kContentDiagnostic>();
        Localization(merchant.Title, "title", hasString, errors);
        Localization(merchant.Greeting, "greeting", hasString, errors);
        if (merchant.RefreshInterval <= TimeSpan.Zero || merchant.DisplayCount is <= 0 or > 64 ||
            !float.IsFinite(merchant.ReturnDistance) || merchant.ReturnDistance <= 0 || merchant.ReturnDistance > 10 ||
            !float.IsFinite(merchant.InteractionRange) || merchant.InteractionRange <= 0 || merchant.InteractionRange > 10 ||
            !float.IsFinite(merchant.LookAtRange) || merchant.LookAtRange < 0 || merchant.LookAtRange > 10)
            errors.Add(new("invalid-merchant-limits", "limits", "Merchant stock, interaction and return limits are invalid."));
        if (merchant.Offers.Count is <= 0 or > 64)
            errors.Add(new("invalid-stock-count", "offers", "Merchant requires 1–64 offers."));
        var products = new HashSet<EntProtoId>();
        foreach (var offer in merchant.Offers)
        {
            Entity(offer.Product, "offers.product", prototypes, errors);
            if (prototypes.TryIndex(offer.Product, out var product) && (!product.Components.ContainsKey("Item") ||
                product.Components.ContainsKey("Wh40kNpcProtection") || product.Components.ContainsKey("Wh40kQuestItem")))
                errors.Add(new("invalid-stock-product", "offers.product", "Only ordinary customer items may be sold."));
            if (!products.Add(offer.Product) || offer.Price is <= 0 or > 1000000 || offer.Weight is <= 0 or > 10000 ||
                offer.MinStock <= 0 || offer.MaxStock < offer.MinStock || offer.MaxStock > 100)
                errors.Add(new("invalid-stock", "offers", "Duplicate product or invalid price/weight/stock range."));
            if (offer.DisplayName is {} name) Localization(name, "offers.displayName", hasString, errors);
            if (offer.Description is {} description) Localization(description, "offers.description", hasString, errors);
            if (offer.RequiredAccess is {} access) Reference(access, "offers.requiredAccess", prototypes, errors);
        }
        if (merchant.Buyback is {} buyback)
        {
            Reference(buyback, "buyback", prototypes, errors);
            if (prototypes.TryIndex(buyback, out var valuation))
            {
                if (valuation.Prices.Count is <= 0 or > 32) errors.Add(new("invalid-valuation-count", "buyback", "Valuation requires 1–32 approved products."));
                foreach (var (product, price) in valuation.Prices)
                {
                    Entity(product, "buyback.product", prototypes, errors);
                    if (price is <= 0 or > 1000000) errors.Add(new("invalid-valuation", "buyback.price", "Invalid approved appraisal price."));
                }
            }
        }
        return errors;
    }
}
