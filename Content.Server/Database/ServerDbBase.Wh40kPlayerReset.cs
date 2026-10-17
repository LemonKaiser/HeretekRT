using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.Quests;
using Content.Shared.Preferences;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public Task ResetWh40kPlayerAsync(NetUserId userId, HumanoidCharacterProfile temporaryProfile,
        Guid actor, string reason, CancellationToken cancel = default)
    {
        if (userId.UserId == Guid.Empty || actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
            throw new ArgumentException("A player reset requires a target, actor and a reason of 1 to 512 characters.");
        if (temporaryProfile.BankBalance != 0 || temporaryProfile.Wh40kBuild.IsCompleteFoundation)
            throw new ArgumentException("A player reset requires an unbuilt temporary profile with no money.");

        var owner = Wh40kQuestOwnerKey.Account(userId);
        return QuestWriteAsync(owner, async db =>
        {
            if (!await db.Player.AnyAsync(player => player.UserId == userId.UserId, cancel))
                throw new InvalidOperationException("The player does not exist.");

            // The runtime must retire materialized lives and finish saves before removing their durable fence.
            if (await db.Wh40kPersistentInventories.AnyAsync(inventory => inventory.UserId == userId.UserId &&
                    (inventory.State == (int) PersistentInventorySnapshotState.Bound ||
                     inventory.State == (int) PersistentInventorySnapshotState.Staging), cancel))
                throw new InvalidOperationException("The player's persistent inventory is still busy.");

            var key = QuestOwnerKey(owner);
            var quests = db.Wh40kQuestInstances.Where(quest => quest.OwnerKey == key).Select(quest => quest.Id);
            await db.Wh40kQuestInboxes.Where(row => quests.Contains(row.QuestId)).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestReceipts.Where(row => quests.Contains(row.QuestId)).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestSubmissions.Where(row => quests.Contains(row.QuestId)).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestRewardIntents.Where(row => quests.Contains(row.QuestId) || row.Recipient == userId.UserId)
                .ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestObjectives.Where(row => quests.Contains(row.QuestId)).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestInstances.Where(row => row.OwnerKey == key).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestOffers.Where(row => row.OwnerKey == key).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestOperations.Where(row => row.OwnerKey == key).ExecuteDeleteAsync(cancel);
            await db.Wh40kQuestAccountStates.Where(row => row.UserId == userId.UserId).ExecuteDeleteAsync(cancel);

            // Paid merchant titles remain as anti-duplication tombstones, without restorable item payloads.
            await db.Wh40kMerchantSales.Where(row => row.UserId == userId.UserId && row.State == 0)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.State, 1), cancel);
            await db.Wh40kMerchantSales.Where(row => row.UserId == userId.UserId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadJson, "{}"), cancel);

            // Foundation deletion cascades to XP, attributes, classes, skills, the reward outbox and party membership.
            await db.Wh40kParties.Where(party => party.LeaderUserId != userId.UserId &&
                    db.Wh40kPartyMembers.Any(member => member.PartyId == party.Id && member.UserId == userId.UserId))
                .ExecuteUpdateAsync(update => update.SetProperty(party => party.Revision, party => party.Revision + 1), cancel);
            await db.Wh40kAccountRpgFoundations.Where(row => row.UserId == userId.UserId).ExecuteDeleteAsync(cancel);
            await db.Wh40kPersistentInventories.Where(row => row.UserId == userId.UserId).ExecuteDeleteAsync(cancel);
            await db.DialoguePersistentMemories.Where(row => row.PlayerUserId == userId.UserId).ExecuteDeleteAsync(cancel);
            await db.GhostPermissions.Where(row => row.PlayerUserId == userId.UserId).ExecuteDeleteAsync(cancel);
            await db.Wh40kDecorationSelections.Where(row => row.UserId == userId.UserId).ExecuteDeleteAsync(cancel);
            await db.WayfarerSafetyDepositBox.Where(row => row.OwnerUserId == userId.UserId).ExecuteDeleteAsync(cancel);

            // Replace every slot and MonoCoins together. Keeping an old preference would activate legacy migration.
            await db.Preference.Where(row => row.UserId == userId.UserId).ExecuteDeleteAsync(cancel);
            await db.Wh40kPlayerProgresses.Where(row => row.UserId == userId.UserId).ExecuteDeleteAsync(cancel);
            var now = DateTime.UtcNow;
            var preferences = new Preference
            {
                UserId = userId.UserId,
                SelectedCharacterSlot = 0,
                AdminOOCColor = Color.Red.ToHex(),
            };
            preferences.Profiles.Add(ConvertProfiles(temporaryProfile, 0));
            db.Preference.Add(preferences);
            db.Wh40kPlayerProgresses.Add(new Wh40kPlayerProgress
            {
                UserId = userId.UserId,
                ActStage = (int) Wh40kActStage.Act1NotStarted,
                OnboardingStatus = (int) Wh40kOnboardingStatus.Required,
                OnboardingProfileSlot = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Wh40kQuestAudits.Add(new Wh40kQuestAudit
            {
                OwnerKey = key,
                Actor = actor,
                Action = "PlayerReset",
                Reason = reason,
                At = now,
            });
            return true;
        }, cancel);
    }
}
