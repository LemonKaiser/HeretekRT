using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._WH40K.Progression;
using Content.Server.Database;
using Content.Shared._WH40K.Progression;
using Content.Shared._WH40K.Quests;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

public enum Wh40kQuestRewardPhase { IntentRead, ExperienceAwarded, OutboxQueued, MaterialDelivered, Acknowledged }

/// <summary>Bridges durable quest obligations into the existing XP ledger and material outbox.</summary>
public sealed partial class Wh40kQuestRewardSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private Wh40kExperienceService _experience = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private Task _dispatch = Task.CompletedTask;
    private float _timer;
    private int _offset;
    public event Action<Wh40kQuestRewardPhase>? PhaseChanged;

    public override void Update(float frameTime)
    {
        _timer += frameTime;
        if (_sessions.IsStopping || _timer < 5f || !_dispatch.IsCompleted) return;
        _timer = 0;
        _dispatch = _sessions.TrackWork(DispatchAsync());
    }

    public async Task DispatchAsync()
    {
        var batch = await _db.GetPendingWh40kQuestIntentsAsync(_offset);
        _offset = batch.Count == 64 && _offset <= int.MaxValue - 64 ? _offset + 64 : 0;
        foreach (var intent in batch)
        {
            try
            {
                PhaseChanged?.Invoke(Wh40kQuestRewardPhase.IntentRead);
                var reward = JsonSerializer.Deserialize<Wh40kQuestRewardSnapshot>(intent.PayloadJson);
                if (reward == null || reward.EntryId != intent.EntryId) continue;
                var user = new NetUserId(intent.Recipient);
                if (reward.Kind == Wh40kQuestRewardKind.Experience)
                {
                    Task? award = null;
                    await _sessions.OnSimulationThread(() =>
                    {
                        if (reward.ExperienceSource != null && _prototypes.TryIndex<Wh40kExperienceSourcePrototype>(reward.ExperienceSource, out var source) &&
                            source.SourceType == Wh40kExperienceSourceType.Mission)
                            award = _experience.AwardAsync(user, new(intent.RewardId, Wh40kExperienceSourceType.Mission, checked(reward.Amount * 10L),
                                ContextJson: intent.PayloadJson));
                    });
                    if (award == null) continue;
                    await award;
                    PhaseChanged?.Invoke(Wh40kQuestRewardPhase.ExperienceAwarded);
                    await _db.AcknowledgeWh40kQuestIntentAsync(intent.QuestId, intent.EntryId, intent.Recipient, experience: true);
                }
                else if (reward.Kind is Wh40kQuestRewardKind.Item or Wh40kQuestRewardKind.Currency)
                {
                    await _db.EnqueueWh40kRewardDeliveriesAsync(user,
                        [new(intent.RewardId, intent.EntryId,
                            reward.Kind == Wh40kQuestRewardKind.Item ? Wh40kLevelRewardCatalog.ItemRewardType : Wh40kLevelRewardCatalog.CurrencyRewardType,
                            reward.Item, reward.Amount, intent.PayloadJson)]);
                    PhaseChanged?.Invoke(Wh40kQuestRewardPhase.OutboxQueued);
                    Task? delivery = null;
                    await _sessions.OnSimulationThread(() => delivery = _sessions.TrackWork(EntityManager.System<Wh40kRewardDeliverySystem>().TryDeliverForUserAsync(user)));
                    await delivery!;
                    PhaseChanged?.Invoke(Wh40kQuestRewardPhase.MaterialDelivered);
                    await _db.AcknowledgeWh40kQuestIntentAsync(intent.QuestId, intent.EntryId, intent.Recipient, experience: false);
                }
                else if (reward.Kind is Wh40kQuestRewardKind.FactionReputation or Wh40kQuestRewardKind.Flag or Wh40kQuestRewardKind.Access)
                {
                    var valid = false;
                    await _sessions.OnSimulationThread(() => valid = reward.Kind switch
                    {
                        Wh40kQuestRewardKind.FactionReputation => reward.Faction != null && _prototypes.HasIndex<Content.Shared._WH40K.Npcs.Wh40kNpcFactionPrototype>(reward.Faction),
                        Wh40kQuestRewardKind.Access => reward.Access != null && _prototypes.HasIndex<Content.Shared.Access.AccessLevelPrototype>(reward.Access),
                        _ => !string.IsNullOrWhiteSpace(reward.Flag) && reward.Flag.Length <= 64,
                    });
                    if (!valid || !await _db.ApplyWh40kQuestStateRewardAsync(intent.QuestId, intent.EntryId, intent.Recipient)) continue;
                    await _sessions.OnSimulationThread(() => { if (_sessions.IsReady(user)) _sessions.TrackWork(_sessions.LoadAccountAsync(user)); });
                }
                PhaseChanged?.Invoke(Wh40kQuestRewardPhase.Acknowledged);
            }
            catch (Exception error) { Log.Error($"Quest reward {intent.RewardId} remains pending: {error}"); }
        }
    }
}
