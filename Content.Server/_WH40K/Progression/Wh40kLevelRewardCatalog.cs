using System.Linq;
using System.Text.Json;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Converts data-driven level reward prototypes into deterministic database outbox entries.
/// </summary>
public sealed class Wh40kLevelRewardCatalog
{
    public const string CurrencyRewardType = "currency";
    public const string ItemRewardType = "item";
    public const int MaximumItemDeliveryCount = 100;
    public const long MaximumCurrencyDeliveryAmount = int.MaxValue;

    [Dependency] private IPrototypeManager _prototypes = default!;

    public IReadOnlyList<Wh40kLevelRewardDefinition> GetDefinitions()
    {
        return BuildDefinitions(_prototypes.EnumeratePrototypes<Wh40kLevelRewardPrototype>());
    }

    internal static IReadOnlyList<Wh40kLevelRewardDefinition> BuildDefinitions(
        IEnumerable<Wh40kLevelRewardPrototype> prototypes)
    {
        var definitions = new List<Wh40kLevelRewardDefinition>();
        var levels = new HashSet<int>();

        foreach (var prototype in prototypes.OrderBy(prototype => prototype.Level).ThenBy(prototype => prototype.ID))
        {
            if (prototype.Level <= Wh40kExperienceCurve.MinimumLevel ||
                prototype.Level > Wh40kExperienceCurve.MaximumLevel)
            {
                throw new InvalidOperationException(
                    $"WH40K level reward {prototype.ID} has invalid level {prototype.Level}.");
            }

            if (!levels.Add(prototype.Level))
                throw new InvalidOperationException($"WH40K level {prototype.Level} has more than one reward prototype.");

            if (prototype.Currency < 0 || prototype.Currency > MaximumCurrencyDeliveryAmount)
                throw new InvalidOperationException($"WH40K level reward {prototype.ID} has invalid currency.");

            var rewardId = $"level-reward:v{Wh40kExperienceCurve.BalanceVersion}:{prototype.Level}";
            var context = JsonSerializer.Serialize(new
            {
                source = "level",
                level = prototype.Level,
                prototype = prototype.ID,
            });
            var entries = new List<Wh40kRewardDeliveryDraft>();

            if (prototype.Currency > 0)
            {
                entries.Add(new Wh40kRewardDeliveryDraft(
                    rewardId,
                    "currency",
                    CurrencyRewardType,
                    null,
                    prototype.Currency,
                    context));
            }

            for (var index = 0; index < prototype.Items.Count; index++)
            {
                var item = prototype.Items[index];
                if (item.Count is <= 0 or > MaximumItemDeliveryCount)
                {
                    throw new InvalidOperationException(
                        $"WH40K level reward {prototype.ID} item {index} has invalid count {item.Count}.");
                }

                entries.Add(new Wh40kRewardDeliveryDraft(
                    rewardId,
                    $"item:{index}",
                    ItemRewardType,
                    item.Id.Id,
                    item.Count,
                    context));
            }

            if (entries.Count == 0)
                throw new InvalidOperationException($"WH40K level reward {prototype.ID} has no entries.");

            definitions.Add(new Wh40kLevelRewardDefinition(prototype.Level, rewardId, entries));
        }

        return definitions;
    }
}
