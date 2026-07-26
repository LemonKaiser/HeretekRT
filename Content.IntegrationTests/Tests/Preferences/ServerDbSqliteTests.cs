using System.Collections.Generic;
using System.Linq;
using System.Net;
using Content.Server._WH40K.Progression;
using Content.Server.Database;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.Progression;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Preferences
{
    [TestFixture]
    public sealed class ServerDbSqliteTests
    {
        [TestPrototypes]
        private const string Prototypes = @"
- type: dataset
  id: sqlite_test_names_first_male
  values:
  - Aaden

- type: dataset
  id: sqlite_test_names_first_female
  values:
  - Aaliyah

- type: dataset
  id: sqlite_test_names_last
  values:
  - Ackerley";

        private static HumanoidCharacterProfile CharlieCharlieson()
        {
            return new HumanoidCharacterProfile() // Frontier - added HumanoidCharacterProfile
            {
                Name = "Charlie Charlieson",
                FlavorText = "The biggest boy around.",
                Species = "Human",
                Age = 21,
                Appearance = new(
                    "Afro",
                    Color.Aqua,
                    "Shaved",
                    Color.Aquamarine,
                    Color.Azure,
                    Color.Beige,
                    new ())
            }.WithBankBalance(27000); // Frontier - accessor issue
        }

        private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
        {
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
            var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            builder.UseSqlite(conn);
            return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
        }

        [Test]
        public async Task TestUserDoesNotExist()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            // Database should be empty so a new GUID should do it.
            Assert.That(await db.GetPlayerPreferencesAsync(NewUserId()), Is.Null);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestInitPrefs()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            const int slot = 0;
            var originalProfile = CharlieCharlieson();
            await db.InitPrefsAsync(username, originalProfile);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs.Characters.Single(p => p.Key == slot).Value.MemberwiseEquals(originalProfile));
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestDeleteCharacter()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            await db.InitPrefsAsync(username, new HumanoidCharacterProfile());
            await db.SaveCharacterSlotAsync(username, CharlieCharlieson(), 1);
            await db.SaveSelectedCharacterIndexAsync(username, 1);
            await db.SaveCharacterSlotAsync(username, null, 1);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(!prefs.Characters.Any(p => p.Key != 0));
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestNoPendingDatabaseChanges()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            Assert.That(async () => await db.HasPendingModelChanges(), Is.False,
                "The database has pending model changes. Add a new migration to apply them. See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations");
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWh40kRpgFoundationIsCreatedOnlyOnce()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var userId = NewUserId();
            await db.UpdatePlayerRecord(userId, "LegacyRpgTest", IPAddress.Loopback, null);

            var firstDraft = FoundationDraft(
                "death-world",
                Wh40kRpgFoundationSource.LegacyRandom,
                Wh40kCharacteristic.Melee);
            var first = await db.GetOrCreateWh40kAccountRpgAsync(userId, firstDraft);

            var secondDraft = FoundationDraft(
                "voidborn",
                Wh40kRpgFoundationSource.Onboarding,
                Wh40kCharacteristic.Agility);
            var second = await db.GetOrCreateWh40kAccountRpgAsync(userId, secondDraft);
            var allowInvites = await db.GetWh40kPartyInvitesAllowedAsync(userId);
            var missingLedger = await db.GetWh40kExperienceLedgerEntryAsync(userId, "missing");
            var pendingRewards = await db.GetPendingWh40kRewardDeliveriesAsync(userId);
            var party = await db.GetWh40kPartyAsync(userId);

            Assert.Multiple(() =>
            {
                Assert.That(first.Foundation.HomeworldId, Is.EqualTo("death-world"));
                Assert.That(first.Foundation.Source, Is.EqualTo(Wh40kRpgFoundationSource.LegacyRandom));
                Assert.That(first.Progress.Level, Is.EqualTo(1));
                Assert.That(first.Progress.ExperienceTenths, Is.Zero);
                Assert.That(first.Progress.UnspentDevelopmentPoints, Is.Zero);
                Assert.That(first.Progress.Revision, Is.Zero);
                Assert.That(second.Foundation.HomeworldId, Is.EqualTo(first.Foundation.HomeworldId));
                Assert.That(second.Foundation.Source, Is.EqualTo(first.Foundation.Source));
                Assert.That(
                    second.Foundation.InitialCharacteristicPoints,
                    Is.EquivalentTo(first.Foundation.InitialCharacteristicPoints));
                Assert.That(allowInvites, Is.True);
                Assert.That(missingLedger, Is.Null);
                Assert.That(pendingRewards, Is.Empty);
                Assert.That(party, Is.Null);
            });

            await db.SetWh40kPartyInvitesAllowedAsync(userId, false);
            Assert.That(await db.GetWh40kPartyInvitesAllowedAsync(userId), Is.False);
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWh40kOnboardingCreatesProfileIndependentFoundation()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var userId = NewUserId();
            await db.UpdatePlayerRecord(userId, "OnboardingRpgTest", IPAddress.Loopback, null);

            var temporaryProfile = CharlieCharlieson();
            await db.InitPrefsAsync(userId, temporaryProfile);
            var onboardingDraft = FoundationDraft(
                "death-world",
                Wh40kRpgFoundationSource.Onboarding,
                Wh40kCharacteristic.Melee);
            var completedProfile = temporaryProfile.WithWh40kCharacterBuild(onboardingDraft.ToCharacterBuild());

            var completion = await db.CompleteWh40kOnboardingAsync(userId, completedProfile);
            var created = await db.GetWh40kAccountRpgAsync(userId);

            Assert.Multiple(() =>
            {
                Assert.That(completion.Status, Is.EqualTo(Wh40kOnboardingCompletionStatus.Success));
                Assert.That(created, Is.Not.Null);
                Assert.That(created!.Foundation.Source, Is.EqualTo(Wh40kRpgFoundationSource.Onboarding));
                Assert.That(created.Foundation.HomeworldId, Is.EqualTo("death-world"));
            });

            var conflictingProfile = temporaryProfile.WithWh40kCharacterBuild(
                FoundationDraft(
                    "voidborn",
                    Wh40kRpgFoundationSource.Onboarding,
                    Wh40kCharacteristic.Agility)
                    .ToCharacterBuild());
            await db.SaveCharacterSlotAsync(userId, conflictingProfile, 0);
            await db.SaveCharacterSlotAsync(userId, null, 0);
            var afterProfileDeletion = await db.GetWh40kAccountRpgAsync(userId);

            Assert.Multiple(() =>
            {
                Assert.That(afterProfileDeletion, Is.Not.Null);
                Assert.That(afterProfileDeletion!.Foundation.HomeworldId, Is.EqualTo("death-world"));
                Assert.That(
                    afterProfileDeletion.Foundation.InitialCharacteristicPoints[Wh40kCharacteristic.Melee],
                    Is.EqualTo(Wh40kCharacterBuild.MaximumAttributePoints));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWh40kRpgExperienceAndCharacteristicSpendAreAtomic()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var userId = NewUserId();
            await db.UpdatePlayerRecord(userId, "RpgStageTwoTest", IPAddress.Loopback, null);
            await db.GetOrCreateWh40kAccountRpgAsync(
                userId,
                FoundationDraft(
                    "death-world",
                    Wh40kRpgFoundationSource.LegacyRandom,
                    Wh40kCharacteristic.Melee));

            var request = new Wh40kXpAwardRequest(
                "stage-two-level-two",
                Wh40kExperienceSourceType.Story,
                Wh40kExperienceCurve.GetCumulativeExperienceTenths(2),
                42,
                "integration-test",
                """{"reason":"stage-two"}""");
            var awarded = await db.AwardWh40kExperienceAsync(userId, request);
            var duplicate = await db.AwardWh40kExperienceAsync(userId, request);
            var spent = await db.SpendWh40kCharacteristicsAsync(
                userId,
                awarded.Account.Progress.Revision,
                [
                    new Wh40kCharacteristicAllocation(Wh40kCharacteristic.Agility, 2),
                    new Wh40kCharacteristicAllocation(Wh40kCharacteristic.Endurance, 1),
                ]);
            var replayedRevision = await db.SpendWh40kCharacteristicAsync(
                userId,
                awarded.Account.Progress.Revision,
                Wh40kCharacteristic.Agility,
                1);
            var duplicatedCharacteristic = await db.SpendWh40kCharacteristicsAsync(
                userId,
                spent.Account!.Progress.Revision,
                [
                    new Wh40kCharacteristicAllocation(Wh40kCharacteristic.Melee, 1),
                    new Wh40kCharacteristicAllocation(Wh40kCharacteristic.Melee, 1),
                ]);
            var insufficient = await db.SpendWh40kCharacteristicAsync(
                userId,
                spent.Account!.Progress.Revision,
                Wh40kCharacteristic.Melee,
                1);
            var persistedLedger = await db.GetWh40kExperienceLedgerEntryAsync(userId, request.RewardId);
            var persisted = await db.GetWh40kAccountRpgAsync(userId);
            var zeroRevision = persisted!.Progress.Revision;
            var zeroAward = await db.AwardWh40kExperienceAsync(
                userId,
                new Wh40kXpAwardRequest(
                    "stage-three-antifarm-zero",
                    Wh40kExperienceSourceType.Combat,
                    0,
                    42,
                    "integration-test",
                    """{"antiFarmMultiplier":0}"""));
            var afterZeroAward = await db.GetWh40kAccountRpgAsync(userId);

            Assert.Multiple(() =>
            {
                Assert.That(awarded.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Awarded));
                Assert.That(awarded.PreviousLevel, Is.EqualTo(1));
                Assert.That(awarded.LevelsGained, Is.EqualTo(1));
                Assert.That(awarded.DevelopmentPointsAwarded, Is.EqualTo(3));
                Assert.That(awarded.Account.Progress.Level, Is.EqualTo(2));
                Assert.That(awarded.Account.Progress.UnspentDevelopmentPoints, Is.EqualTo(3));
                Assert.That(awarded.Account.Progress.Revision, Is.EqualTo(1));

                Assert.That(duplicate.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Duplicate));
                Assert.That(duplicate.Ledger.Id, Is.EqualTo(awarded.Ledger.Id));
                Assert.That(duplicate.Account.Progress.ExperienceTenths, Is.EqualTo(request.AmountTenths));
                Assert.That(duplicate.Account.Progress.Revision, Is.EqualTo(1));

                Assert.That(spent.Status, Is.EqualTo(Wh40kCharacteristicSpendStatus.Success));
                Assert.That(spent.Account!.Progress.UnspentDevelopmentPoints, Is.Zero);
                Assert.That(spent.Account.Progress.Revision, Is.EqualTo(2));
                Assert.That(
                    spent.Account.AttributePurchases[Wh40kCharacteristic.Agility].PurchasedPoints,
                    Is.EqualTo(2));
                Assert.That(
                    spent.Account.AttributePurchases[Wh40kCharacteristic.Endurance].PurchasedPoints,
                    Is.EqualTo(1));

                Assert.That(
                    replayedRevision.Status,
                    Is.EqualTo(Wh40kCharacteristicSpendStatus.RevisionMismatch));
                Assert.That(
                    duplicatedCharacteristic.Status,
                    Is.EqualTo(Wh40kCharacteristicSpendStatus.InvalidCount));
                Assert.That(
                    insufficient.Status,
                    Is.EqualTo(Wh40kCharacteristicSpendStatus.InsufficientDevelopmentPoints));

                Assert.That(persistedLedger, Is.Not.Null);
                Assert.That(persistedLedger!.RewardId, Is.EqualTo(request.RewardId));
                Assert.That(persistedLedger.SourceType, Is.EqualTo("story"));
                Assert.That(persisted, Is.Not.Null);
                Assert.That(persisted!.Progress.ExperienceTenths, Is.EqualTo(request.AmountTenths));
                Assert.That(persisted.Progress.UnspentDevelopmentPoints, Is.Zero);
                Assert.That(persisted.Progress.Revision, Is.EqualTo(2));
                Assert.That(
                    persisted.AttributePurchases[Wh40kCharacteristic.Agility].PurchasedPoints,
                    Is.EqualTo(2));
                Assert.That(
                    persisted.AttributePurchases[Wh40kCharacteristic.Endurance].PurchasedPoints,
                    Is.EqualTo(1));
                Assert.That(zeroAward.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Awarded));
                Assert.That(zeroAward.Ledger.AmountTenths, Is.Zero);
                Assert.That(afterZeroAward!.Progress.Revision, Is.EqualTo(zeroRevision));
                Assert.That(afterZeroAward.Progress.ExperienceTenths, Is.EqualTo(request.AmountTenths));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWh40kStageFiveRewardOutboxAndAdminPointsAreIdempotent()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var userId = NewUserId();
            await db.UpdatePlayerRecord(userId, "RpgStageFiveTest", IPAddress.Loopback, null);
            await db.GetOrCreateWh40kAccountRpgAsync(
                userId,
                FoundationDraft(
                    "death-world",
                    Wh40kRpgFoundationSource.LegacyRandom,
                    Wh40kCharacteristic.Melee));

            const string rewardId = "level-reward:v1:5";
            var levelReward = new Wh40kLevelRewardDefinition(
                5,
                rewardId,
                [
                    new Wh40kRewardDeliveryDraft(
                        rewardId,
                        "currency",
                        Wh40kLevelRewardCatalog.CurrencyRewardType,
                        null,
                        10_000,
                        """{"level":5}"""),
                    new Wh40kRewardDeliveryDraft(
                        rewardId,
                        "item:0",
                        Wh40kLevelRewardCatalog.ItemRewardType,
                        "BoxMRE",
                        1,
                        """{"level":5}"""),
                ]);
            var xpRequest = new Wh40kXpAwardRequest(
                "stage-five-level-five",
                Wh40kExperienceSourceType.Admin,
                Wh40kExperienceCurve.GetCumulativeExperienceTenths(5),
                ContextJson: """{"reason":"stage-five"}""",
                LevelRewards: [levelReward]);

            var awarded = await db.AwardWh40kExperienceAsync(userId, xpRequest);
            var duplicate = await db.AwardWh40kExperienceAsync(userId, xpRequest);
            var pending = await db.GetPendingWh40kRewardDeliveriesAsync(userId);
            var delivered = await db.RecordWh40kRewardDeliveryAttemptAsync(
                userId,
                pending[0].Id,
                true);
            var deliveredReplay = await db.RecordWh40kRewardDeliveryAttemptAsync(
                userId,
                pending[0].Id,
                true);
            var afterDelivery = await db.GetPendingWh40kRewardDeliveriesAsync(userId);

            var pointAudit = new Wh40kXpAwardRequest(
                "admin:development-points:stage-five",
                Wh40kExperienceSourceType.Admin,
                0,
                IssuerEntity: "integration-test",
                ContextJson: """{"operation":"development-points","reason":"stage-five"}""");
            var pointGrant = await db.GrantWh40kDevelopmentPointsAsync(userId, 4, pointAudit);
            var pointReplay = await db.GrantWh40kDevelopmentPointsAsync(userId, 4, pointAudit);

            Assert.Multiple(() =>
            {
                Assert.That(awarded.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Awarded));
                Assert.That(awarded.Account.Progress.Level, Is.EqualTo(5));
                Assert.That(duplicate.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Duplicate));
                Assert.That(pending, Has.Count.EqualTo(2));
                Assert.That(
                    pending.Select(entry => (entry.RewardId, entry.EntryId)),
                    Is.EquivalentTo(new[] { (rewardId, "currency"), (rewardId, "item:0") }));
                Assert.That(delivered, Is.True);
                Assert.That(deliveredReplay, Is.False);
                Assert.That(afterDelivery, Has.Count.EqualTo(1));
                Assert.That(pointGrant.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Awarded));
                Assert.That(pointGrant.DevelopmentPointsAwarded, Is.EqualTo(4));
                Assert.That(pointReplay.Status, Is.EqualTo(Wh40kExperienceAwardStatus.Duplicate));
                Assert.That(pointReplay.DevelopmentPointsAwarded, Is.Zero);
                Assert.That(
                    pointReplay.Account.Progress.UnspentDevelopmentPoints,
                    Is.EqualTo(awarded.DevelopmentPointsAwarded + 4));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWh40kPartyMutationsPersistAndLeaderLeaveDisbands()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var users = Enumerable.Range(0, 6).Select(_ => NewUserId()).ToArray();
            foreach (var (user, index) in users.Select((user, index) => (user, index)))
            {
                await db.UpdatePlayerRecord(user, $"RpgParty{index}", IPAddress.Loopback, null);
                await db.GetOrCreateWh40kAccountRpgAsync(
                    user,
                    FoundationDraft(
                        "death-world",
                        Wh40kRpgFoundationSource.LegacyRandom,
                        Wh40kCharacteristic.Melee));
            }

            var created = await db.CreateWh40kPartyAsync(users[0]);
            Assert.That(created.Status, Is.EqualTo(Wh40kPartyMutationStatus.Success));
            Assert.That(created.Party, Is.Not.Null);

            var party = created.Party!;
            for (var index = 1; index < 5; index++)
            {
                var added = await db.AddWh40kPartyMemberAsync(
                    party.Id,
                    users[0],
                    users[index],
                    party.Revision);
                Assert.That(added.Status, Is.EqualTo(Wh40kPartyMutationStatus.Success));
                party = added.Party!;
            }

            var full = await db.AddWh40kPartyMemberAsync(
                party.Id,
                users[0],
                users[5],
                party.Revision);
            var reloadedForMember = await db.GetWh40kPartyAsync(users[3]);
            var kicked = await db.KickWh40kPartyMemberAsync(
                users[0],
                users[4],
                party.Revision);
            Assert.That(kicked.Status, Is.EqualTo(Wh40kPartyMutationStatus.Success));
            Assert.That(await db.GetWh40kPartyAsync(users[4]), Is.Null);

            party = kicked.Party!;
            var readded = await db.AddWh40kPartyMemberAsync(
                party.Id,
                users[0],
                users[4],
                party.Revision);
            Assert.That(readded.Status, Is.EqualTo(Wh40kPartyMutationStatus.Success));
            party = readded.Party!;
            var leaderLeft = await db.LeaveWh40kPartyAsync(users[0], party.Revision);

            Assert.Multiple(() =>
            {
                Assert.That(full.Status, Is.EqualTo(Wh40kPartyMutationStatus.PartyFull));
                Assert.That(reloadedForMember, Is.Not.Null);
                Assert.That(reloadedForMember!.Id, Is.EqualTo(party.Id));
                Assert.That(reloadedForMember.Members, Has.Count.EqualTo(5));
                Assert.That(leaderLeft.Status, Is.EqualTo(Wh40kPartyMutationStatus.Success));
            });

            foreach (var user in users.Take(5))
                Assert.That(await db.GetWh40kPartyAsync(user), Is.Null);

            var expiring = await db.CreateWh40kPartyAsync(users[5]);
            Assert.That(expiring.IsSuccess, Is.True);
            Assert.That(
                await db.DeleteExpiredWh40kPartiesAsync(DateTime.UtcNow.AddDays(8)),
                Is.EqualTo(1));
            Assert.That(await db.GetWh40kPartyAsync(users[5]), Is.Null);

            await pair.CleanReturnAsync();
        }

        private static Wh40kRpgFoundationDraft FoundationDraft(
            string homeworld,
            Wh40kRpgFoundationSource source,
            Wh40kCharacteristic characteristic)
        {
            return new Wh40kRpgFoundationDraft(
                homeworld,
                "commissar",
                "soldier",
                "test-portrait-01",
                new Dictionary<Wh40kCharacteristic, int>
                {
                    [characteristic] = Wh40kCharacterBuild.MaximumAttributePoints,
                },
                source);
        }

        private static NetUserId NewUserId()
        {
            return new(Guid.NewGuid());
        }
    }
}
