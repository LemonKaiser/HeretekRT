using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server._WH40K.ClassProgression;
using Content.Server._WH40K.Progression;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.Progression;
using Content.Shared._WH40K.Administration.Mute;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Ghost.Roles;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Database
{
    public abstract class ServerDbBase
    {
        private const int Wh40kMutationRetryLimit = 8;
        private const int Wh40kMaximumRewardIdLength = 128;
        private const int Wh40kMaximumRewardEntryIdLength = 64;
        private const int Wh40kMaximumRewardTypeLength = 32;
        private const int Wh40kMaximumRewardPrototypeIdLength = 128;
        private const int Wh40kMaximumSourceTypeLength = 32;
        private const int Wh40kMaximumIssuerEntityLength = 128;
        private const int Wh40kMaximumContextJsonLength = 4096;
        private const int Wh40kMaximumClassIdLength = 64;
        private const int Wh40kMaximumSkillIdLength = 128;
        private const int Wh40kMaximumClassAuditActorLength = 128;
        private const int Wh40kMaximumClassAuditReasonLength = 1024;
        private const int PersistentInventoryMaximumPolicyIdLength = 64;
        private const int PersistentInventoryMaximumAuditActorLength = 64;
        private const int PersistentInventoryMaximumAuditReasonLength = 512;
        private const int PersistentInventoryMaximumAuditEntries = 100;

        private readonly ISawmill _opsLog;

        public event Action<DatabaseNotification>? OnNotificationReceived;

        /// <param name="opsLog">Sawmill to trace log database operations to.</param>
        public ServerDbBase(ISawmill opsLog)
        {
            _opsLog = opsLog;
        }

        #region Preferences
        public async Task<PlayerPreferences?> GetPlayerPreferencesAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext
                .Preference
                .Include(p => p.Profiles).ThenInclude(h => h.Jobs)
                .Include(p => p.Profiles).ThenInclude(h => h.Antags)
                .Include(p => p.Profiles).ThenInclude(h => h.Traits)
                .Include(p => p.Profiles)
                    .ThenInclude(h => h.Loadouts)
                    .ThenInclude(l => l.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            if (prefs is null)
                return null;

            var maxSlot = prefs.Profiles.Max(p => p.Slot) + 1;
            var profiles = new Dictionary<int, ICharacterProfile>(maxSlot);
            foreach (var profile in prefs.Profiles)
            {
                profiles[profile.Slot] = ConvertProfiles(profile);
            }

            return new PlayerPreferences(profiles, prefs.SelectedCharacterSlot, Color.FromHex(prefs.AdminOOCColor));
        }

        public async Task SaveSelectedCharacterIndexAsync(NetUserId userId, int index)
        {
            await using var db = await GetDb();

            await SetSelectedCharacterSlotAsync(userId, index, db.DbContext);

            await db.DbContext.SaveChangesAsync();
        }

        public async Task SaveCharacterSlotAsync(NetUserId userId, ICharacterProfile? profile, int slot)
        {
            await using var db = await GetDb();

            if (profile is null)
            {
                await DeleteCharacterSlot(db.DbContext, userId, slot);
                await db.DbContext.SaveChangesAsync();
                return;
            }

            if (profile is not HumanoidCharacterProfile humanoid)
            {
                // TODO: Handle other ICharacterProfile implementations properly
                throw new NotImplementedException();
            }

            var oldProfile = db.DbContext.Profile
                .Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId)
                .Include(p => p.Jobs)
                .Include(p => p.Antags)
                .Include(p => p.Traits)
                .Include(p => p.Loadouts)
                    .ThenInclude(l => l.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefault(h => h.Slot == slot);

            var newProfile = ConvertProfiles(humanoid, slot, oldProfile);
            if (oldProfile == null)
            {
                var prefs = await db.DbContext
                    .Preference
                    .Include(p => p.Profiles)
                    .SingleAsync(p => p.UserId == userId.UserId);

                prefs.Profiles.Add(newProfile);
            }

            await db.DbContext.SaveChangesAsync();
        }

        private static async Task DeleteCharacterSlot(ServerDbContext db, NetUserId userId, int slot)
        {
            var profile = await db.Profile.Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId && p.Slot == slot)
                .SingleOrDefaultAsync();

            if (profile == null)
            {
                return;
            }

            db.Profile.Remove(profile);
        }

        public async Task<PlayerPreferences> InitPrefsAsync(NetUserId userId, ICharacterProfile defaultProfile)
        {
            await using var db = await GetDb();

            var profile = ConvertProfiles((HumanoidCharacterProfile) defaultProfile, 0);
            var prefs = new Preference
            {
                UserId = userId.UserId,
                SelectedCharacterSlot = 0,
                AdminOOCColor = Color.Red.ToHex()
            };

            prefs.Profiles.Add(profile);

            db.DbContext.Preference.Add(prefs);
            var now = DateTime.UtcNow;
            db.DbContext.Wh40kPlayerProgresses.Add(new Wh40kPlayerProgress
            {
                UserId = userId.UserId,
                ActStage = (int) Wh40kActStage.Act1NotStarted,
                OnboardingStatus = (int) Wh40kOnboardingStatus.Required,
                OnboardingProfileSlot = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });

            await db.DbContext.SaveChangesAsync();

            return new PlayerPreferences(new[] {new KeyValuePair<int, ICharacterProfile>(0, defaultProfile)}, 0, Color.FromHex(prefs.AdminOOCColor));
        }

        public async Task DeleteSlotAndSetSelectedIndex(NetUserId userId, int deleteSlot, int newSlot)
        {
            await using var db = await GetDb();

            await DeleteCharacterSlot(db.DbContext, userId, deleteSlot);
            await SetSelectedCharacterSlotAsync(userId, newSlot, db.DbContext);

            await db.DbContext.SaveChangesAsync();
        }

        public async Task SaveAdminOOCColorAsync(NetUserId userId, Color color)
        {
            await using var db = await GetDb();
            var prefs = await db.DbContext
                .Preference
                .Include(p => p.Profiles)
                .SingleAsync(p => p.UserId == userId.UserId);
            prefs.AdminOOCColor = color.ToHex();

            await db.DbContext.SaveChangesAsync();

        }

        private static async Task SetSelectedCharacterSlotAsync(NetUserId userId, int newSlot, ServerDbContext db)
        {
            var prefs = await db.Preference.SingleAsync(p => p.UserId == userId.UserId);
            prefs.SelectedCharacterSlot = newSlot;
        }

        private static HumanoidCharacterProfile ConvertProfiles(Profile profile)
        {
            var jobs = profile.Jobs.ToDictionary(j => new ProtoId<JobPrototype>(j.JobName), j => (JobPriority) j.Priority);
            var antags = profile.Antags.Select(a => new ProtoId<AntagPrototype>(a.AntagName));
            var traits = profile.Traits.Select(t => new ProtoId<TraitPrototype>(t.TraitName));

            var sex = Sex.Male;
            if (Enum.TryParse<Sex>(profile.Sex, true, out var sexVal))
                sex = sexVal;

            var spawnPriority = (SpawnPriorityPreference) profile.SpawnPriority;

            var gender = sex == Sex.Male ? Gender.Male : Gender.Female;
            if (Enum.TryParse<Gender>(profile.Gender, true, out var genderVal))
                gender = genderVal;

            var balance = profile.BankBalance;

            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            var markingsRaw = profile.Markings?.Deserialize<List<string>>();

            List<Marking> markings = new();
            if (markingsRaw != null)
            {
                foreach (var marking in markingsRaw)
                {
                    var parsed = Marking.ParseFromDbString(marking);

                    if (parsed is null) continue;

                    markings.Add(parsed);
                }
            }

            var loadouts = new Dictionary<string, RoleLoadout>();

            foreach (var role in profile.Loadouts)
            {
                var loadout = new RoleLoadout(role.RoleName)
                {
                    EntityName = role.EntityName,
                };

                foreach (var group in role.Groups)
                {
                    var groupLoadouts = loadout.SelectedLoadouts.GetOrNew(group.GroupName);
                    foreach (var profLoadout in group.Loadouts)
                    {
                        groupLoadouts.Add(new Loadout()
                        {
                            Prototype = profLoadout.LoadoutName,
                        });
                    }
                }

                loadouts[role.RoleName] = loadout;
            }

            // Get the company with fallback to default "None"
            var company = profile.Company ?? "None";

            var wh40kBuild = profile.Wh40kBuild?.RootElement.Deserialize<Wh40kCharacterBuild>()?.Validated()
                ?? new Wh40kCharacterBuild();

            // Validate height and width to prevent sprite scale errors
            // Database migration set default values to 0f for existing profiles
            var height = profile.Height <= 0.005f ? 1.0f : profile.Height;
            var width = profile.Width <= 0.005f ? 1.0f : profile.Width;

            return new HumanoidCharacterProfile(
                profile.CharacterName,
                profile.FlavorText,
                profile.Species,
                profile.Age,
                sex,
                gender,
                balance,
                new HumanoidCharacterAppearance
                (
                    profile.HairName,
                    Color.FromHex(profile.HairColor),
                    profile.FacialHairName,
                    Color.FromHex(profile.FacialHairColor),
                    Color.FromHex(profile.EyeColor),
                    Color.FromHex(profile.SkinColor),
                    markings,
                    height,
                    width
                ),
                spawnPriority,
                jobs,
                (PreferenceUnavailableMode) profile.PreferenceUnavailable,
                antags.ToHashSet(),
                traits.ToHashSet(),
                loadouts,
                company,
                wh40kBuild);
        }

        private static Profile ConvertProfiles(HumanoidCharacterProfile humanoid, int slot, Profile? profile = null)
        {
            profile ??= new Profile();
            var appearance = (HumanoidCharacterAppearance) humanoid.CharacterAppearance;
            List<string> markingStrings = new();
            foreach (var marking in appearance.Markings)
            {
                markingStrings.Add(marking.ToString());
            }
            var markings = JsonSerializer.SerializeToDocument(markingStrings);

            profile.CharacterName = humanoid.Name;
            profile.FlavorText = humanoid.FlavorText;
            profile.Species = humanoid.Species;
            profile.Age = humanoid.Age;
            profile.Sex = humanoid.Sex.ToString();
            profile.Gender = humanoid.Gender.ToString();
            profile.BankBalance = humanoid.BankBalance;
            profile.HairName = appearance.HairStyleId;
            profile.HairColor = appearance.HairColor.ToHex();
            profile.FacialHairName = appearance.FacialHairStyleId;
            profile.FacialHairColor = appearance.FacialHairColor.ToHex();
            profile.EyeColor = appearance.EyeColor.ToHex();
            profile.SkinColor = appearance.SkinColor.ToHex();
            profile.Height = appearance.Height;
            profile.Width = appearance.Width;
            profile.SpawnPriority = (int) humanoid.SpawnPriority;
            profile.Markings = markings;
            profile.Slot = slot;
            profile.PreferenceUnavailable = (DbPreferenceUnavailableMode) humanoid.PreferenceUnavailable;
            profile.Company = humanoid.Company;
            profile.Wh40kBuild = JsonSerializer.SerializeToDocument(humanoid.Wh40kBuild.Validated());

            profile.Jobs.Clear();
            profile.Jobs.AddRange(
                humanoid.JobPriorities
                    .Where(j => j.Value != JobPriority.Never)
                    .Select(j => new Job {JobName = j.Key, Priority = (DbJobPriority) j.Value})
            );

            profile.Antags.Clear();
            profile.Antags.AddRange(
                humanoid.AntagPreferences
                    .Select(a => new Antag {AntagName = a})
            );

            profile.Traits.Clear();
            profile.Traits.AddRange(
                humanoid.TraitPreferences
                        .Select(t => new Trait {TraitName = t})
            );

            profile.Loadouts.Clear();

            foreach (var (role, loadouts) in humanoid.Loadouts)
            {
                var dz = new ProfileRoleLoadout()
                {
                    RoleName = role,
                    EntityName = loadouts.EntityName ?? string.Empty,
                };

                foreach (var (group, groupLoadouts) in loadouts.SelectedLoadouts)
                {
                    var profileGroup = new ProfileLoadoutGroup()
                    {
                        GroupName = group,
                    };

                    foreach (var loadout in groupLoadouts)
                    {
                        profileGroup.Loadouts.Add(new ProfileLoadout()
                        {
                            LoadoutName = loadout.Prototype,
                        });
                    }

                    dz.Groups.Add(profileGroup);
                }

                profile.Loadouts.Add(dz);
            }

            return profile;
        }
        #endregion

        #region WH40K character creation
        public async Task<Wh40kPlayerProgressSnapshot?> GetWh40kPlayerProgressAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var progress = await db.DbContext.Wh40kPlayerProgresses
                .SingleOrDefaultAsync(progress => progress.UserId == userId.UserId, cancel);

            return progress == null ? null : ToSnapshot(progress);
        }

        public async Task<Wh40kPlayerProgressSnapshot> GetOrCreateWh40kPlayerProgressAsync(
            NetUserId userId,
            Wh40kPlayerProgressSnapshot fallback,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var progress = await db.DbContext.Wh40kPlayerProgresses
                .SingleOrDefaultAsync(progress => progress.UserId == userId.UserId, cancel);

            if (progress != null)
                return ToSnapshot(progress);

            var now = DateTime.UtcNow;
            progress = new Wh40kPlayerProgress
            {
                UserId = userId.UserId,
                ActStage = (int) fallback.ActStage,
                OnboardingStatus = (int) fallback.OnboardingStatus,
                OnboardingProfileSlot = fallback.OnboardingProfileSlot,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.DbContext.Wh40kPlayerProgresses.Add(progress);
            await db.DbContext.SaveChangesAsync(cancel);
            return ToSnapshot(progress);
        }

        /// <summary>
        /// Replaces only the server-designated temporary profile. The explicit transaction keeps the profile and
        /// progress updates inseparable, so a failed save cannot unlock the account without the profile.
        /// </summary>
        public async Task<Wh40kOnboardingCompletionResult> CompleteWh40kOnboardingAsync(
            NetUserId userId,
            HumanoidCharacterProfile humanoid,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            var progress = await db.DbContext.Wh40kPlayerProgresses
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);

            if (progress == null)
            {
                return new Wh40kOnboardingCompletionResult(
                    Wh40kOnboardingCompletionStatus.NotAllowed,
                    Wh40kPlayerProgressSnapshot.Unknown,
                    -1);
            }

            var snapshot = ToSnapshot(progress);
            if (snapshot.ActStage != Wh40kActStage.Act1NotStarted ||
                snapshot.OnboardingStatus != Wh40kOnboardingStatus.Required ||
                snapshot.OnboardingProfileSlot < 0)
            {
                return new Wh40kOnboardingCompletionResult(
                    Wh40kOnboardingCompletionStatus.NotAllowed,
                    snapshot,
                    -1);
            }

            if (await db.DbContext.Wh40kAccountRpgFoundations
                    .AnyAsync(candidate => candidate.UserId == userId.UserId, cancel))
            {
                return new Wh40kOnboardingCompletionResult(
                    Wh40kOnboardingCompletionStatus.NotAllowed,
                    snapshot,
                    -1);
            }

            var slot = snapshot.OnboardingProfileSlot;
            var oldProfile = await db.DbContext.Profile
                .Include(profile => profile.Preference)
                .Where(profile => profile.Preference.UserId == userId.UserId)
                .Include(profile => profile.Jobs)
                .Include(profile => profile.Antags)
                .Include(profile => profile.Traits)
                .Include(profile => profile.Loadouts)
                    .ThenInclude(loadout => loadout.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefaultAsync(profile => profile.Slot == slot, cancel);

            if (oldProfile == null)
            {
                return new Wh40kOnboardingCompletionResult(
                    Wh40kOnboardingCompletionStatus.NotAllowed,
                    snapshot,
                    -1);
            }

            var build = humanoid.Wh40kBuild;
            if (!build.IsCompleteFoundation)
            {
                return new Wh40kOnboardingCompletionResult(
                    Wh40kOnboardingCompletionStatus.InvalidBuild,
                    snapshot,
                    -1);
            }

            var foundation = new Wh40kRpgFoundationDraft(
                build.HomeworldId!,
                build.OriginId!,
                build.ClassId!,
                build.PortraitId!,
                build.CharacteristicPoints,
                Wh40kRpgFoundationSource.Onboarding);

            ConvertProfiles(humanoid, slot, oldProfile);
            oldProfile.Preference.SelectedCharacterSlot = slot;
            progress.ActStage = (int) Wh40kActStage.Act1InProgress;
            progress.OnboardingStatus = (int) Wh40kOnboardingStatus.CharacterCreated;
            progress.UpdatedAt = DateTime.UtcNow;
            AddWh40kAccountRpg(db.DbContext, userId, foundation, progress.UpdatedAt);

            await db.DbContext.SaveChangesAsync(cancel);
            await transaction.CommitAsync(cancel);
            return new Wh40kOnboardingCompletionResult(
                Wh40kOnboardingCompletionStatus.Success,
                ToSnapshot(progress),
                slot);
        }

        public async Task<Wh40kAccountRpgRecord?> GetWh40kAccountRpgAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
        }

        public async Task<Wh40kAccountRpgRecord> GetOrCreateWh40kAccountRpgAsync(
            NetUserId userId,
            Wh40kRpgFoundationDraft foundation,
            CancellationToken cancel = default)
        {
            ValidateWh40kFoundationDraft(foundation);
            DbUpdateException? concurrentInsert = null;

            await using (var db = await GetDb(cancel))
            {
                var existing = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
                if (existing != null)
                    return existing;

                AddWh40kAccountRpg(db.DbContext, userId, foundation, DateTime.UtcNow);

                try
                {
                    await db.DbContext.SaveChangesAsync(cancel);
                    return await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                           ?? throw new InvalidOperationException($"WH40K RPG account {userId} disappeared after creation.");
                }
                catch (DbUpdateException exception)
                {
                    // A second server or connection may have won the unique UserId insert.
                    // Re-read using a clean context; any other persistence error is rethrown below.
                    concurrentInsert = exception;
                }
            }

            await using var retryDb = await GetDb(cancel);
            var concurrentlyCreated = await LoadWh40kAccountRpgAsync(retryDb.DbContext, userId, cancel);
            if (concurrentlyCreated != null)
                return concurrentlyCreated;

            throw concurrentInsert!;
        }

        public async Task<Wh40kAccountClassProgressRecord?> GetWh40kAccountClassProgressAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel);
        }

        public async Task<Wh40kClassSkillPurchaseResult> PurchaseWh40kClassSkillAsync(
            NetUserId userId,
            long expectedRevision,
            Wh40kClassSkillPurchaseSpec skill,
            int additionalSkillPoints,
            CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(skill);
            if (additionalSkillPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(additionalSkillPoints));

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
            var classProgress = await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel);
            if (account == null || classProgress == null)
            {
                return new Wh40kClassSkillPurchaseResult(
                    Wh40kClassSkillPurchaseStatus.AccountNotFound,
                    account,
                    classProgress);
            }

            var status = classProgress.TreeVersion != skill.TreeVersion
                ? Wh40kClassSkillPurchaseStatus.ContentUnavailable
                : Wh40kClassPurchasePolicy.Evaluate(
                    account.Foundation.ClassId,
                    account.Progress.Level,
                    classProgress.Revision,
                    expectedRevision,
                    classProgress.PurchasedSkillIds,
                    new Wh40kClassSkillPurchaseSpecData(
                        skill.SkillId,
                        skill.ClassId,
                        skill.PrerequisiteSkillId,
                        skill.MinimumLevel,
                        skill.Cost,
                        skill.Availability),
                    additionalSkillPoints,
                    Wh40kClassProgressionPolicy.GetSpentSkillPoints(
                        classProgress.PurchasedSkillIds,
                        skill.PersistentSkillCosts));
            if (status != Wh40kClassSkillPurchaseStatus.Success)
                return new Wh40kClassSkillPurchaseResult(status, account, classProgress);

            var now = DateTime.UtcNow;
            var revision = checked(expectedRevision + 1);
            var updated = await db.DbContext.Wh40kAccountClassProgresses
                .Where(candidate =>
                    candidate.UserId == userId.UserId &&
                    candidate.Revision == expectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.UpdatedAt, now)
                    .SetProperty(candidate => candidate.Revision, revision), cancel);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancel);
                return await GetWh40kClassPurchaseFailureAsync(
                    userId,
                    Wh40kClassSkillPurchaseStatus.RevisionMismatch,
                    cancel);
            }

            db.DbContext.Wh40kAccountClassSkills.Add(new Wh40kAccountClassSkill
            {
                UserId = userId.UserId,
                SkillId = skill.SkillId,
                PurchasedAt = now,
            });
            var newSkills = classProgress.Skills.Select(entry => entry.SkillId)
                .Append(skill.SkillId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            db.DbContext.Wh40kAccountClassAudits.Add(CreateWh40kClassAudit(
                Guid.NewGuid(),
                userId,
                Wh40kClassAdminOperation.Purchase,
                userId.ToString(),
                userId.ToString(),
                "player-purchase",
                account.Foundation.ClassId,
                account.Foundation.ClassId,
                classProgress.Skills.Select(entry => entry.SkillId),
                newSkills,
                now));

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancel);
                return await GetWh40kClassPurchaseFailureAsync(
                    userId,
                    Wh40kClassSkillPurchaseStatus.RevisionMismatch,
                    cancel);
            }

            var resultAccount = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                ?? throw new InvalidOperationException($"WH40K RPG account {userId} disappeared after skill purchase.");
            var resultProgress = await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel)
                ?? throw new InvalidOperationException($"WH40K class progress {userId} disappeared after skill purchase.");
            return new Wh40kClassSkillPurchaseResult(
                Wh40kClassSkillPurchaseStatus.Success,
                resultAccount,
                resultProgress);
        }

        public async Task<Wh40kClassAdminMutationResult> MutateWh40kClassProgressAsync(
            NetUserId userId,
            Wh40kClassAdminMutationRequest request,
            CancellationToken cancel = default)
        {
            ValidateWh40kClassAdminMutationRequest(request);
            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            var duplicate = await db.DbContext.Wh40kAccountClassAudits
                .AsNoTracking()
                .AnyAsync(audit => audit.OperationId == request.OperationId, cancel);
            if (duplicate)
            {
                return new Wh40kClassAdminMutationResult(
                    Wh40kClassSkillPurchaseStatus.Success,
                    await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel),
                    await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel));
            }

            var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
            var classProgress = await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel);
            if (account == null || classProgress == null)
            {
                return new Wh40kClassAdminMutationResult(
                    Wh40kClassSkillPurchaseStatus.AccountNotFound,
                    account,
                    classProgress);
            }

            if (classProgress.Revision != request.ExpectedRevision)
            {
                return new Wh40kClassAdminMutationResult(
                    Wh40kClassSkillPurchaseStatus.RevisionMismatch,
                    account,
                    classProgress);
            }

            var now = DateTime.UtcNow;
            var revision = checked(request.ExpectedRevision + 1);
            var updated = await db.DbContext.Wh40kAccountClassProgresses
                .Where(candidate =>
                    candidate.UserId == userId.UserId &&
                    candidate.Revision == request.ExpectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.TreeVersion, request.TreeVersion)
                    .SetProperty(candidate => candidate.UpdatedAt, now)
                    .SetProperty(candidate => candidate.Revision, revision), cancel);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancel);
                return new Wh40kClassAdminMutationResult(
                    Wh40kClassSkillPurchaseStatus.RevisionMismatch,
                    account,
                    classProgress);
            }

            await db.DbContext.Wh40kAccountRpgFoundations
                .Where(candidate => candidate.UserId == userId.UserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.ClassId, request.NewClassId), cancel);
            await db.DbContext.Wh40kAccountClassSkills
                .Where(candidate => candidate.UserId == userId.UserId)
                .ExecuteDeleteAsync(cancel);
            foreach (var skillId in request.NewSkillIds.Distinct(StringComparer.Ordinal))
            {
                db.DbContext.Wh40kAccountClassSkills.Add(new Wh40kAccountClassSkill
                {
                    UserId = userId.UserId,
                    SkillId = skillId,
                    PurchasedAt = now,
                });
            }

            db.DbContext.Wh40kAccountClassAudits.Add(CreateWh40kClassAudit(
                request.OperationId,
                userId,
                request.Operation,
                request.ActorId,
                request.ActorName,
                request.Reason,
                account.Foundation.ClassId,
                request.NewClassId,
                classProgress.Skills.Select(entry => entry.SkillId),
                request.NewSkillIds,
                now));

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancel);
                return new Wh40kClassAdminMutationResult(
                    Wh40kClassSkillPurchaseStatus.RevisionMismatch,
                    await GetWh40kAccountRpgAsync(userId, cancel),
                    await GetWh40kAccountClassProgressAsync(userId, cancel));
            }

            return new Wh40kClassAdminMutationResult(
                Wh40kClassSkillPurchaseStatus.Success,
                await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel),
                await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel));
        }

        public async Task<IReadOnlyList<Wh40kClassAuditRecord>> GetWh40kClassAuditAsync(
            NetUserId userId,
            int limit = 50,
            CancellationToken cancel = default)
        {
            if (limit is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(limit));

            await using var db = await GetDb(cancel);
            var audits = await db.DbContext.Wh40kAccountClassAudits
                .AsNoTracking()
                .Where(audit => audit.UserId == userId.UserId)
                .OrderByDescending(audit => audit.CreatedAt)
                .Take(limit)
                .ToListAsync(cancel);
            return audits.Select(ToWh40kClassAuditRecord).ToList();
        }

        public async Task<Wh40kExperienceAwardResult> AwardWh40kExperienceAsync(
            NetUserId userId,
            Wh40kXpAwardRequest request,
            CancellationToken cancel = default)
        {
            ValidateWh40kXpAwardRequest(request);
            ValidateWh40kLevelRewardDefinitions(request.LevelRewards);

            for (var attempt = 0; attempt < Wh40kMutationRetryLimit; attempt++)
            {
                await using var db = await GetDb(cancel);
                await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

                var existingLedger = await db.DbContext.Wh40kExperienceLedgers
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        entry => entry.UserId == userId.UserId && entry.RewardId == request.RewardId,
                        cancel);
                if (existingLedger != null)
                {
                    var duplicateAccount = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                        ?? throw new InvalidOperationException($"WH40K RPG account {userId} does not exist.");
                    return new Wh40kExperienceAwardResult(
                        Wh40kExperienceAwardStatus.Duplicate,
                        duplicateAccount,
                        ToWh40kExperienceLedgerRecord(existingLedger),
                        duplicateAccount.Progress.Level,
                        0,
                        0);
                }

                var progress = await db.DbContext.Wh40kAccountRpgProgresses
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel)
                    ?? throw new InvalidOperationException($"WH40K RPG account {userId} does not exist.");
                var experienceTenths = checked(progress.ExperienceTenths + request.AmountTenths);
                var level = Wh40kExperienceCurve.GetLevel(experienceTenths);
                var levelsGained = level - progress.Level;
                if (levelsGained < 0)
                {
                    throw new InvalidOperationException(
                        $"WH40K RPG account {userId} has level {progress.Level} above its XP-derived level {level}.");
                }

                var developmentPointsAwarded = checked(
                    levelsGained * Wh40kExperienceCurve.DevelopmentPointsPerLevel);
                var unspentDevelopmentPoints = checked(
                    progress.UnspentDevelopmentPoints + developmentPointsAwarded);
                var revision = request.AmountTenths == 0
                    ? progress.Revision
                    : checked(progress.Revision + 1);
                var now = DateTime.UtcNow;

                if (request.AmountTenths > 0)
                {
                    var updated = await db.DbContext.Wh40kAccountRpgProgresses
                        .Where(candidate =>
                            candidate.UserId == userId.UserId &&
                            candidate.Revision == progress.Revision)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(candidate => candidate.ExperienceTenths, experienceTenths)
                            .SetProperty(candidate => candidate.Level, level)
                            .SetProperty(candidate => candidate.UnspentDevelopmentPoints, unspentDevelopmentPoints)
                            .SetProperty(candidate => candidate.UpdatedAt, now)
                            .SetProperty(candidate => candidate.Revision, revision), cancel);
                    if (updated == 0)
                        continue;
                }

                var ledger = new Wh40kExperienceLedger
                {
                    UserId = userId.UserId,
                    RewardId = request.RewardId,
                    SourceType = ToDatabaseExperienceSourceType(request.SourceType),
                    AmountTenths = request.AmountTenths,
                    RoundId = request.RoundId,
                    IssuerEntity = request.IssuerEntity,
                    ContextJson = ParseWh40kContextJson(request.ContextJson),
                    AwardedAt = now,
                    BalanceVersion = Wh40kExperienceCurve.BalanceVersion,
                };
                db.DbContext.Wh40kExperienceLedgers.Add(ledger);
                if (levelsGained > 0 && request.LevelRewards != null)
                {
                    foreach (var definition in request.LevelRewards)
                    {
                        if (definition.Level <= progress.Level || definition.Level > level)
                            continue;

                        AddWh40kRewardDeliveries(
                            db.DbContext,
                            userId,
                            definition.Entries,
                            now);
                    }
                }

                try
                {
                    await db.DbContext.SaveChangesAsync(cancel);
                    await transaction.CommitAsync(cancel);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(cancel);
                    continue;
                }

                var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                    ?? throw new InvalidOperationException($"WH40K RPG account {userId} disappeared after XP award.");
                return new Wh40kExperienceAwardResult(
                    Wh40kExperienceAwardStatus.Awarded,
                    account,
                    ToWh40kExperienceLedgerRecord(ledger),
                    progress.Level,
                    levelsGained,
                    developmentPointsAwarded);
            }

            await using var duplicateDb = await GetDb(cancel);
            var duplicate = await duplicateDb.DbContext.Wh40kExperienceLedgers
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.UserId == userId.UserId && entry.RewardId == request.RewardId,
                    cancel);
            if (duplicate != null)
            {
                var account = await LoadWh40kAccountRpgAsync(duplicateDb.DbContext, userId, cancel)
                    ?? throw new InvalidOperationException($"WH40K RPG account {userId} disappeared after XP award.");
                return new Wh40kExperienceAwardResult(
                    Wh40kExperienceAwardStatus.Duplicate,
                    account,
                    ToWh40kExperienceLedgerRecord(duplicate),
                    account.Progress.Level,
                    0,
                    0);
            }

            throw new InvalidOperationException(
                $"WH40K RPG account {userId} could not be updated after {Wh40kMutationRetryLimit} attempts.");
        }

        public async Task<Wh40kDevelopmentPointGrantResult> GrantWh40kDevelopmentPointsAsync(
            NetUserId userId,
            int amount,
            Wh40kXpAwardRequest audit,
            CancellationToken cancel = default)
        {
            ValidateWh40kXpAwardRequest(audit);
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "WH40K development point grant must be positive.");
            if (audit.SourceType != Wh40kExperienceSourceType.Admin || audit.AmountTenths != 0)
            {
                throw new ArgumentException(
                    "WH40K development point grants require a zero-XP admin ledger entry.",
                    nameof(audit));
            }

            for (var attempt = 0; attempt < Wh40kMutationRetryLimit; attempt++)
            {
                await using var db = await GetDb(cancel);
                await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
                var existingLedger = await db.DbContext.Wh40kExperienceLedgers
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        entry => entry.UserId == userId.UserId && entry.RewardId == audit.RewardId,
                        cancel);
                if (existingLedger != null)
                {
                    var duplicateAccount = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                        ?? throw new InvalidOperationException($"WH40K RPG account {userId} does not exist.");
                    return new Wh40kDevelopmentPointGrantResult(
                        Wh40kExperienceAwardStatus.Duplicate,
                        duplicateAccount,
                        ToWh40kExperienceLedgerRecord(existingLedger),
                        0);
                }

                var progress = await db.DbContext.Wh40kAccountRpgProgresses
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel)
                    ?? throw new InvalidOperationException($"WH40K RPG account {userId} does not exist.");
                var now = DateTime.UtcNow;
                var revision = checked(progress.Revision + 1);
                var points = checked(progress.UnspentDevelopmentPoints + amount);
                var updated = await db.DbContext.Wh40kAccountRpgProgresses
                    .Where(candidate =>
                        candidate.UserId == userId.UserId &&
                        candidate.Revision == progress.Revision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.UnspentDevelopmentPoints, points)
                        .SetProperty(candidate => candidate.UpdatedAt, now)
                        .SetProperty(candidate => candidate.Revision, revision), cancel);
                if (updated == 0)
                    continue;

                var ledger = new Wh40kExperienceLedger
                {
                    UserId = userId.UserId,
                    RewardId = audit.RewardId,
                    SourceType = ToDatabaseExperienceSourceType(audit.SourceType),
                    AmountTenths = 0,
                    RoundId = audit.RoundId,
                    IssuerEntity = audit.IssuerEntity,
                    ContextJson = ParseWh40kContextJson(audit.ContextJson),
                    AwardedAt = now,
                    BalanceVersion = Wh40kExperienceCurve.BalanceVersion,
                };
                db.DbContext.Wh40kExperienceLedgers.Add(ledger);

                try
                {
                    await db.DbContext.SaveChangesAsync(cancel);
                    await transaction.CommitAsync(cancel);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(cancel);
                    continue;
                }

                var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                    ?? throw new InvalidOperationException(
                        $"WH40K RPG account {userId} disappeared after development point grant.");
                return new Wh40kDevelopmentPointGrantResult(
                    Wh40kExperienceAwardStatus.Awarded,
                    account,
                    ToWh40kExperienceLedgerRecord(ledger),
                    amount);
            }

            throw new InvalidOperationException(
                $"WH40K RPG account {userId} could not receive development points after " +
                $"{Wh40kMutationRetryLimit} attempts.");
        }

        public async Task<Wh40kCharacteristicSpendResult> SpendWh40kCharacteristicAsync(
            NetUserId userId,
            long expectedRevision,
            Wh40kCharacteristic characteristic,
            int count,
            CancellationToken cancel = default)
        {
            return await SpendWh40kCharacteristicsAsync(
                userId,
                expectedRevision,
                [new Wh40kCharacteristicAllocation(characteristic, count)],
                cancel);
        }

        public async Task<Wh40kCharacteristicSpendResult> SpendWh40kCharacteristicsAsync(
            NetUserId userId,
            long expectedRevision,
            IReadOnlyList<Wh40kCharacteristicAllocation> allocations,
            CancellationToken cancel = default)
        {
            if (allocations == null || allocations.Count == 0)
                return await GetWh40kSpendFailureAsync(userId, Wh40kCharacteristicSpendStatus.InvalidCount, cancel);

            var normalized = new Dictionary<Wh40kCharacteristic, int>();
            var totalCount = 0;
            foreach (var allocation in allocations)
            {
                if (!Enum.IsDefined(allocation.Characteristic))
                {
                    return await GetWh40kSpendFailureAsync(
                        userId,
                        Wh40kCharacteristicSpendStatus.InvalidCharacteristic,
                        cancel);
                }

                if (allocation.Count <= 0 || !normalized.TryAdd(allocation.Characteristic, allocation.Count))
                {
                    return await GetWh40kSpendFailureAsync(
                        userId,
                        Wh40kCharacteristicSpendStatus.InvalidCount,
                        cancel);
                }

                try
                {
                    totalCount = checked(totalCount + allocation.Count);
                }
                catch (OverflowException)
                {
                    return await GetWh40kSpendFailureAsync(
                        userId,
                        Wh40kCharacteristicSpendStatus.InvalidCount,
                        cancel);
                }
            }

            for (var attempt = 0; attempt < Wh40kMutationRetryLimit; attempt++)
            {
                await using var db = await GetDb(cancel);
                await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
                var progress = await db.DbContext.Wh40kAccountRpgProgresses
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
                if (progress == null)
                    return new Wh40kCharacteristicSpendResult(Wh40kCharacteristicSpendStatus.AccountNotFound, null);

                if (progress.Revision != expectedRevision)
                {
                    var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
                    return new Wh40kCharacteristicSpendResult(
                        Wh40kCharacteristicSpendStatus.RevisionMismatch,
                        account);
                }

                if (progress.UnspentDevelopmentPoints < totalCount)
                {
                    var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
                    return new Wh40kCharacteristicSpendResult(
                        Wh40kCharacteristicSpendStatus.InsufficientDevelopmentPoints,
                        account);
                }

                var purchases =
                    new Dictionary<Wh40kCharacteristic, (Wh40kAccountAttributePurchase? Entity, int Points)>();
                foreach (var (characteristic, count) in normalized)
                {
                    var purchase = await db.DbContext.Wh40kAccountAttributePurchases
                        .SingleOrDefaultAsync(candidate =>
                            candidate.UserId == userId.UserId &&
                            candidate.Characteristic == (int) characteristic,
                            cancel);
                    try
                    {
                        purchases.Add(
                            characteristic,
                            (purchase, checked((purchase?.PurchasedPoints ?? 0) + count)));
                    }
                    catch (OverflowException)
                    {
                        var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
                        return new Wh40kCharacteristicSpendResult(
                            Wh40kCharacteristicSpendStatus.InvalidCount,
                            account);
                    }
                }

                var now = DateTime.UtcNow;
                var revision = checked(progress.Revision + 1);
                var updated = await db.DbContext.Wh40kAccountRpgProgresses
                    .Where(candidate =>
                        candidate.UserId == userId.UserId &&
                        candidate.Revision == expectedRevision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            candidate => candidate.UnspentDevelopmentPoints,
                            progress.UnspentDevelopmentPoints - totalCount)
                        .SetProperty(candidate => candidate.UpdatedAt, now)
                        .SetProperty(candidate => candidate.Revision, revision), cancel);
                if (updated == 0)
                    continue;

                foreach (var (characteristic, purchase) in purchases)
                {
                    if (purchase.Entity == null)
                    {
                        db.DbContext.Wh40kAccountAttributePurchases.Add(new Wh40kAccountAttributePurchase
                        {
                            UserId = userId.UserId,
                            Characteristic = (int) characteristic,
                            PurchasedPoints = purchase.Points,
                            FirstPurchasedAt = now,
                            UpdatedAt = now,
                        });
                    }
                    else
                    {
                        purchase.Entity.PurchasedPoints = purchase.Points;
                        purchase.Entity.UpdatedAt = now;
                    }
                }

                try
                {
                    await db.DbContext.SaveChangesAsync(cancel);
                    await transaction.CommitAsync(cancel);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(cancel);
                    continue;
                }

                var result = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel)
                    ?? throw new InvalidOperationException(
                        $"WH40K RPG account {userId} disappeared after characteristic purchase.");
                return new Wh40kCharacteristicSpendResult(Wh40kCharacteristicSpendStatus.Success, result);
            }

            return await GetWh40kSpendFailureAsync(
                userId,
                Wh40kCharacteristicSpendStatus.RevisionMismatch,
                cancel);
        }

        public async Task<Wh40kExperienceLedgerRecord?> GetWh40kExperienceLedgerEntryAsync(
            NetUserId userId,
            string rewardId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var ledger = await db.DbContext.Wh40kExperienceLedgers
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.UserId == userId.UserId && entry.RewardId == rewardId,
                    cancel);

            return ledger == null ? null : ToWh40kExperienceLedgerRecord(ledger);
        }

        public async Task<IReadOnlyList<Wh40kRewardDeliveryRecord>> GetPendingWh40kRewardDeliveriesAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var expiredClaim = DateTime.UtcNow.AddMinutes(-5);
            var deliveries = await db.DbContext.Wh40kRewardDeliveries
                .AsNoTracking()
                .Where(delivery =>
                    delivery.UserId == userId.UserId &&
                    (delivery.Status == (int) Wh40kRewardDeliveryStatus.Pending ||
                     delivery.Status == (int) Wh40kRewardDeliveryStatus.Claimed &&
                     delivery.LastAttemptAt < expiredClaim))
                .OrderBy(delivery => delivery.CreatedAt)
                .ThenBy(delivery => delivery.Id)
                .ToListAsync(cancel);

            return deliveries.Select(ToWh40kRewardDeliveryRecord).ToList();
        }

        public async Task<IReadOnlyList<Wh40kRewardDeliveryRecord>> EnqueueWh40kRewardDeliveriesAsync(
            NetUserId userId,
            IReadOnlyList<Wh40kRewardDeliveryDraft> deliveries,
            CancellationToken cancel = default)
        {
            ValidateWh40kRewardDeliveries(deliveries);
            if (deliveries.Count == 0)
                return Array.Empty<Wh40kRewardDeliveryRecord>();

            for (var attempt = 0; attempt < Wh40kMutationRetryLimit; attempt++)
            {
                await using var db = await GetDb(cancel);
                var rewardIds = deliveries.Select(delivery => delivery.RewardId).Distinct().ToArray();
                var entryIds = deliveries.Select(delivery => delivery.EntryId).Distinct().ToArray();
                var existing = await db.DbContext.Wh40kRewardDeliveries
                    .AsNoTracking()
                    .Where(delivery =>
                        delivery.UserId == userId.UserId &&
                        rewardIds.Contains(delivery.RewardId) &&
                        entryIds.Contains(delivery.EntryId))
                    .ToListAsync(cancel);
                var existingKeys = existing
                    .Select(delivery => (delivery.RewardId, delivery.EntryId))
                    .ToHashSet();
                var missing = deliveries
                    .Where(delivery => !existingKeys.Contains((delivery.RewardId, delivery.EntryId)))
                    .ToArray();
                if (missing.Length == 0)
                    return existing.Select(ToWh40kRewardDeliveryRecord).ToList();

                var foundationExists = await db.DbContext.Wh40kAccountRpgFoundations
                    .AnyAsync(foundation => foundation.UserId == userId.UserId, cancel);
                if (!foundationExists)
                    throw new InvalidOperationException($"WH40K RPG account {userId} does not exist.");

                AddWh40kRewardDeliveries(db.DbContext, userId, missing, DateTime.UtcNow);
                try
                {
                    await db.DbContext.SaveChangesAsync(cancel);
                }
                catch (DbUpdateException)
                {
                    continue;
                }

                var inserted = await db.DbContext.Wh40kRewardDeliveries
                    .AsNoTracking()
                    .Where(delivery =>
                        delivery.UserId == userId.UserId &&
                        rewardIds.Contains(delivery.RewardId) &&
                        entryIds.Contains(delivery.EntryId))
                    .OrderBy(delivery => delivery.Id)
                    .ToListAsync(cancel);
                return inserted.Select(ToWh40kRewardDeliveryRecord).ToList();
            }

            throw new InvalidOperationException(
                $"WH40K reward deliveries for {userId} could not be queued after " +
                $"{Wh40kMutationRetryLimit} attempts.");
        }

        public async Task<Wh40kRewardDeliveryRecord?> ClaimWh40kRewardDeliveryAsync(
            NetUserId userId,
            long deliveryId,
            CancellationToken cancel = default)
        {
            if (deliveryId <= 0)
                throw new ArgumentOutOfRangeException(nameof(deliveryId));

            await using var db = await GetDb(cancel);
            var now = DateTime.UtcNow;
            var expiredClaim = now.AddMinutes(-5);
            var updated = await db.DbContext.Wh40kRewardDeliveries
                .Where(candidate =>
                    candidate.Id == deliveryId &&
                    candidate.UserId == userId.UserId &&
                    (candidate.Status == (int) Wh40kRewardDeliveryStatus.Pending ||
                     candidate.Status == (int) Wh40kRewardDeliveryStatus.Claimed &&
                     candidate.LastAttemptAt < expiredClaim))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.Status, (int) Wh40kRewardDeliveryStatus.Claimed)
                    .SetProperty(candidate => candidate.LastAttemptAt, now)
                    .SetProperty(candidate => candidate.AttemptCount, candidate => candidate.AttemptCount + 1),
                    cancel);
            if (updated != 1)
                return null;

            var claimed = await db.DbContext.Wh40kRewardDeliveries
                .AsNoTracking()
                .SingleAsync(candidate =>
                    candidate.Id == deliveryId &&
                    candidate.UserId == userId.UserId,
                    cancel);
            return ToWh40kRewardDeliveryRecord(claimed);
        }

        public async Task<bool> CompleteWh40kRewardDeliveryClaimAsync(
            NetUserId userId,
            long deliveryId,
            int claimAttempt,
            bool delivered,
            CancellationToken cancel = default)
        {
            if (deliveryId <= 0)
                throw new ArgumentOutOfRangeException(nameof(deliveryId));
            if (claimAttempt <= 0)
                throw new ArgumentOutOfRangeException(nameof(claimAttempt));

            await using var db = await GetDb(cancel);
            var now = DateTime.UtcNow;
            var candidates = db.DbContext.Wh40kRewardDeliveries
                .Where(candidate =>
                    candidate.Id == deliveryId &&
                    candidate.UserId == userId.UserId &&
                    candidate.Status == (int) Wh40kRewardDeliveryStatus.Claimed &&
                    candidate.AttemptCount == claimAttempt);
            var updated = delivered
                ? await candidates.ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            candidate => candidate.Status,
                            (int) Wh40kRewardDeliveryStatus.Delivered)
                        .SetProperty(candidate => candidate.DeliveredAt, now)
                        .SetProperty(candidate => candidate.LastAttemptAt, now),
                    cancel)
                : await candidates.ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            candidate => candidate.Status,
                            (int) Wh40kRewardDeliveryStatus.Pending)
                        .SetProperty(candidate => candidate.LastAttemptAt, now),
                    cancel);
            if (updated == 1)
                return true;
            if (!delivered)
                return false;

            return await db.DbContext.Wh40kRewardDeliveries
                .AsNoTracking()
                .AnyAsync(candidate =>
                    candidate.Id == deliveryId &&
                    candidate.UserId == userId.UserId &&
                    candidate.Status == (int) Wh40kRewardDeliveryStatus.Delivered &&
                    candidate.AttemptCount == claimAttempt,
                    cancel);
        }

        public async Task<Wh40kPartyRecord?> GetWh40kPartyAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var party = await LoadWh40kPartyAsync(db.DbContext, userId, cancel);
            if (party == null)
                return null;

            if (party.ExpiresAt <= DateTime.UtcNow)
            {
                await db.DbContext.Wh40kParties
                    .Where(candidate => candidate.Id == party.Id)
                    .ExecuteDeleteAsync(cancel);
                return null;
            }

            return party;
        }

        public async Task<Wh40kPartyMutationResult> CreateWh40kPartyAsync(
            NetUserId leaderUserId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            if (!await db.DbContext.Wh40kAccountRpgFoundations
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.UserId == leaderUserId.UserId, cancel))
            {
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.AccountNotFound, null);
            }

            if (await db.DbContext.Wh40kPartyMembers
                    .AsNoTracking()
                    .AnyAsync(member => member.UserId == leaderUserId.UserId, cancel))
            {
                return new Wh40kPartyMutationResult(
                    Wh40kPartyMutationStatus.AlreadyInParty,
                    await LoadWh40kPartyAsync(db.DbContext, leaderUserId, cancel));
            }

            var now = DateTime.UtcNow;
            var party = new Wh40kParty
            {
                Id = Guid.NewGuid(),
                LeaderUserId = leaderUserId.UserId,
                CreatedAt = now,
                ExpiresAt = now + Wh40kPartyManager.PartyLifetime,
                Revision = 0,
            };
            db.DbContext.Wh40kParties.Add(party);
            db.DbContext.Wh40kPartyMembers.Add(new Wh40kPartyMember
            {
                PartyId = party.Id,
                UserId = leaderUserId.UserId,
                JoinedAt = now,
            });

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
            }
            catch (DbUpdateException)
            {
                var existing = await LoadWh40kPartyAsync(db.DbContext, leaderUserId, cancel);
                return new Wh40kPartyMutationResult(
                    existing == null
                        ? Wh40kPartyMutationStatus.RevisionMismatch
                        : Wh40kPartyMutationStatus.AlreadyInParty,
                    existing);
            }

            return new Wh40kPartyMutationResult(
                Wh40kPartyMutationStatus.Success,
                await LoadWh40kPartyAsync(db.DbContext, leaderUserId, cancel));
        }

        public async Task<Wh40kPartyMutationResult> AddWh40kPartyMemberAsync(
            Guid partyId,
            NetUserId leaderUserId,
            NetUserId memberUserId,
            long expectedRevision,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            var party = await db.DbContext.Wh40kParties
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == partyId, cancel);
            if (party == null)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.PartyNotFound, null);
            if (party.ExpiresAt <= DateTime.UtcNow)
            {
                await db.DbContext.Wh40kParties
                    .Where(candidate => candidate.Id == party.Id)
                    .ExecuteDeleteAsync(cancel);
                await transaction.CommitAsync(cancel);
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.PartyExpired, null);
            }
            if (party.LeaderUserId != leaderUserId.UserId)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotLeader, null);
            if (party.Revision != expectedRevision)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);
            if (!await db.DbContext.Wh40kAccountRpgFoundations
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.UserId == memberUserId.UserId, cancel))
            {
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.AccountNotFound, null);
            }
            if (await db.DbContext.Wh40kPartyMembers
                    .AsNoTracking()
                    .AnyAsync(member => member.UserId == memberUserId.UserId, cancel))
            {
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.AlreadyInParty, null);
            }
            if (await db.DbContext.Wh40kPartyMembers
                    .AsNoTracking()
                    .CountAsync(member => member.PartyId == partyId, cancel) >= 5)
            {
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.PartyFull, null);
            }

            var updated = await db.DbContext.Wh40kParties
                .Where(candidate => candidate.Id == partyId && candidate.Revision == expectedRevision)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.Revision, expectedRevision + 1),
                    cancel);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancel);
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);
            }

            db.DbContext.Wh40kPartyMembers.Add(new Wh40kPartyMember
            {
                PartyId = partyId,
                UserId = memberUserId.UserId,
                JoinedAt = DateTime.UtcNow,
            });

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancel);
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);
            }

            return new Wh40kPartyMutationResult(
                Wh40kPartyMutationStatus.Success,
                await LoadWh40kPartyAsync(db.DbContext, memberUserId, cancel));
        }

        public async Task<Wh40kPartyMutationResult> LeaveWh40kPartyAsync(
            NetUserId userId,
            long expectedRevision,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            var membership = await db.DbContext.Wh40kPartyMembers
                .AsNoTracking()
                .SingleOrDefaultAsync(member => member.UserId == userId.UserId, cancel);
            if (membership == null)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotInParty, null);

            var party = await db.DbContext.Wh40kParties
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == membership.PartyId, cancel);
            if (party.Revision != expectedRevision)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);

            if (party.LeaderUserId == userId.UserId || party.ExpiresAt <= DateTime.UtcNow)
            {
                await db.DbContext.Wh40kParties
                    .Where(candidate => candidate.Id == party.Id && candidate.Revision == expectedRevision)
                    .ExecuteDeleteAsync(cancel);
                await transaction.CommitAsync(cancel);
                return new Wh40kPartyMutationResult(
                    party.ExpiresAt <= DateTime.UtcNow
                        ? Wh40kPartyMutationStatus.PartyExpired
                        : Wh40kPartyMutationStatus.Success,
                    null);
            }

            var updated = await db.DbContext.Wh40kParties
                .Where(candidate => candidate.Id == party.Id && candidate.Revision == expectedRevision)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.Revision, expectedRevision + 1),
                    cancel);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancel);
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);
            }

            await db.DbContext.Wh40kPartyMembers
                .Where(member => member.PartyId == party.Id && member.UserId == userId.UserId)
                .ExecuteDeleteAsync(cancel);
            await transaction.CommitAsync(cancel);
            return new Wh40kPartyMutationResult(
                Wh40kPartyMutationStatus.Success,
                await LoadWh40kPartyAsync(
                    db.DbContext,
                    new NetUserId(party.LeaderUserId),
                    cancel));
        }

        public async Task<Wh40kPartyMutationResult> KickWh40kPartyMemberAsync(
            NetUserId leaderUserId,
            NetUserId memberUserId,
            long expectedRevision,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            var leaderMembership = await db.DbContext.Wh40kPartyMembers
                .AsNoTracking()
                .SingleOrDefaultAsync(member => member.UserId == leaderUserId.UserId, cancel);
            if (leaderMembership == null)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotInParty, null);

            var party = await db.DbContext.Wh40kParties
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == leaderMembership.PartyId, cancel);
            if (party.LeaderUserId != leaderUserId.UserId || memberUserId == leaderUserId)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotLeader, null);
            if (party.ExpiresAt <= DateTime.UtcNow)
            {
                await db.DbContext.Wh40kParties
                    .Where(candidate => candidate.Id == party.Id)
                    .ExecuteDeleteAsync(cancel);
                await transaction.CommitAsync(cancel);
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.PartyExpired, null);
            }
            if (party.Revision != expectedRevision)
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);
            if (!await db.DbContext.Wh40kPartyMembers
                    .AsNoTracking()
                    .AnyAsync(member =>
                        member.PartyId == party.Id &&
                        member.UserId == memberUserId.UserId,
                        cancel))
            {
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotInParty, null);
            }

            var updated = await db.DbContext.Wh40kParties
                .Where(candidate => candidate.Id == party.Id && candidate.Revision == expectedRevision)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.Revision, expectedRevision + 1),
                    cancel);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancel);
                return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.RevisionMismatch, null);
            }

            await db.DbContext.Wh40kPartyMembers
                .Where(member =>
                    member.PartyId == party.Id &&
                    member.UserId == memberUserId.UserId)
                .ExecuteDeleteAsync(cancel);
            await transaction.CommitAsync(cancel);
            return new Wh40kPartyMutationResult(
                Wh40kPartyMutationStatus.Success,
                await LoadWh40kPartyAsync(db.DbContext, leaderUserId, cancel));
        }

        public async Task<int> DeleteExpiredWh40kPartiesAsync(
            DateTime now,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.Wh40kParties
                .Where(party => party.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancel);
        }

        public async Task<bool> GetWh40kPartyInvitesAllowedAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var preference = await db.DbContext.Wh40kPartyPreferences
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            return preference?.AllowInvites ?? true;
        }

        public async Task SetWh40kPartyInvitesAllowedAsync(
            NetUserId userId,
            bool allowInvites,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var preference = await db.DbContext.Wh40kPartyPreferences
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);

            if (preference == null)
            {
                preference = new Wh40kPartyPreference
                {
                    UserId = userId.UserId,
                };
                db.DbContext.Wh40kPartyPreferences.Add(preference);
            }

            preference.AllowInvites = allowInvites;
            await db.DbContext.SaveChangesAsync(cancel);
        }

        private static async Task<Wh40kAccountRpgRecord?> LoadWh40kAccountRpgAsync(
            ServerDbContext db,
            NetUserId userId,
            CancellationToken cancel)
        {
            var foundation = await db.Wh40kAccountRpgFoundations
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (foundation == null)
                return null;

            var progress = await db.Wh40kAccountRpgProgresses
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel)
                ?? throw new InvalidOperationException($"WH40K RPG account {userId} has no progress row.");

            var purchases = await db.Wh40kAccountAttributePurchases
                .AsNoTracking()
                .Where(purchase => purchase.UserId == userId.UserId)
                .ToListAsync(cancel);

            var foundationRecord = ToWh40kFoundationRecord(foundation);
            var progressRecord = new Wh40kRpgProgressRecord(
                userId,
                progress.SchemaVersion,
                progress.ExperienceTenths,
                progress.Level,
                progress.UnspentDevelopmentPoints,
                progress.CreatedAt,
                progress.UpdatedAt,
                progress.Revision);
            var purchaseRecords = new Dictionary<Wh40kCharacteristic, Wh40kAttributePurchaseRecord>();

            foreach (var purchase in purchases)
            {
                if (purchase.Characteristic is < byte.MinValue or > byte.MaxValue ||
                    !Enum.IsDefined(typeof(Wh40kCharacteristic), (byte) purchase.Characteristic))
                {
                    throw new InvalidOperationException(
                        $"WH40K RPG account {userId} has unknown characteristic {purchase.Characteristic}.");
                }

                var characteristic = (Wh40kCharacteristic) purchase.Characteristic;
                purchaseRecords.Add(
                    characteristic,
                    new Wh40kAttributePurchaseRecord(
                        characteristic,
                        purchase.PurchasedPoints,
                        purchase.FirstPurchasedAt,
                        purchase.UpdatedAt));
            }

            return new Wh40kAccountRpgRecord(foundationRecord, progressRecord, purchaseRecords);
        }

        private static async Task<Wh40kAccountClassProgressRecord?> LoadWh40kAccountClassProgressAsync(
            ServerDbContext db,
            NetUserId userId,
            CancellationToken cancel)
        {
            var progress = await db.Wh40kAccountClassProgresses
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (progress == null)
                return null;

            var skills = await db.Wh40kAccountClassSkills
                .AsNoTracking()
                .Where(skill => skill.UserId == userId.UserId)
                .OrderBy(skill => skill.SkillId)
                .Select(skill => new Wh40kAccountClassSkillRecord(skill.SkillId, skill.PurchasedAt))
                .ToListAsync(cancel);
            return new Wh40kAccountClassProgressRecord(
                userId,
                progress.TreeVersion,
                progress.Revision,
                progress.CreatedAt,
                progress.UpdatedAt,
                skills);
        }

        private static async Task<Wh40kPartyRecord?> LoadWh40kPartyAsync(
            ServerDbContext db,
            NetUserId userId,
            CancellationToken cancel)
        {
            var membership = await db.Wh40kPartyMembers
                .AsNoTracking()
                .SingleOrDefaultAsync(member => member.UserId == userId.UserId, cancel);
            if (membership == null)
                return null;

            var party = await db.Wh40kParties
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == membership.PartyId, cancel);
            if (party == null)
                return null;

            var memberEntities = await db.Wh40kPartyMembers
                .AsNoTracking()
                .Where(member => member.PartyId == party.Id)
                .OrderBy(member => member.JoinedAt)
                .ThenBy(member => member.UserId)
                .ToListAsync(cancel);
            var members = memberEntities
                .Select(member => new Wh40kPartyMemberRecord(
                    new NetUserId(member.UserId),
                    member.JoinedAt))
                .ToList();

            return new Wh40kPartyRecord(
                party.Id,
                new NetUserId(party.LeaderUserId),
                party.CreatedAt,
                party.ExpiresAt,
                party.Revision,
                members);
        }

        private static void AddWh40kAccountRpg(
            ServerDbContext db,
            NetUserId userId,
            Wh40kRpgFoundationDraft foundation,
            DateTime now)
        {
            ValidateWh40kFoundationDraft(foundation);
            db.Wh40kAccountRpgFoundations.Add(new Wh40kAccountRpgFoundation
            {
                UserId = userId.UserId,
                HomeworldId = foundation.HomeworldId,
                OriginId = foundation.OriginId,
                ClassId = foundation.ClassId,
                InitialPortraitId = foundation.InitialPortraitId,
                InitialCharacteristicPoints = JsonSerializer.SerializeToDocument(
                    new Dictionary<Wh40kCharacteristic, int>(foundation.InitialCharacteristicPoints)),
                Source = ToDatabaseFoundationSource(foundation.Source),
                CreatedAt = now,
            });
            db.Wh40kAccountRpgProgresses.Add(new Wh40kAccountRpgProgress
            {
                UserId = userId.UserId,
                SchemaVersion = Wh40kExperienceCurve.ProgressSchemaVersion,
                ExperienceTenths = 0,
                Level = Wh40kExperienceCurve.MinimumLevel,
                UnspentDevelopmentPoints = 0,
                CreatedAt = now,
                UpdatedAt = now,
                Revision = 0,
            });
            db.Wh40kAccountClassProgresses.Add(new Wh40kAccountClassProgress
            {
                UserId = userId.UserId,
                TreeVersion = Wh40kClassProgressionConstants.TreeVersion,
                Revision = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Wh40kPartyPreferences.Add(new Wh40kPartyPreference
            {
                UserId = userId.UserId,
                AllowInvites = true,
            });
        }

        private static void ValidateWh40kFoundationDraft(Wh40kRpgFoundationDraft foundation)
        {
            if (!Enum.IsDefined(foundation.Source) || !foundation.ToCharacterBuild().IsCompleteFoundation)
                throw new ArgumentException("WH40K RPG foundation is incomplete or invalid.", nameof(foundation));
        }

        private async Task<Wh40kCharacteristicSpendResult> GetWh40kSpendFailureAsync(
            NetUserId userId,
            Wh40kCharacteristicSpendStatus status,
            CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
            return new Wh40kCharacteristicSpendResult(
                account == null ? Wh40kCharacteristicSpendStatus.AccountNotFound : status,
                account);
        }

        private async Task<Wh40kClassSkillPurchaseResult> GetWh40kClassPurchaseFailureAsync(
            NetUserId userId,
            Wh40kClassSkillPurchaseStatus status,
            CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var account = await LoadWh40kAccountRpgAsync(db.DbContext, userId, cancel);
            var classProgress = await LoadWh40kAccountClassProgressAsync(db.DbContext, userId, cancel);
            return new Wh40kClassSkillPurchaseResult(
                account == null || classProgress == null
                    ? Wh40kClassSkillPurchaseStatus.AccountNotFound
                    : status,
                account,
                classProgress);
        }

        private static Wh40kAccountClassAudit CreateWh40kClassAudit(
            Guid operationId,
            NetUserId userId,
            Wh40kClassAdminOperation operation,
            string actorId,
            string actorName,
            string reason,
            string previousClassId,
            string newClassId,
            IEnumerable<string> previousSkillIds,
            IEnumerable<string> newSkillIds,
            DateTime now)
        {
            return new Wh40kAccountClassAudit
            {
                OperationId = operationId,
                UserId = userId.UserId,
                Operation = operation.ToString(),
                ActorId = actorId,
                ActorName = actorName,
                Reason = reason,
                PreviousClassId = previousClassId,
                NewClassId = newClassId,
                PreviousSkillIds = JsonSerializer.SerializeToDocument(
                    previousSkillIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)),
                NewSkillIds = JsonSerializer.SerializeToDocument(
                    newSkillIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)),
                CreatedAt = now,
            };
        }

        private static Wh40kClassAuditRecord ToWh40kClassAuditRecord(Wh40kAccountClassAudit audit)
        {
            return new Wh40kClassAuditRecord(
                audit.OperationId,
                new NetUserId(audit.UserId),
                audit.Operation,
                audit.ActorId,
                audit.ActorName,
                audit.Reason,
                audit.PreviousClassId,
                audit.NewClassId,
                audit.PreviousSkillIds.Deserialize<List<string>>() ?? [],
                audit.NewSkillIds.Deserialize<List<string>>() ?? [],
                audit.CreatedAt);
        }

        private static void ValidateWh40kClassAdminMutationRequest(Wh40kClassAdminMutationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Operation == Wh40kClassAdminOperation.Purchase || !Enum.IsDefined(request.Operation))
                throw new ArgumentException("WH40K class admin operation is invalid.", nameof(request));
            if (request.ExpectedRevision < 0)
                throw new ArgumentOutOfRangeException(nameof(request));
            if (request.TreeVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.NewClassId) || request.NewClassId.Length > Wh40kMaximumClassIdLength)
                throw new ArgumentException("WH40K class ID is empty or too long.", nameof(request));
            if (request.NewSkillIds.Count > Wh40kClassProgressionConstants.MaximumSkillsPerClass ||
                request.NewSkillIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > Wh40kMaximumSkillIdLength) ||
                request.NewSkillIds.Distinct(StringComparer.Ordinal).Count() != request.NewSkillIds.Count)
            {
                throw new ArgumentException("WH40K class skill set is invalid.", nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.ActorId) ||
                request.ActorId.Length > Wh40kMaximumClassAuditActorLength ||
                string.IsNullOrWhiteSpace(request.ActorName) ||
                request.ActorName.Length > Wh40kMaximumClassAuditActorLength)
            {
                throw new ArgumentException("WH40K class audit actor is invalid.", nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.Reason) ||
                request.Reason.Length > Wh40kMaximumClassAuditReasonLength)
            {
                throw new ArgumentException("WH40K class audit reason is empty or too long.", nameof(request));
            }
        }

        private static void ValidateWh40kXpAwardRequest(Wh40kXpAwardRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.RewardId) ||
                request.RewardId.Length > Wh40kMaximumRewardIdLength)
            {
                throw new ArgumentException("WH40K XP RewardId is empty or too long.", nameof(request));
            }

            if (!Enum.IsDefined(request.SourceType) ||
                ToDatabaseExperienceSourceType(request.SourceType).Length > Wh40kMaximumSourceTypeLength)
            {
                throw new ArgumentException("WH40K XP source type is unknown.", nameof(request));
            }

            if (request.AmountTenths < 0)
                throw new ArgumentOutOfRangeException(nameof(request), "WH40K XP amount cannot be negative.");

            if (request.RoundId < 0)
                throw new ArgumentOutOfRangeException(nameof(request), "WH40K XP round ID cannot be negative.");

            if (request.IssuerEntity?.Length > Wh40kMaximumIssuerEntityLength)
                throw new ArgumentException("WH40K XP issuer context is too long.", nameof(request));

            if (request.ContextJson?.Length > Wh40kMaximumContextJsonLength)
                throw new ArgumentException("WH40K XP diagnostic context is too long.", nameof(request));

            if (request.ContextJson != null)
            {
                try
                {
                    using var _ = JsonDocument.Parse(request.ContextJson);
                }
                catch (JsonException exception)
                {
                    throw new ArgumentException("WH40K XP diagnostic context is not valid JSON.", nameof(request), exception);
                }
            }
        }

        private static void ValidateWh40kLevelRewardDefinitions(
            IReadOnlyList<Wh40kLevelRewardDefinition>? definitions)
        {
            if (definitions == null)
                return;

            var levels = new HashSet<int>();
            foreach (var definition in definitions)
            {
                if (definition.Level <= Wh40kExperienceCurve.MinimumLevel ||
                    definition.Level > Wh40kExperienceCurve.MaximumLevel ||
                    !levels.Add(definition.Level))
                {
                    throw new ArgumentException("WH40K level reward definitions contain an invalid or duplicate level.");
                }

                if (string.IsNullOrWhiteSpace(definition.RewardId) ||
                    definition.Entries.Count == 0 ||
                    definition.Entries.Any(entry => entry.RewardId != definition.RewardId))
                {
                    throw new ArgumentException("WH40K level reward definition is incomplete or inconsistent.");
                }

                ValidateWh40kRewardDeliveries(definition.Entries);
            }
        }

        private static void ValidateWh40kRewardDeliveries(IReadOnlyList<Wh40kRewardDeliveryDraft> deliveries)
        {
            ArgumentNullException.ThrowIfNull(deliveries);
            var keys = new HashSet<(string RewardId, string EntryId)>();

            foreach (var delivery in deliveries)
            {
                if (string.IsNullOrWhiteSpace(delivery.RewardId) ||
                    delivery.RewardId.Length > Wh40kMaximumRewardIdLength)
                {
                    throw new ArgumentException("WH40K reward delivery RewardId is empty or too long.");
                }

                if (string.IsNullOrWhiteSpace(delivery.EntryId) ||
                    delivery.EntryId.Length > Wh40kMaximumRewardEntryIdLength ||
                    !keys.Add((delivery.RewardId, delivery.EntryId)))
                {
                    throw new ArgumentException("WH40K reward delivery EntryId is empty, too long or duplicated.");
                }

                if (string.IsNullOrWhiteSpace(delivery.RewardType) ||
                    delivery.RewardType.Length > Wh40kMaximumRewardTypeLength ||
                    delivery.RewardType is not (
                        Wh40kLevelRewardCatalog.CurrencyRewardType or
                        Wh40kLevelRewardCatalog.ItemRewardType))
                {
                    throw new ArgumentException("WH40K reward delivery type is unknown.");
                }

                if (delivery.Amount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(deliveries), "WH40K reward amount must be positive.");

                if (delivery.RewardType == Wh40kLevelRewardCatalog.ItemRewardType &&
                    delivery.Amount > Wh40kLevelRewardCatalog.MaximumItemDeliveryCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(deliveries),
                        "WH40K item reward count exceeds the delivery limit.");
                }

                if (delivery.RewardType == Wh40kLevelRewardCatalog.CurrencyRewardType &&
                    delivery.Amount > Wh40kLevelRewardCatalog.MaximumCurrencyDeliveryAmount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(deliveries),
                        "WH40K currency reward exceeds the stack limit.");
                }

                if (delivery.RewardType == Wh40kLevelRewardCatalog.ItemRewardType &&
                    (string.IsNullOrWhiteSpace(delivery.PrototypeId) ||
                     delivery.PrototypeId.Length > Wh40kMaximumRewardPrototypeIdLength))
                {
                    throw new ArgumentException("WH40K item reward requires a valid prototype ID.");
                }

                if (delivery.RewardType == Wh40kLevelRewardCatalog.CurrencyRewardType &&
                    delivery.PrototypeId != null)
                {
                    throw new ArgumentException("WH40K currency reward cannot specify an item prototype.");
                }

                if (delivery.ContextJson?.Length > Wh40kMaximumContextJsonLength)
                    throw new ArgumentException("WH40K reward diagnostic context is too long.");

                if (delivery.ContextJson != null)
                {
                    try
                    {
                        using var _ = JsonDocument.Parse(delivery.ContextJson);
                    }
                    catch (JsonException exception)
                    {
                        throw new ArgumentException(
                            "WH40K reward diagnostic context is not valid JSON.",
                            nameof(deliveries),
                            exception);
                    }
                }
            }
        }

        private static void AddWh40kRewardDeliveries(
            ServerDbContext db,
            NetUserId userId,
            IEnumerable<Wh40kRewardDeliveryDraft> deliveries,
            DateTime createdAt)
        {
            foreach (var delivery in deliveries)
            {
                db.Wh40kRewardDeliveries.Add(new Wh40kRewardDelivery
                {
                    UserId = userId.UserId,
                    RewardId = delivery.RewardId,
                    EntryId = delivery.EntryId,
                    RewardType = delivery.RewardType,
                    PrototypeId = delivery.PrototypeId,
                    Amount = delivery.Amount,
                    ContextJson = ParseWh40kContextJson(delivery.ContextJson),
                    Status = (int) Wh40kRewardDeliveryStatus.Pending,
                    CreatedAt = createdAt,
                    AttemptCount = 0,
                });
            }
        }

        private static JsonDocument? ParseWh40kContextJson(string? contextJson)
        {
            return contextJson == null ? null : JsonDocument.Parse(contextJson);
        }

        private static string ToDatabaseExperienceSourceType(Wh40kExperienceSourceType source)
        {
            return source switch
            {
                Wh40kExperienceSourceType.Mission => "mission",
                Wh40kExperienceSourceType.Objective => "objective",
                Wh40kExperienceSourceType.Combat => "combat",
                Wh40kExperienceSourceType.Support => "support",
                Wh40kExperienceSourceType.Story => "story",
                Wh40kExperienceSourceType.Admin => "admin",
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
        }

        private static Wh40kRpgFoundationRecord ToWh40kFoundationRecord(Wh40kAccountRpgFoundation foundation)
        {
            var points = foundation.InitialCharacteristicPoints.RootElement
                .Deserialize<Dictionary<Wh40kCharacteristic, int>>()
                ?? throw new InvalidOperationException(
                    $"WH40K RPG foundation {foundation.UserId} has no characteristic allocation.");
            var source = foundation.Source switch
            {
                "onboarding" => Wh40kRpgFoundationSource.Onboarding,
                "legacy-random" => Wh40kRpgFoundationSource.LegacyRandom,
                _ => throw new InvalidOperationException(
                    $"WH40K RPG foundation {foundation.UserId} has unknown source '{foundation.Source}'."),
            };
            var record = new Wh40kRpgFoundationRecord(
                new NetUserId(foundation.UserId),
                foundation.HomeworldId,
                foundation.OriginId,
                foundation.ClassId,
                foundation.InitialPortraitId,
                points,
                source,
                foundation.CreatedAt);

            if (!record.ToCharacterBuild().IsCompleteFoundation)
                throw new InvalidOperationException($"WH40K RPG foundation {foundation.UserId} is invalid.");

            return record;
        }

        private static Wh40kExperienceLedgerRecord ToWh40kExperienceLedgerRecord(Wh40kExperienceLedger ledger)
        {
            return new Wh40kExperienceLedgerRecord(
                ledger.Id,
                new NetUserId(ledger.UserId),
                ledger.RewardId,
                ledger.SourceType,
                ledger.AmountTenths,
                ledger.RoundId,
                ledger.IssuerEntity,
                ledger.ContextJson?.RootElement.GetRawText(),
                ledger.AwardedAt,
                ledger.BalanceVersion);
        }

        private static Wh40kRewardDeliveryRecord ToWh40kRewardDeliveryRecord(Wh40kRewardDelivery delivery)
        {
            if (delivery.Status is < byte.MinValue or > byte.MaxValue ||
                !Enum.IsDefined(typeof(Wh40kRewardDeliveryStatus), (byte) delivery.Status))
            {
                throw new InvalidOperationException(
                    $"WH40K reward delivery {delivery.Id} has unknown status {delivery.Status}.");
            }

            return new Wh40kRewardDeliveryRecord(
                delivery.Id,
                new NetUserId(delivery.UserId),
                delivery.RewardId,
                delivery.EntryId,
                delivery.RewardType,
                delivery.PrototypeId,
                delivery.Amount,
                delivery.ContextJson?.RootElement.GetRawText(),
                (Wh40kRewardDeliveryStatus) delivery.Status,
                delivery.CreatedAt,
                delivery.DeliveredAt,
                delivery.AttemptCount,
                delivery.LastAttemptAt);
        }

        private static string ToDatabaseFoundationSource(Wh40kRpgFoundationSource source)
        {
            return source switch
            {
                Wh40kRpgFoundationSource.Onboarding => "onboarding",
                Wh40kRpgFoundationSource.LegacyRandom => "legacy-random",
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
        }

        private static Wh40kPlayerProgressSnapshot ToSnapshot(Wh40kPlayerProgress progress)
        {
            if (progress.ActStage is < byte.MinValue or > byte.MaxValue ||
                progress.OnboardingStatus is < byte.MinValue or > byte.MaxValue ||
                !Enum.IsDefined(typeof(Wh40kActStage), (byte) progress.ActStage) ||
                !Enum.IsDefined(typeof(Wh40kOnboardingStatus), (byte) progress.OnboardingStatus))
            {
                return Wh40kPlayerProgressSnapshot.Unknown;
            }

            var snapshot = new Wh40kPlayerProgressSnapshot(
                (Wh40kActStage) progress.ActStage,
                (Wh40kOnboardingStatus) progress.OnboardingStatus,
                progress.OnboardingProfileSlot);

            return snapshot is
                { ActStage: Wh40kActStage.Act1NotStarted, OnboardingStatus: Wh40kOnboardingStatus.Required, OnboardingProfileSlot: >= 0 } or
                { ActStage: Wh40kActStage.Act1InProgress, OnboardingStatus: Wh40kOnboardingStatus.CharacterCreated, OnboardingProfileSlot: >= 0 } or
                { ActStage: Wh40kActStage.Act1Completed, OnboardingStatus: Wh40kOnboardingStatus.CharacterCreated }
                ? snapshot
                : Wh40kPlayerProgressSnapshot.Unknown;
        }
        #endregion

        #region WH40K persistent inventory

        public async Task<PersistentInventorySnapshotHeader?> GetPersistentInventoryHeaderAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel);
        }

        public async Task<PersistentInventoryStoredRevision?> GetPersistentInventoryRevisionAsync(
            NetUserId userId,
            PersistentInventorySnapshotId snapshotId,
            CancellationToken cancel = default)
        {
            if (snapshotId.Value == Guid.Empty)
                throw new ArgumentException("Идентификатор снимка не может быть пустым.", nameof(snapshotId));

            await using var db = await GetDb(cancel);
            var revision = await db.DbContext.Wh40kPersistentInventoryRevisions
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.UserId == userId.UserId &&
                    candidate.SnapshotId == snapshotId.Value,
                    cancel);

            return revision == null
                ? null
                : new PersistentInventoryStoredRevision(
                    new PersistentInventoryAccountId(revision.UserId),
                    ToPersistentInventoryRevisionMetadata(revision),
                    revision.Payload);
        }

        public async Task<IReadOnlyList<PersistentInventoryAuditRecord>> GetPersistentInventoryAuditAsync(
            NetUserId userId,
            int limit = 50,
            CancellationToken cancel = default)
        {
            if (limit is < 1 or > PersistentInventoryMaximumAuditEntries)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(limit),
                    $"Число записей аудита должно быть от 1 до {PersistentInventoryMaximumAuditEntries}.");
            }

            await using var db = await GetDb(cancel);
            var entries = await db.DbContext.Wh40kPersistentInventoryAudits
                .AsNoTracking()
                .Where(entry => entry.UserId == userId.UserId)
                .OrderByDescending(entry => entry.Id)
                .Take(limit)
                .ToListAsync(cancel);

            return entries.Select(ToPersistentInventoryAuditRecord).ToList();
        }

        public async Task<PersistentInventorySnapshotId?> GetLatestPersistentInventoryLostSnapshotAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var snapshotId = await FindLatestPersistentInventoryLostSnapshotIdAsync(
                db.DbContext,
                userId.UserId,
                cancel);
            return snapshotId == null
                ? null
                : new PersistentInventorySnapshotId(snapshotId.Value);
        }

        public async Task<IReadOnlyList<PersistentInventorySnapshotHeader>> GetPersistentInventoryStagingAsync(
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var userIds = await db.DbContext.Wh40kPersistentInventories
                .AsNoTracking()
                .Where(account => account.State == (int) PersistentInventorySnapshotState.Staging)
                .OrderBy(account => account.UpdatedAt)
                .Select(account => account.UserId)
                .ToListAsync(cancel);

            var headers = new List<PersistentInventorySnapshotHeader>(userIds.Count);
            foreach (var userId in userIds)
            {
                var header = await LoadPersistentInventoryHeaderAsync(db.DbContext, userId, cancel);
                if (header != null)
                    headers.Add(header);
            }

            return headers;
        }

        public async Task<IReadOnlyList<PersistentInventorySnapshotHeader>> GetPersistentInventoryBoundAsync(
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var userIds = await db.DbContext.Wh40kPersistentInventories
                .AsNoTracking()
                .Where(account => account.State == (int) PersistentInventorySnapshotState.Bound)
                .OrderBy(account => account.UpdatedAt)
                .Select(account => account.UserId)
                .ToListAsync(cancel);

            var headers = new List<PersistentInventorySnapshotHeader>(userIds.Count);
            foreach (var userId in userIds)
            {
                var header = await LoadPersistentInventoryHeaderAsync(db.DbContext, userId, cancel);
                if (header != null)
                    headers.Add(header);
            }

            return headers;
        }

        public async Task<IReadOnlyList<PersistentInventoryStateCount>> GetPersistentInventoryStateCountsAsync(
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var counts = await db.DbContext.Wh40kPersistentInventories
                .AsNoTracking()
                .GroupBy(account => account.State)
                .Select(group => new
                {
                    State = group.Key,
                    Count = group.Count(),
                })
                .ToListAsync(cancel);

            return counts
                .Where(entry => Enum.IsDefined(typeof(PersistentInventorySnapshotState), entry.State))
                .Select(entry => new PersistentInventoryStateCount(
                    (PersistentInventorySnapshotState) entry.State,
                    entry.Count))
                .OrderBy(entry => entry.State)
                .ToList();
        }

        public async Task<PersistentInventoryServerEpochRecord?> GetPersistentInventoryServerEpochAsync(
            PersistentInventoryServerEpoch serverEpoch,
            CancellationToken cancel = default)
        {
            if (serverEpoch.Value == Guid.Empty)
                throw new ArgumentException("Server epoch не может быть пустым.", nameof(serverEpoch));

            await using var db = await GetDb(cancel);
            var epoch = await db.DbContext.Wh40kPersistentInventoryServerEpochs
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.ServerEpoch == serverEpoch.Value, cancel);
            return epoch == null
                ? null
                : new PersistentInventoryServerEpochRecord(
                    new PersistentInventoryServerEpoch(epoch.ServerEpoch),
                    NormalizeDatabaseTime(epoch.StartedAt),
                    NormalizeDatabaseTime(epoch.CleanShutdownAt));
        }

        public async Task BeginPersistentInventoryServerEpochAsync(
            PersistentInventoryServerEpoch serverEpoch,
            CancellationToken cancel = default)
        {
            if (serverEpoch.Value == Guid.Empty)
                throw new ArgumentException("Server epoch не может быть пустым.", nameof(serverEpoch));

            await using var db = await GetDb(cancel);
            if (await db.DbContext.Wh40kPersistentInventoryServerEpochs
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.ServerEpoch == serverEpoch.Value, cancel))
            {
                return;
            }

            db.DbContext.Wh40kPersistentInventoryServerEpochs.Add(
                new Wh40kPersistentInventoryServerEpoch
                {
                    ServerEpoch = serverEpoch.Value,
                    StartedAt = DateTime.UtcNow,
                });

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
            }
            catch (DbUpdateException)
            {
                await using var retryDb = await GetDb(cancel);
                if (await retryDb.DbContext.Wh40kPersistentInventoryServerEpochs
                        .AsNoTracking()
                        .AnyAsync(candidate => candidate.ServerEpoch == serverEpoch.Value, cancel))
                {
                    return;
                }

                throw;
            }
        }

        public async Task<bool> MarkPersistentInventoryServerEpochCleanAsync(
            PersistentInventoryServerEpoch serverEpoch,
            CancellationToken cancel = default)
        {
            if (serverEpoch.Value == Guid.Empty)
                throw new ArgumentException("Server epoch не может быть пустым.", nameof(serverEpoch));

            await using var db = await GetDb(cancel);
            var epoch = await db.DbContext.Wh40kPersistentInventoryServerEpochs
                .SingleOrDefaultAsync(candidate => candidate.ServerEpoch == serverEpoch.Value, cancel);
            if (epoch == null)
                return false;
            if (epoch.CleanShutdownAt != null)
                return true;

            epoch.CleanShutdownAt = DateTime.UtcNow;
            await db.DbContext.SaveChangesAsync(cancel);
            return true;
        }

        public async Task<PersistentInventoryMutationResult> StagePersistentInventoryAsync(
            NetUserId userId,
            PersistentInventoryStageRequest request,
            CancellationToken cancel = default)
        {
            ValidatePersistentInventoryStageRequest(request);

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var duplicate = await FindPersistentInventoryDuplicateAsync(
                db.DbContext,
                userId.UserId,
                request.OperationId,
                PersistentInventoryAuditAction.Staged,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(db.DbContext, duplicate, cancel);

            var account = await db.DbContext.Wh40kPersistentInventories
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            var oldState = PersistentInventorySnapshotState.None;

            if (account == null)
            {
                if (request.ExpectedRevision != PersistentInventoryRevision.None)
                {
                    return CreatePersistentInventoryFailure(
                        PersistentInventoryMutationStatus.RevisionMismatch,
                        null);
                }

                var now = DateTime.UtcNow;
                account = new Wh40kPersistentInventory
                {
                    UserId = userId.UserId,
                    State = (int) PersistentInventorySnapshotState.None,
                    VerifiedState = (int) PersistentInventorySnapshotState.None,
                    SavePhase = (int) PersistentInventorySavePhase.None,
                    Revision = 0,
                    OperationId = request.OperationId.Value,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.DbContext.Wh40kPersistentInventories.Add(account);
            }
            else
            {
                oldState = ParsePersistentInventoryState(account.State);
                if (account.Revision != request.ExpectedRevision.Value)
                {
                    return CreatePersistentInventoryFailure(
                        PersistentInventoryMutationStatus.RevisionMismatch,
                        await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
                }

                if (oldState == PersistentInventorySnapshotState.Staging)
                {
                    return CreatePersistentInventoryFailure(
                        PersistentInventoryMutationStatus.StagingConflict,
                        await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
                }

                if (!PersistentInventoryStateMachine.CanTransition(
                        oldState,
                        PersistentInventorySnapshotState.Staging))
                {
                    return CreatePersistentInventoryFailure(
                        PersistentInventoryMutationStatus.InvalidTransition,
                        await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
                }

                if (account.StagingSnapshotId is { } staleStaging &&
                    staleStaging != account.CurrentSnapshotId &&
                    staleStaging != account.LastKnownGoodSnapshotId)
                {
                    var staleRevision = await db.DbContext.Wh40kPersistentInventoryRevisions
                        .SingleOrDefaultAsync(candidate =>
                            candidate.UserId == userId.UserId &&
                            candidate.SnapshotId == staleStaging,
                            cancel);
                    if (staleRevision != null)
                        db.DbContext.Wh40kPersistentInventoryRevisions.Remove(staleRevision);
                }
            }

            var timestamp = DateTime.UtcNow;
            var revision = checked(account.Revision + 1);
            var candidate = new Wh40kPersistentInventoryRevision
            {
                SnapshotId = request.SnapshotId.Value,
                UserId = userId.UserId,
                SchemaVersion = request.SchemaVersion,
                PolicyId = request.PolicyId,
                CapturedRoleId = request.CapturedRoleId,
                CapturedProfileName = request.CapturedProfileName,
                Payload = request.Payload,
                PayloadSha256 = request.PayloadSha256,
                ItemCount = request.ItemCount,
                EntityCount = request.EntityCount,
                UncompressedBytes = request.UncompressedBytes,
                CompressedBytes = request.Payload.Length,
                OperationId = request.OperationId.Value,
                CreatedAt = timestamp,
                SavedAt = timestamp,
            };
            db.DbContext.Wh40kPersistentInventoryRevisions.Add(candidate);

            account.State = (int) PersistentInventorySnapshotState.Staging;
            account.SavePhase = (int) PersistentInventorySavePhase.CandidateStaged;
            account.Revision = revision;
            account.OperationId = request.OperationId.Value;
            account.StagingSnapshotId = request.SnapshotId.Value;
            account.StagingServerEpoch = request.ServerEpoch?.Value;
            account.WorldCleanupAuthorizedAt = null;
            account.UpdatedAt = timestamp;

            AddPersistentInventoryAudit(
                db.DbContext,
                account,
                request.OperationId,
                PersistentInventoryAuditAction.Staged,
                oldState,
                PersistentInventorySnapshotState.Staging,
                request.SnapshotId,
                request.Actor,
                request.ActorUserId,
                request.Reason,
                candidate,
                timestamp);

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.Staged,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.Staged,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }

            var header = await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel);
            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Success,
                header,
                new PersistentInventoryRevision(revision),
                PersistentInventorySnapshotState.Staging,
                request.SnapshotId);
        }

        public async Task<PersistentInventoryMutationResult> AuthorizePersistentInventoryWorldCleanupAsync(
            NetUserId userId,
            PersistentInventoryAuthorizeWorldCleanupRequest request,
            CancellationToken cancel = default)
        {
            ValidatePersistentInventoryMutationIdentity(
                request.OperationId,
                request.ExpectedRevision,
                request.Actor,
                request.Reason);
            if (request.SnapshotId.Value == Guid.Empty)
                throw new ArgumentException("Идентификатор снимка не может быть пустым.", nameof(request));
            if (request.ServerEpoch.Value == Guid.Empty)
                throw new ArgumentException("Server epoch не может быть пустым.", nameof(request));

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var duplicate = await FindPersistentInventoryDuplicateAsync(
                db.DbContext,
                userId.UserId,
                request.OperationId,
                PersistentInventoryAuditAction.WorldCleanupAuthorized,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(db.DbContext, duplicate, cancel);

            var account = await db.DbContext.Wh40kPersistentInventories
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (account == null)
                return CreatePersistentInventoryFailure(PersistentInventoryMutationStatus.NotFound, null);

            if (account.Revision != request.ExpectedRevision.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.RevisionMismatch,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            if (ParsePersistentInventoryState(account.State) != PersistentInventorySnapshotState.Staging ||
                ParsePersistentInventorySavePhase(account.SavePhase) != PersistentInventorySavePhase.CandidateStaged ||
                account.StagingSnapshotId != request.SnapshotId.Value ||
                account.OperationId != request.OperationId.Value ||
                account.StagingServerEpoch != request.ServerEpoch.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.CandidateNotFound,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var candidate = await db.DbContext.Wh40kPersistentInventoryRevisions
                .AsNoTracking()
                .SingleOrDefaultAsync(revision =>
                    revision.UserId == userId.UserId &&
                    revision.SnapshotId == request.SnapshotId.Value &&
                    revision.OperationId == request.OperationId.Value,
                    cancel);
            if (candidate == null)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.CandidateNotFound,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var timestamp = DateTime.UtcNow;
            var revisionNumber = checked(account.Revision + 1);
            account.SavePhase = (int) PersistentInventorySavePhase.WorldCleanupAuthorized;
            account.WorldCleanupAuthorizedAt = timestamp;
            account.Revision = revisionNumber;
            account.UpdatedAt = timestamp;

            AddPersistentInventoryAudit(
                db.DbContext,
                account,
                request.OperationId,
                PersistentInventoryAuditAction.WorldCleanupAuthorized,
                PersistentInventorySnapshotState.Staging,
                PersistentInventorySnapshotState.Staging,
                request.SnapshotId,
                request.Actor,
                request.ActorUserId,
                request.Reason,
                candidate,
                timestamp);

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.WorldCleanupAuthorized,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.WorldCleanupAuthorized,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }

            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Success,
                await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel),
                new PersistentInventoryRevision(revisionNumber),
                PersistentInventorySnapshotState.Staging,
                request.SnapshotId);
        }

        public async Task<PersistentInventoryMutationResult> PromotePersistentInventoryAsync(
            NetUserId userId,
            PersistentInventoryPromoteRequest request,
            CancellationToken cancel = default)
        {
            ValidatePersistentInventoryMutationIdentity(
                request.OperationId,
                request.ExpectedRevision,
                request.Actor,
                request.Reason);
            if (request.SnapshotId.Value == Guid.Empty)
                throw new ArgumentException("Идентификатор снимка не может быть пустым.", nameof(request));

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var duplicate = await FindPersistentInventoryDuplicateAsync(
                db.DbContext,
                userId.UserId,
                request.OperationId,
                PersistentInventoryAuditAction.Promoted,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(db.DbContext, duplicate, cancel);

            var account = await db.DbContext.Wh40kPersistentInventories
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (account == null)
                return CreatePersistentInventoryFailure(PersistentInventoryMutationStatus.NotFound, null);

            if (account.Revision != request.ExpectedRevision.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.RevisionMismatch,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var oldState = ParsePersistentInventoryState(account.State);
            if (oldState != PersistentInventorySnapshotState.Staging ||
                ParsePersistentInventorySavePhase(account.SavePhase) !=
                    PersistentInventorySavePhase.WorldCleanupAuthorized ||
                account.StagingSnapshotId != request.SnapshotId.Value ||
                account.OperationId != request.OperationId.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.CandidateNotFound,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var candidate = await db.DbContext.Wh40kPersistentInventoryRevisions
                .SingleOrDefaultAsync(revision =>
                    revision.UserId == userId.UserId &&
                    revision.SnapshotId == request.SnapshotId.Value,
                    cancel);
            if (candidate == null)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.CandidateNotFound,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var latestLostSnapshotId = await FindLatestPersistentInventoryLostSnapshotIdAsync(
                db.DbContext,
                userId.UserId,
                cancel);
            if (account.LastKnownGoodSnapshotId is { } oldLastKnownGood &&
                oldLastKnownGood != account.CurrentSnapshotId &&
                oldLastKnownGood != latestLostSnapshotId)
            {
                var retained = await db.DbContext.Wh40kPersistentInventoryRevisions
                    .SingleOrDefaultAsync(revision =>
                        revision.UserId == userId.UserId &&
                        revision.SnapshotId == oldLastKnownGood,
                        cancel);
                if (retained != null)
                    db.DbContext.Wh40kPersistentInventoryRevisions.Remove(retained);
            }

            var timestamp = DateTime.UtcNow;
            var revisionNumber = checked(account.Revision + 1);
            account.LastKnownGoodSnapshotId = account.CurrentSnapshotId;
            account.CurrentSnapshotId = request.SnapshotId.Value;
            account.StagingSnapshotId = null;
            account.State = (int) PersistentInventorySnapshotState.Active;
            account.VerifiedState = (int) PersistentInventorySnapshotState.Active;
            account.SavePhase = (int) PersistentInventorySavePhase.None;
            account.Revision = revisionNumber;
            account.OperationId = request.OperationId.Value;
            account.ServerEpoch = null;
            account.StagingServerEpoch = null;
            account.LifeId = null;
            account.InvalidationReason = (int) PersistentInventoryInvalidationReason.None;
            account.LossReason = (int) PersistentInventoryLossReason.None;
            account.QuarantineReason = (int) PersistentInventoryQuarantineReason.None;
            account.ReasonDetails = null;
            account.UpdatedAt = timestamp;
            account.RestoredAt = null;
            account.InvalidatedAt = null;
            account.LostAt = null;
            account.WorldCleanupAuthorizedAt = null;

            AddPersistentInventoryAudit(
                db.DbContext,
                account,
                request.OperationId,
                PersistentInventoryAuditAction.Promoted,
                oldState,
                PersistentInventorySnapshotState.Active,
                request.SnapshotId,
                request.Actor,
                request.ActorUserId,
                request.Reason,
                candidate,
                timestamp);

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.Promoted,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.Promoted,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }

            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Success,
                await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel),
                new PersistentInventoryRevision(revisionNumber),
                PersistentInventorySnapshotState.Active,
                request.SnapshotId);
        }

        public async Task<PersistentInventoryMutationResult> RepairPersistentInventoryAsync(
            NetUserId userId,
            PersistentInventoryRepairRequest request,
            CancellationToken cancel = default)
        {
            ValidatePersistentInventoryRepairRequest(request);

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var duplicate = await FindPersistentInventoryDuplicateAsync(
                db.DbContext,
                userId.UserId,
                request.OperationId,
                PersistentInventoryAuditAction.Repaired,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(db.DbContext, duplicate, cancel);

            var account = await db.DbContext.Wh40kPersistentInventories
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (account == null)
                return CreatePersistentInventoryFailure(PersistentInventoryMutationStatus.NotFound, null);

            if (account.Revision != request.ExpectedRevision.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.RevisionMismatch,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var oldState = ParsePersistentInventoryState(account.State);
            if (oldState != PersistentInventorySnapshotState.Active ||
                ParsePersistentInventoryState(account.VerifiedState) !=
                    PersistentInventorySnapshotState.Active ||
                ParsePersistentInventorySavePhase(account.SavePhase) !=
                    PersistentInventorySavePhase.None ||
                account.CurrentSnapshotId != request.SourceSnapshotId.Value ||
                account.StagingSnapshotId != null)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.InvalidTransition,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var source = await db.DbContext.Wh40kPersistentInventoryRevisions
                .SingleOrDefaultAsync(revision =>
                    revision.UserId == userId.UserId &&
                    revision.SnapshotId == request.SourceSnapshotId.Value,
                    cancel);
            if (source == null)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.CandidateNotFound,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            if (await db.DbContext.Wh40kPersistentInventoryRevisions
                    .AsNoTracking()
                    .AnyAsync(revision =>
                        revision.SnapshotId == request.RepairedSnapshotId.Value,
                        cancel))
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.StagingConflict,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var timestamp = DateTime.UtcNow;
            var revisionNumber = checked(account.Revision + 1);
            var repaired = new Wh40kPersistentInventoryRevision
            {
                SnapshotId = request.RepairedSnapshotId.Value,
                UserId = userId.UserId,
                SchemaVersion = request.SchemaVersion,
                PolicyId = request.PolicyId,
                CapturedRoleId = source.CapturedRoleId,
                CapturedProfileName = source.CapturedProfileName,
                Payload = request.Payload,
                PayloadSha256 = request.PayloadSha256,
                ItemCount = request.ItemCount,
                EntityCount = request.EntityCount,
                UncompressedBytes = request.UncompressedBytes,
                CompressedBytes = request.Payload.Length,
                OperationId = request.OperationId.Value,
                CreatedAt = timestamp,
                SavedAt = timestamp,
            };
            db.DbContext.Wh40kPersistentInventoryRevisions.Add(repaired);

            account.CurrentSnapshotId = request.RepairedSnapshotId.Value;
            if (account.LastKnownGoodSnapshotId == request.SourceSnapshotId.Value)
                account.LastKnownGoodSnapshotId = null;
            account.Revision = revisionNumber;
            account.OperationId = request.OperationId.Value;
            account.ReasonDetails = request.Reason;
            account.UpdatedAt = timestamp;

            AddPersistentInventoryAudit(
                db.DbContext,
                account,
                request.OperationId,
                PersistentInventoryAuditAction.Repaired,
                oldState,
                PersistentInventorySnapshotState.Active,
                request.RepairedSnapshotId,
                request.Actor,
                request.ActorUserId,
                request.Reason,
                repaired,
                timestamp);
            db.DbContext.Wh40kPersistentInventoryRevisions.Remove(source);

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.Repaired,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    PersistentInventoryAuditAction.Repaired,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }

            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Success,
                await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel),
                new PersistentInventoryRevision(revisionNumber),
                PersistentInventorySnapshotState.Active,
                request.RepairedSnapshotId);
        }

        public async Task<PersistentInventoryMutationResult> TransitionPersistentInventoryAsync(
            NetUserId userId,
            PersistentInventoryTransitionRequest request,
            CancellationToken cancel = default)
        {
            ValidatePersistentInventoryMutationIdentity(
                request.OperationId,
                request.ExpectedRevision,
                request.Actor,
                request.Reason);
            if (!IsPersistentInventoryTransitionAuditActionValid(request.NewState, request.AuditAction))
                throw new ArgumentException("Audit action не соответствует переходу состояния.", nameof(request));

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var duplicate = await FindPersistentInventoryDuplicateAsync(
                db.DbContext,
                userId.UserId,
                request.OperationId,
                request.AuditAction,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(db.DbContext, duplicate, cancel);

            var account = await db.DbContext.Wh40kPersistentInventories
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (account == null)
                return CreatePersistentInventoryFailure(PersistentInventoryMutationStatus.NotFound, null);

            if (account.Revision != request.ExpectedRevision.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.RevisionMismatch,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var oldState = ParsePersistentInventoryState(account.State);
            if (!PersistentInventoryStateMachine.CanTransition(oldState, request.NewState) ||
                !PersistentInventoryStateMachine.HasValidTransitionMetadata(request) ||
                oldState == PersistentInventorySnapshotState.Staging &&
                request.NewState == PersistentInventorySnapshotState.Active)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.InvalidTransition,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var timestamp = DateTime.UtcNow;
            var revisionNumber = checked(account.Revision + 1);
            Wh40kPersistentInventoryRevision? auditRevision = null;
            var auditSnapshotId = account.CurrentSnapshotId;
            var supersededLostSnapshotId =
                request.NewState == PersistentInventorySnapshotState.LostByDisconnect
                    ? await FindLatestPersistentInventoryLostSnapshotIdAsync(
                        db.DbContext,
                        userId.UserId,
                        cancel)
                    : null;

            if (request.NewState == PersistentInventorySnapshotState.Aborted &&
                account.StagingSnapshotId is { } stagingSnapshotId)
            {
                auditSnapshotId = stagingSnapshotId;
                auditRevision = await db.DbContext.Wh40kPersistentInventoryRevisions
                    .SingleOrDefaultAsync(revision =>
                        revision.UserId == userId.UserId &&
                        revision.SnapshotId == stagingSnapshotId,
                        cancel);
                if (auditRevision != null)
                    db.DbContext.Wh40kPersistentInventoryRevisions.Remove(auditRevision);
                account.StagingSnapshotId = null;
                account.StagingServerEpoch = null;
                account.SavePhase = (int) PersistentInventorySavePhase.None;
                account.WorldCleanupAuthorizedAt = null;
            }
            else
            {
                var metadataSnapshotId = request.NewState == PersistentInventorySnapshotState.Quarantined
                    ? account.StagingSnapshotId ?? account.CurrentSnapshotId
                    : account.CurrentSnapshotId;
                auditSnapshotId = metadataSnapshotId;
                if (metadataSnapshotId != null)
                {
                    auditRevision = await db.DbContext.Wh40kPersistentInventoryRevisions
                        .AsNoTracking()
                        .SingleOrDefaultAsync(revision =>
                            revision.UserId == userId.UserId &&
                            revision.SnapshotId == metadataSnapshotId,
                            cancel);
                }
            }

            var isCandidateAbort = oldState == PersistentInventorySnapshotState.Staging &&
                                   request.NewState == PersistentInventorySnapshotState.Aborted;
            account.State = isCandidateAbort
                ? account.VerifiedState
                : (int) request.NewState;
            account.Revision = revisionNumber;
            account.OperationId = request.OperationId.Value;
            account.ReasonDetails = request.Reason;
            account.UpdatedAt = timestamp;
            var preservesVerifiedState = oldState == PersistentInventorySnapshotState.Staging &&
                                         request.NewState is PersistentInventorySnapshotState.Aborted
                                             or PersistentInventorySnapshotState.Quarantined;
            if (preservesVerifiedState)
            {
                if (request.NewState == PersistentInventorySnapshotState.Quarantined)
                {
                    account.QuarantineReason = (int) request.QuarantineReason;
                    account.SavePhase = (int) PersistentInventorySavePhase.None;
                    account.StagingServerEpoch = null;
                    account.WorldCleanupAuthorizedAt = null;
                }
            }
            else
            {
                account.VerifiedState = (int) request.NewState;
                account.ServerEpoch = request.NewState == PersistentInventorySnapshotState.Bound
                    ? request.ServerEpoch?.Value
                    : null;
                account.LifeId = request.NewState == PersistentInventorySnapshotState.Bound
                    ? request.LifeId?.Value
                    : null;
                account.InvalidationReason = (int) request.InvalidationReason;
                account.LossReason = (int) request.LossReason;
                account.QuarantineReason = (int) request.QuarantineReason;
                account.RestoredAt = request.NewState == PersistentInventorySnapshotState.Bound
                    ? timestamp
                    : account.RestoredAt;
                account.InvalidatedAt = request.NewState == PersistentInventorySnapshotState.Invalid
                    ? timestamp
                    : null;
                account.LostAt = request.NewState == PersistentInventorySnapshotState.LostByDisconnect
                    ? timestamp
                    : null;
            }

            AddPersistentInventoryAudit(
                db.DbContext,
                account,
                request.OperationId,
                request.AuditAction,
                oldState,
                request.NewState,
                auditSnapshotId == null ? null : new PersistentInventorySnapshotId(auditSnapshotId.Value),
                request.Actor,
                request.ActorUserId,
                request.Reason,
                auditRevision,
                timestamp);

            if (supersededLostSnapshotId is { } oldLostSnapshotId &&
                oldLostSnapshotId != auditSnapshotId &&
                oldLostSnapshotId != account.CurrentSnapshotId &&
                oldLostSnapshotId != account.LastKnownGoodSnapshotId &&
                oldLostSnapshotId != account.StagingSnapshotId)
            {
                var supersededLostRevision = await db.DbContext.Wh40kPersistentInventoryRevisions
                    .SingleOrDefaultAsync(revision =>
                        revision.UserId == userId.UserId &&
                        revision.SnapshotId == oldLostSnapshotId,
                        cancel);
                if (supersededLostRevision != null)
                    db.DbContext.Wh40kPersistentInventoryRevisions.Remove(supersededLostRevision);
            }

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    request.AuditAction,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    request.AuditAction,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }

            var appliedState = ParsePersistentInventoryState(account.State);
            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Success,
                await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel),
                new PersistentInventoryRevision(revisionNumber),
                appliedState,
                auditSnapshotId == null ? null : new PersistentInventorySnapshotId(auditSnapshotId.Value));
        }

        public async Task<PersistentInventoryMutationResult> SelectPersistentInventoryRevisionAsync(
            NetUserId userId,
            PersistentInventorySelectRevisionRequest request,
            CancellationToken cancel = default)
        {
            ValidatePersistentInventoryMutationIdentity(
                request.OperationId,
                request.ExpectedRevision,
                request.Actor,
                request.Reason);
            if (request.SnapshotId.Value == Guid.Empty)
                throw new ArgumentException("Идентификатор снимка не может быть пустым.", nameof(request));
            if (!Enum.IsDefined(request.Mode))
                throw new ArgumentOutOfRangeException(nameof(request), "Неизвестный режим выбора ревизии.");

            var action = request.Mode == PersistentInventoryRevisionSelectionMode.RecoverLost
                ? PersistentInventoryAuditAction.Recovered
                : PersistentInventoryAuditAction.RolledBack;

            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var duplicate = await FindPersistentInventoryDuplicateAsync(
                db.DbContext,
                userId.UserId,
                request.OperationId,
                action,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(db.DbContext, duplicate, cancel);

            var account = await db.DbContext.Wh40kPersistentInventories
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId.UserId, cancel);
            if (account == null)
                return CreatePersistentInventoryFailure(PersistentInventoryMutationStatus.NotFound, null);
            if (account.Revision != request.ExpectedRevision.Value)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.RevisionMismatch,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var oldState = ParsePersistentInventoryState(account.State);
            var oldVerifiedState = ParsePersistentInventoryState(account.VerifiedState);
            var target = request.SnapshotId.Value;
            var isCurrent = account.CurrentSnapshotId == target;
            var isLastKnownGood = account.LastKnownGoodSnapshotId == target;
            var selected = await db.DbContext.Wh40kPersistentInventoryRevisions
                .SingleOrDefaultAsync(revision =>
                    revision.UserId == userId.UserId &&
                    revision.SnapshotId == target,
                    cancel);
            if (selected == null)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.CandidateNotFound,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var selectableState = oldState is not PersistentInventorySnapshotState.Bound
                                      and not PersistentInventorySnapshotState.Staging &&
                                  oldVerifiedState != PersistentInventorySnapshotState.Bound;
            var isVerifiedRollbackTarget =
                request.Mode != PersistentInventoryRevisionSelectionMode.Rollback ||
                await db.DbContext.Wh40kPersistentInventoryAudits
                    .AsNoTracking()
                    .AnyAsync(audit =>
                        audit.UserId == userId.UserId &&
                        audit.SnapshotId == target &&
                        (audit.Action == (int) PersistentInventoryAuditAction.Promoted ||
                         audit.Action == (int) PersistentInventoryAuditAction.Repaired),
                        cancel);
            var latestLostSnapshotId =
                request.Mode == PersistentInventoryRevisionSelectionMode.RecoverLost
                    ? await FindLatestPersistentInventoryLostSnapshotIdAsync(
                        db.DbContext,
                        userId.UserId,
                        cancel)
                    : null;
            var allowed = request.Mode switch
            {
                PersistentInventoryRevisionSelectionMode.Rollback =>
                    selectableState &&
                    isVerifiedRollbackTarget,
                PersistentInventoryRevisionSelectionMode.RecoverLost =>
                    selectableState &&
                    latestLostSnapshotId == target,
                PersistentInventoryRevisionSelectionMode.StartupFallback =>
                    oldState == PersistentInventorySnapshotState.Bound &&
                    isLastKnownGood,
                _ => false,
            };
            if (!allowed)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.InvalidTransition,
                    await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel));
            }

            var previousCurrent = account.CurrentSnapshotId;
            var abandonedStaging = account.StagingSnapshotId;
            account.CurrentSnapshotId = target;
            if (!isCurrent)
            {
                account.LastKnownGoodSnapshotId =
                    request.Mode == PersistentInventoryRevisionSelectionMode.StartupFallback
                        ? null
                        : previousCurrent;
            }

            var timestamp = DateTime.UtcNow;
            var revisionNumber = checked(account.Revision + 1);
            account.State = (int) PersistentInventorySnapshotState.Active;
            account.VerifiedState = (int) PersistentInventorySnapshotState.Active;
            account.SavePhase = (int) PersistentInventorySavePhase.None;
            account.Revision = revisionNumber;
            account.OperationId = request.OperationId.Value;
            account.StagingSnapshotId = null;
            account.ServerEpoch = null;
            account.StagingServerEpoch = null;
            account.LifeId = null;
            account.InvalidationReason = (int) PersistentInventoryInvalidationReason.None;
            account.LossReason = (int) PersistentInventoryLossReason.None;
            account.QuarantineReason = (int) PersistentInventoryQuarantineReason.None;
            account.ReasonDetails = request.Reason;
            account.UpdatedAt = timestamp;
            account.RestoredAt = null;
            account.InvalidatedAt = null;
            account.LostAt = null;
            account.WorldCleanupAuthorizedAt = null;

            if (abandonedStaging is { } abandonedSnapshotId &&
                abandonedSnapshotId != target &&
                abandonedSnapshotId != account.CurrentSnapshotId &&
                abandonedSnapshotId != account.LastKnownGoodSnapshotId)
            {
                var abandonedRevision = await db.DbContext.Wh40kPersistentInventoryRevisions
                    .SingleOrDefaultAsync(revision =>
                        revision.UserId == userId.UserId &&
                        revision.SnapshotId == abandonedSnapshotId,
                        cancel);
                if (abandonedRevision != null)
                    db.DbContext.Wh40kPersistentInventoryRevisions.Remove(abandonedRevision);
            }

            AddPersistentInventoryAudit(
                db.DbContext,
                account,
                request.OperationId,
                action,
                oldState,
                PersistentInventorySnapshotState.Active,
                request.SnapshotId,
                request.Actor,
                request.ActorUserId,
                request.Reason,
                selected,
                timestamp);

            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    action,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancel);
                return await ResolvePersistentInventoryWriteFailureAsync(
                    userId.UserId,
                    request.OperationId,
                    action,
                    request.ExpectedRevision,
                    exception,
                    cancel);
            }

            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Success,
                await LoadPersistentInventoryHeaderAsync(db.DbContext, userId.UserId, cancel),
                new PersistentInventoryRevision(revisionNumber),
                PersistentInventorySnapshotState.Active,
                request.SnapshotId);
        }

        private async Task<PersistentInventorySnapshotHeader?> LoadPersistentInventoryHeaderAsync(
            ServerDbContext db,
            Guid userId,
            CancellationToken cancel)
        {
            var account = await db.Wh40kPersistentInventories
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancel);
            if (account == null)
                return null;

            var snapshotIds = new[]
                {
                    account.CurrentSnapshotId,
                    account.LastKnownGoodSnapshotId,
                    account.StagingSnapshotId,
                }
                .Where(snapshotId => snapshotId != null)
                .Select(snapshotId => snapshotId!.Value)
                .Distinct()
                .ToArray();
            var revisions = snapshotIds.Length == 0
                ? new Dictionary<Guid, Wh40kPersistentInventoryRevision>()
                : await db.Wh40kPersistentInventoryRevisions
                    .AsNoTracking()
                    .Where(revision =>
                        revision.UserId == userId &&
                        snapshotIds.Contains(revision.SnapshotId))
                    .ToDictionaryAsync(revision => revision.SnapshotId, cancel);

            PersistentInventoryRevisionMetadata? Metadata(Guid? snapshotId)
            {
                if (snapshotId == null || !revisions.TryGetValue(snapshotId.Value, out var revision))
                    return null;

                return ToPersistentInventoryRevisionMetadata(revision);
            }

            return new PersistentInventorySnapshotHeader(
                new PersistentInventoryAccountId(account.UserId),
                ParsePersistentInventoryState(account.State),
                ParsePersistentInventoryState(account.VerifiedState),
                ParsePersistentInventorySavePhase(account.SavePhase),
                new PersistentInventoryRevision(account.Revision),
                new PersistentInventoryOperationId(account.OperationId),
                Metadata(account.CurrentSnapshotId),
                Metadata(account.LastKnownGoodSnapshotId),
                Metadata(account.StagingSnapshotId),
                account.ServerEpoch == null
                    ? null
                    : new PersistentInventoryServerEpoch(account.ServerEpoch.Value),
                account.StagingServerEpoch == null
                    ? null
                    : new PersistentInventoryServerEpoch(account.StagingServerEpoch.Value),
                account.LifeId == null
                    ? null
                    : new PersistentInventoryLifeId(account.LifeId.Value),
                ParsePersistentInventoryInvalidationReason(account.InvalidationReason),
                ParsePersistentInventoryLossReason(account.LossReason),
                ParsePersistentInventoryQuarantineReason(account.QuarantineReason),
                account.ReasonDetails,
                NormalizeDatabaseTime(account.CreatedAt),
                NormalizeDatabaseTime(account.UpdatedAt),
                NormalizeDatabaseTime(account.RestoredAt),
                NormalizeDatabaseTime(account.InvalidatedAt),
                NormalizeDatabaseTime(account.LostAt),
                NormalizeDatabaseTime(account.WorldCleanupAuthorizedAt));
        }

        private static async Task<Guid?> FindLatestPersistentInventoryLostSnapshotIdAsync(
            ServerDbContext db,
            Guid userId,
            CancellationToken cancel)
        {
            var lostAudits = db.Wh40kPersistentInventoryAudits
                .AsNoTracking()
                .Where(audit =>
                    audit.UserId == userId &&
                    audit.Action == (int) PersistentInventoryAuditAction.Lost &&
                    audit.OldState == (int) PersistentInventorySnapshotState.Bound &&
                    audit.NewState == (int) PersistentInventorySnapshotState.LostByDisconnect &&
                    audit.SnapshotId != null);

            return await (
                    from audit in lostAudits
                    join revision in db.Wh40kPersistentInventoryRevisions.AsNoTracking()
                        on new
                        {
                            audit.UserId,
                            SnapshotId = audit.SnapshotId!.Value,
                        }
                        equals new
                        {
                            revision.UserId,
                            revision.SnapshotId,
                        }
                    orderby audit.Id descending
                    select audit.SnapshotId)
                .FirstOrDefaultAsync(cancel);
        }

        private async Task<PersistentInventoryMutationResult> ResolvePersistentInventoryWriteFailureAsync(
            Guid userId,
            PersistentInventoryOperationId operationId,
            PersistentInventoryAuditAction action,
            PersistentInventoryRevision expectedRevision,
            Exception exception,
            CancellationToken cancel)
        {
            await using var retryDb = await GetDb(cancel);
            var duplicate = await FindPersistentInventoryDuplicateAsync(
                retryDb.DbContext,
                userId,
                operationId,
                action,
                cancel);
            if (duplicate != null)
                return await ToDuplicatePersistentInventoryResultAsync(retryDb.DbContext, duplicate, cancel);

            var header = await LoadPersistentInventoryHeaderAsync(retryDb.DbContext, userId, cancel);
            if (header != null && header.Revision != expectedRevision)
            {
                return CreatePersistentInventoryFailure(
                    PersistentInventoryMutationStatus.RevisionMismatch,
                    header);
            }

            throw new InvalidOperationException(
                $"Не удалось сохранить persistent inventory для аккаунта {userId}.",
                exception);
        }

        private async Task<PersistentInventoryMutationResult> ToDuplicatePersistentInventoryResultAsync(
            ServerDbContext db,
            Wh40kPersistentInventoryAudit duplicate,
            CancellationToken cancel)
        {
            var header = await LoadPersistentInventoryHeaderAsync(db, duplicate.UserId, cancel);
            return new PersistentInventoryMutationResult(
                PersistentInventoryMutationStatus.Duplicate,
                header,
                new PersistentInventoryRevision(duplicate.Revision),
                header?.State ?? ParsePersistentInventoryState(duplicate.NewState),
                duplicate.SnapshotId == null
                    ? null
                    : new PersistentInventorySnapshotId(duplicate.SnapshotId.Value));
        }

        private static async Task<Wh40kPersistentInventoryAudit?> FindPersistentInventoryDuplicateAsync(
            ServerDbContext db,
            Guid userId,
            PersistentInventoryOperationId operationId,
            PersistentInventoryAuditAction action,
            CancellationToken cancel)
        {
            return await db.Wh40kPersistentInventoryAudits
                .AsNoTracking()
                .SingleOrDefaultAsync(entry =>
                    entry.UserId == userId &&
                    entry.OperationId == operationId.Value &&
                    entry.Action == (int) action,
                    cancel);
        }

        private static void AddPersistentInventoryAudit(
            ServerDbContext db,
            Wh40kPersistentInventory account,
            PersistentInventoryOperationId operationId,
            PersistentInventoryAuditAction action,
            PersistentInventorySnapshotState oldState,
            PersistentInventorySnapshotState newState,
            PersistentInventorySnapshotId? snapshotId,
            string actor,
            Guid? actorUserId,
            string? reason,
            Wh40kPersistentInventoryRevision? metadata,
            DateTime timestamp)
        {
            db.Wh40kPersistentInventoryAudits.Add(new Wh40kPersistentInventoryAudit
            {
                UserId = account.UserId,
                OperationId = operationId.Value,
                Action = (int) action,
                OldState = (int) oldState,
                NewState = (int) newState,
                Revision = account.Revision,
                SnapshotId = snapshotId?.Value,
                ActorUserId = actorUserId,
                Actor = actor,
                Reason = reason,
                ItemCount = metadata?.ItemCount ?? 0,
                EntityCount = metadata?.EntityCount ?? 0,
                UncompressedBytes = metadata?.UncompressedBytes ?? 0,
                CompressedBytes = metadata?.CompressedBytes ?? 0,
                CreatedAt = timestamp,
            });
        }

        private static PersistentInventoryMutationResult CreatePersistentInventoryFailure(
            PersistentInventoryMutationStatus status,
            PersistentInventorySnapshotHeader? header)
        {
            return new PersistentInventoryMutationResult(
                status,
                header,
                header?.Revision ?? PersistentInventoryRevision.None,
                header?.State ?? PersistentInventorySnapshotState.None,
                header?.Staging?.SnapshotId ?? header?.CurrentVerified?.SnapshotId);
        }

        private static void ValidatePersistentInventoryStageRequest(PersistentInventoryStageRequest request)
        {
            ValidatePersistentInventoryMutationIdentity(
                request.OperationId,
                request.ExpectedRevision,
                request.Actor,
                request.Reason);

            if (request.SnapshotId.Value == Guid.Empty)
                throw new ArgumentException("Идентификатор снимка не может быть пустым.", nameof(request));
            if (request.CapturedRoleId?.Length > PersistentInventoryMaximumPolicyIdLength ||
                request.CapturedProfileName?.Length > PersistentInventoryMaximumPolicyIdLength)
            {
                throw new ArgumentException(
                    $"Диагностические role/profile поля не могут превышать {PersistentInventoryMaximumPolicyIdLength} символов.",
                    nameof(request));
            }

            ValidatePersistentInventoryPayloadMetadata(
                request.SchemaVersion,
                request.PolicyId,
                request.Payload,
                request.PayloadSha256,
                request.ItemCount,
                request.EntityCount,
                request.UncompressedBytes,
                nameof(request));
        }

        private static void ValidatePersistentInventoryRepairRequest(PersistentInventoryRepairRequest request)
        {
            ValidatePersistentInventoryMutationIdentity(
                request.OperationId,
                request.ExpectedRevision,
                request.Actor,
                request.Reason);

            if (request.SourceSnapshotId.Value == Guid.Empty ||
                request.RepairedSnapshotId.Value == Guid.Empty ||
                request.SourceSnapshotId == request.RepairedSnapshotId)
            {
                throw new ArgumentException(
                    "Исходный и исправленный снимки должны иметь разные непустые идентификаторы.",
                    nameof(request));
            }

            ValidatePersistentInventoryPayloadMetadata(
                request.SchemaVersion,
                request.PolicyId,
                request.Payload,
                request.PayloadSha256,
                request.ItemCount,
                request.EntityCount,
                request.UncompressedBytes,
                nameof(request));
        }

        private static void ValidatePersistentInventoryPayloadMetadata(
            int schemaVersion,
            string policyId,
            byte[] payload,
            byte[] payloadSha256,
            int itemCount,
            int entityCount,
            int uncompressedBytes,
            string parameterName)
        {
            if (schemaVersion <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "Версия схемы должна быть положительной.");
            if (string.IsNullOrWhiteSpace(policyId) ||
                policyId.Length > PersistentInventoryMaximumPolicyIdLength)
            {
                throw new ArgumentException(
                    $"Идентификатор policy должен содержать от 1 до {PersistentInventoryMaximumPolicyIdLength} символов.",
                    parameterName);
            }

            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(payloadSha256);
            if (payloadSha256.Length != 32)
                throw new ArgumentException("SHA-256 должен содержать ровно 32 байта.", parameterName);
            if (itemCount < 0 || entityCount < 0 || uncompressedBytes < 0)
                throw new ArgumentOutOfRangeException(parameterName, "Размеры и количества не могут быть отрицательными.");
        }

        private static void ValidatePersistentInventoryMutationIdentity(
            PersistentInventoryOperationId operationId,
            PersistentInventoryRevision expectedRevision,
            string actor,
            string? reason)
        {
            if (operationId.Value == Guid.Empty)
                throw new ArgumentException("Идентификатор операции не может быть пустым.", nameof(operationId));
            if (expectedRevision.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedRevision), "Ожидаемая ревизия не может быть отрицательной.");
            if (string.IsNullOrWhiteSpace(actor) || actor.Length > PersistentInventoryMaximumAuditActorLength)
            {
                throw new ArgumentException(
                    $"Инициатор должен содержать от 1 до {PersistentInventoryMaximumAuditActorLength} символов.",
                    nameof(actor));
            }

            if (reason?.Length > PersistentInventoryMaximumAuditReasonLength)
            {
                throw new ArgumentException(
                    $"Причина не может превышать {PersistentInventoryMaximumAuditReasonLength} символов.",
                    nameof(reason));
            }
        }

        private static bool IsPersistentInventoryTransitionAuditActionValid(
            PersistentInventorySnapshotState state,
            PersistentInventoryAuditAction action)
        {
            if (action == PersistentInventoryAuditAction.StateChanged)
                return true;

            return (state, action) switch
            {
                (PersistentInventorySnapshotState.Invalid, PersistentInventoryAuditAction.Invalidated) => true,
                (PersistentInventorySnapshotState.LostByDisconnect, PersistentInventoryAuditAction.Lost) => true,
                (PersistentInventorySnapshotState.Quarantined, PersistentInventoryAuditAction.Quarantined) => true,
                (PersistentInventorySnapshotState.Active, PersistentInventoryAuditAction.Recovered) => true,
                _ => false,
            };
        }

        private PersistentInventoryRevisionMetadata ToPersistentInventoryRevisionMetadata(
            Wh40kPersistentInventoryRevision revision)
        {
            return new PersistentInventoryRevisionMetadata(
                new PersistentInventorySnapshotId(revision.SnapshotId),
                revision.SchemaVersion,
                revision.PolicyId,
                revision.CapturedRoleId,
                revision.CapturedProfileName,
                revision.PayloadSha256,
                revision.ItemCount,
                revision.EntityCount,
                revision.UncompressedBytes,
                revision.CompressedBytes,
                new PersistentInventoryOperationId(revision.OperationId),
                NormalizeDatabaseTime(revision.CreatedAt),
                NormalizeDatabaseTime(revision.SavedAt));
        }

        private PersistentInventoryAuditRecord ToPersistentInventoryAuditRecord(
            Wh40kPersistentInventoryAudit audit)
        {
            return new PersistentInventoryAuditRecord(
                audit.Id,
                new PersistentInventoryAccountId(audit.UserId),
                new PersistentInventoryOperationId(audit.OperationId),
                ParsePersistentInventoryAuditAction(audit.Action),
                ParsePersistentInventoryState(audit.OldState),
                ParsePersistentInventoryState(audit.NewState),
                new PersistentInventoryRevision(audit.Revision),
                audit.SnapshotId == null
                    ? null
                    : new PersistentInventorySnapshotId(audit.SnapshotId.Value),
                audit.ActorUserId,
                audit.Actor,
                audit.Reason,
                audit.ItemCount,
                audit.EntityCount,
                audit.UncompressedBytes,
                audit.CompressedBytes,
                NormalizeDatabaseTime(audit.CreatedAt));
        }

        private static PersistentInventorySnapshotState ParsePersistentInventoryState(int value)
        {
            return Enum.IsDefined(typeof(PersistentInventorySnapshotState), value)
                ? (PersistentInventorySnapshotState) value
                : throw new InvalidOperationException($"Неизвестное состояние persistent inventory: {value}.");
        }

        private static PersistentInventorySavePhase ParsePersistentInventorySavePhase(int value)
        {
            return Enum.IsDefined(typeof(PersistentInventorySavePhase), value)
                ? (PersistentInventorySavePhase) value
                : throw new InvalidOperationException($"Неизвестная фаза save-saga persistent inventory: {value}.");
        }

        private static PersistentInventoryInvalidationReason ParsePersistentInventoryInvalidationReason(int value)
        {
            return Enum.IsDefined(typeof(PersistentInventoryInvalidationReason), value)
                ? (PersistentInventoryInvalidationReason) value
                : throw new InvalidOperationException($"Неизвестная причина invalidation: {value}.");
        }

        private static PersistentInventoryLossReason ParsePersistentInventoryLossReason(int value)
        {
            return Enum.IsDefined(typeof(PersistentInventoryLossReason), value)
                ? (PersistentInventoryLossReason) value
                : throw new InvalidOperationException($"Неизвестная причина утраты: {value}.");
        }

        private static PersistentInventoryQuarantineReason ParsePersistentInventoryQuarantineReason(int value)
        {
            return Enum.IsDefined(typeof(PersistentInventoryQuarantineReason), value)
                ? (PersistentInventoryQuarantineReason) value
                : throw new InvalidOperationException($"Неизвестная причина карантина: {value}.");
        }

        private static PersistentInventoryAuditAction ParsePersistentInventoryAuditAction(int value)
        {
            return Enum.IsDefined(typeof(PersistentInventoryAuditAction), value)
                ? (PersistentInventoryAuditAction) value
                : throw new InvalidOperationException($"Неизвестное действие аудита: {value}.");
        }

        #endregion

        #region User Ids
        public async Task<NetUserId?> GetAssignedUserIdAsync(string name)
        {
            await using var db = await GetDb();

            var assigned = await db.DbContext.AssignedUserId.SingleOrDefaultAsync(p => p.UserName == name);
            return assigned?.UserId is { } g ? new NetUserId(g) : default(NetUserId?);
        }

        public async Task AssignUserIdAsync(string name, NetUserId netUserId)
        {
            await using var db = await GetDb();

            db.DbContext.AssignedUserId.Add(new AssignedUserId
            {
                UserId = netUserId.UserId,
                UserName = name
            });

            await db.DbContext.SaveChangesAsync();
        }
        #endregion

        #region MonoCoins

        public async Task<long> GetMonoCoinsAsync(NetUserId userId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            return prefs?.MonoCoins ?? 0l;
        }

        public async Task SetMonoCoinsAsync(NetUserId userId, long balance, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            if (prefs != null)
            {
                prefs.MonoCoins = Math.Max(0l, balance); // Ensure balance is never negative
                await db.DbContext.SaveChangesAsync(cancel);
            }
        }

        public async Task<long> AddMonoCoinsAsync(NetUserId userId, long amount, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            if (prefs != null)
            {
                prefs.MonoCoins += amount;
                prefs.MonoCoins = Math.Max(0l, prefs.MonoCoins); // Ensure balance is never negative
                await db.DbContext.SaveChangesAsync(cancel);
                return prefs.MonoCoins;
            }

            return 0;
        }

        #endregion

        #region Bans
        /*
         * BAN STUFF
         */
        /// <summary>
        ///     Looks up a ban by id.
        ///     This will return a pardoned ban as well.
        /// </summary>
        /// <param name="id">The ban id to look for.</param>
        /// <returns>The ban with the given id or null if none exist.</returns>
        public abstract Task<ServerBanDef?> GetServerBanAsync(int id);

        /// <summary>
        ///     Looks up an user's most recent received un-pardoned ban.
        ///     This will NOT return a pardoned ban.
        ///     One of <see cref="address"/> or <see cref="userId"/> need to not be null.
        /// </summary>
        /// <param name="address">The ip address of the user.</param>
        /// <param name="userId">The id of the user.</param>
        /// <param name="hwId">The legacy HWId of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <returns>The user's latest received un-pardoned ban, or null if none exist.</returns>
        public abstract Task<ServerBanDef?> GetServerBanAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds);

        /// <summary>
        ///     Looks up an user's ban history.
        ///     This will return pardoned bans as well.
        ///     One of <see cref="address"/> or <see cref="userId"/> need to not be null.
        /// </summary>
        /// <param name="address">The ip address of the user.</param>
        /// <param name="userId">The id of the user.</param>
        /// <param name="hwId">The legacy HWId of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <param name="includeUnbanned">Include pardoned and expired bans.</param>
        /// <returns>The user's ban history.</returns>
        public abstract Task<List<ServerBanDef>> GetServerBansAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds,
            bool includeUnbanned);

        public abstract Task AddServerBanAsync(ServerBanDef serverBan);
        public abstract Task AddServerUnbanAsync(ServerUnbanDef serverUnban);

        public virtual async Task<List<WH40KMuteDef>> GetActiveMutesAsync(NetUserId userId)
        {
            await using var db = await GetDb();
            var now = DateTime.UtcNow;
            var mutes = await ActiveMuteQuery(db.DbContext, userId, now)
                .AsNoTracking()
                .OrderByDescending(m => m.MuteTime)
                .ThenByDescending(m => m.Id)
                .ToListAsync();

            return mutes.Select(ConvertMute).ToList();
        }

        public virtual async Task<WH40KMuteHistoryPage> GetMuteHistoryAsync(
            NetUserId userId,
            int offset,
            int limit)
        {
            const int maximumPageSize = 100;
            offset = Math.Max(0, offset);
            limit = Math.Clamp(limit, 1, maximumPageSize);

            await using var db = await GetDb();
            var mutes = await WH40KMuteQuery(db.DbContext)
                .AsNoTracking()
                .Where(m => m.PlayerUserId == userId.UserId)
                .OrderByDescending(m => m.MuteTime)
                .ThenByDescending(m => m.Id)
                .Skip(offset)
                .Take(limit + 1)
                .ToListAsync();

            var hasNextPage = mutes.Count > limit;
            if (hasNextPage)
                mutes.RemoveAt(mutes.Count - 1);

            return new WH40KMuteHistoryPage(mutes.Select(ConvertMute).ToList(), hasNextPage);
        }

        public virtual async Task<WH40KMuteReplacementResult> ReplaceMutesAsync(
            NetUserId userId,
            IReadOnlyCollection<WH40KMuteType> types,
            string reason,
            NetUserId? mutingAdmin,
            DateTimeOffset muteTime,
            DateTimeOffset? expirationTime)
        {
            var scopes = NormalizeMuteScopes(types);
            await using var db = await GetDb();
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync();

            var activeMutes = await ActiveMuteQuery(db.DbContext, userId, muteTime.UtcDateTime, scopes)
                .ToListAsync();
            foreach (var activeMute in activeMutes)
            {
                db.DbContext.WH40KUnmute.Add(new WH40KUnmute
                {
                    MuteId = activeMute.Id,
                    UnmutingAdminId = mutingAdmin?.UserId,
                    UnmuteTime = muteTime.UtcDateTime,
                });
            }

            foreach (var scope in scopes)
            {
                db.DbContext.WH40KMute.Add(new WH40KMute
                {
                    PlayerUserId = userId.UserId,
                    Type = scope,
                    Reason = reason,
                    CreatedById = mutingAdmin?.UserId,
                    MuteTime = muteTime.UtcDateTime,
                    ExpirationTime = expirationTime?.UtcDateTime,
                });
            }

            await db.DbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return new WH40KMuteReplacementResult(activeMutes.Count, scopes.Length);
        }

        public virtual async Task<int> RemoveActiveMutesAsync(
            NetUserId userId,
            IReadOnlyCollection<WH40KMuteType> types,
            NetUserId? unmutingAdmin,
            DateTimeOffset unmuteTime)
        {
            var scopes = NormalizeMuteScopes(types);
            await using var db = await GetDb();
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync();

            var activeMutes = await ActiveMuteQuery(db.DbContext, userId, unmuteTime.UtcDateTime, scopes)
                .ToListAsync();
            foreach (var activeMute in activeMutes)
            {
                db.DbContext.WH40KUnmute.Add(new WH40KUnmute
                {
                    MuteId = activeMute.Id,
                    UnmutingAdminId = unmutingAdmin?.UserId,
                    UnmuteTime = unmuteTime.UtcDateTime,
                });
            }

            await db.DbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return activeMutes.Count;
        }

        public async Task EditServerBan(int id, string reason, NoteSeverity severity, DateTimeOffset? expiration, Guid editedBy, DateTimeOffset editedAt)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban.SingleOrDefaultAsync(b => b.Id == id);
            if (ban is null)
                return;
            ban.Severity = severity;
            ban.Reason = reason;
            ban.ExpirationTime = expiration?.UtcDateTime;
            ban.LastEditedById = editedBy;
            ban.LastEditedAt = editedAt.UtcDateTime;
            await db.DbContext.SaveChangesAsync();
        }

        protected static async Task<ServerBanExemptFlags?> GetBanExemptionCore(
            DbGuard db,
            NetUserId? userId,
            CancellationToken cancel = default)
        {
            if (userId == null)
                return null;

            var exemption = await db.DbContext.BanExemption
                .SingleOrDefaultAsync(e => e.UserId == userId.Value.UserId, cancellationToken: cancel);

            return exemption?.Flags;
        }

        private static IQueryable<WH40KMute> WH40KMuteQuery(ServerDbContext dbContext)
        {
            return dbContext.WH40KMute
                .Include(m => m.Unmute);
        }

        private static IQueryable<WH40KMute> ActiveMuteQuery(
            ServerDbContext dbContext,
            NetUserId userId,
            DateTime now,
            IReadOnlyCollection<int>? scopes = null)
        {
            var query = WH40KMuteQuery(dbContext)
                .Where(m =>
                    m.PlayerUserId == userId.UserId &&
                    m.Unmute == null &&
                    (m.ExpirationTime == null || m.ExpirationTime > now));

            return scopes == null ? query : query.Where(m => scopes.Contains(m.Type));
        }

        private static int[] NormalizeMuteScopes(IReadOnlyCollection<WH40KMuteType> types)
        {
            var scopes = types
                .Select(type => (int) type)
                .Distinct()
                .ToArray();
            if (scopes.Length == 0)
                throw new ArgumentException("At least one mute scope is required.", nameof(types));

            return scopes;
        }

        private WH40KMuteDef ConvertMute(WH40KMute mute)
        {
            NetUserId? admin = mute.CreatedById == null ? null : new NetUserId(mute.CreatedById.Value);
            var unmute = mute.Unmute == null
                ? null
                : new WH40KUnmuteDef(
                    mute.Id,
                    mute.Unmute.UnmutingAdminId == null
                        ? null
                        : new NetUserId(mute.Unmute.UnmutingAdminId.Value),
                    new DateTimeOffset(NormalizeDatabaseTime(mute.Unmute.UnmuteTime)));

            return new WH40KMuteDef(
                mute.Id,
                new NetUserId(mute.PlayerUserId),
                (WH40KMuteType) mute.Type,
                mute.Reason,
                admin,
                new DateTimeOffset(NormalizeDatabaseTime(mute.MuteTime)),
                NormalizeDatabaseTime(mute.ExpirationTime) is { } expirationTime
                    ? new DateTimeOffset(expirationTime)
                    : null,
                unmute);
        }

        public async Task UpdateBanExemption(NetUserId userId, ServerBanExemptFlags flags)
        {
            await using var db = await GetDb();

            if (flags == 0)
            {
                // Delete whatever is there.
                await db.DbContext.BanExemption.Where(u => u.UserId == userId.UserId).ExecuteDeleteAsync();
                return;
            }

            var exemption = await db.DbContext.BanExemption.SingleOrDefaultAsync(u => u.UserId == userId.UserId);
            if (exemption == null)
            {
                exemption = new ServerBanExemption
                {
                    UserId = userId
                };

                db.DbContext.BanExemption.Add(exemption);
            }

            exemption.Flags = flags;
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<ServerBanExemptFlags> GetBanExemption(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var flags = await GetBanExemptionCore(db, userId, cancel);
            return flags ?? ServerBanExemptFlags.None;
        }

        #endregion

        #region Role Bans
        /*
         * ROLE BANS
         */
        /// <summary>
        ///     Looks up a role ban by id.
        ///     This will return a pardoned role ban as well.
        /// </summary>
        /// <param name="id">The role ban id to look for.</param>
        /// <returns>The role ban with the given id or null if none exist.</returns>
        public abstract Task<ServerRoleBanDef?> GetServerRoleBanAsync(int id);

        /// <summary>
        ///     Looks up an user's role ban history.
        ///     This will return pardoned role bans based on the <see cref="includeUnbanned"/> bool.
        ///     Requires one of <see cref="address"/>, <see cref="userId"/>, or <see cref="hwId"/> to not be null.
        /// </summary>
        /// <param name="address">The IP address of the user.</param>
        /// <param name="userId">The NetUserId of the user.</param>
        /// <param name="hwId">The Hardware Id of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <param name="includeUnbanned">Whether expired and pardoned bans are included.</param>
        /// <returns>The user's role ban history.</returns>
        public abstract Task<List<ServerRoleBanDef>> GetServerRoleBansAsync(IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds,
            bool includeUnbanned);

        public abstract Task<ServerRoleBanDef> AddServerRoleBanAsync(ServerRoleBanDef serverRoleBan);
        public abstract Task AddServerRoleUnbanAsync(ServerRoleUnbanDef serverRoleUnban);

        public async Task EditServerRoleBan(int id, string reason, NoteSeverity severity, DateTimeOffset? expiration, Guid editedBy, DateTimeOffset editedAt)
        {
            await using var db = await GetDb();
            var roleBanDetails = await db.DbContext.RoleBan
                .Where(b => b.Id == id)
                .Select(b => new { b.BanTime, b.PlayerUserId })
                .SingleOrDefaultAsync();

            if (roleBanDetails == default)
                return;

            await db.DbContext.RoleBan
                .Where(b => b.BanTime == roleBanDetails.BanTime && b.PlayerUserId == roleBanDetails.PlayerUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.Severity, severity)
                    .SetProperty(b => b.Reason, reason)
                    .SetProperty(b => b.ExpirationTime, expiration.HasValue ? expiration.Value.UtcDateTime : (DateTime?)null)
                    .SetProperty(b => b.LastEditedById, editedBy)
                    .SetProperty(b => b.LastEditedAt, editedAt.UtcDateTime)
                );
        }
        #endregion

        #region Playtime
        public async Task<List<PlayTime>> GetPlayTimes(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.PlayTime
                .Where(p => p.PlayerId == player)
                .ToListAsync(cancel);
        }

        public async Task UpdatePlayTimes(IReadOnlyCollection<PlayTimeUpdate> updates)
        {
            await using var db = await GetDb();

            // Ideally I would just be able to send a bunch of UPSERT commands, but EFCore is a pile of garbage.
            // So... In the interest of not making this take forever at high update counts...
            // Bulk-load play time objects for all players involved.
            // This allows us to semi-efficiently load all entities we need in a single DB query.
            // Then we can update & insert without further round-trips to the DB.

            var players = updates.Select(u => u.User.UserId).Distinct().ToList();
            var dbTimes = (await db.DbContext.PlayTime
                    .Where(p => players.Contains(p.PlayerId))
                    .ToArrayAsync())
                .GroupBy(p => p.PlayerId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Tracker, p => p));

            foreach (var (user, tracker, time) in updates)
            {
                if (dbTimes.TryGetValue(user.UserId, out var userTimes)
                    && userTimes.TryGetValue(tracker, out var ent))
                {
                    // Already have a tracker in the database, update it.
                    ent.TimeSpent = time;
                    continue;
                }

                // No tracker, make a new one.
                var playTime = new PlayTime
                {
                    Tracker = tracker,
                    PlayerId = user.UserId,
                    TimeSpent = time
                };

                db.DbContext.PlayTime.Add(playTime);
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Player Records
        /*
         * PLAYER RECORDS
         */
        public async Task UpdatePlayerRecord(
            NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableTypedHwid? hwId)
        {
            await using var db = await GetDb();

            var record = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == userId.UserId);
            if (record == null)
            {
                db.DbContext.Player.Add(record = new Player
                {
                    FirstSeenTime = DateTime.UtcNow,
                    UserId = userId.UserId,
                });
            }

            record.LastSeenTime = DateTime.UtcNow;
            record.LastSeenAddress = address;
            record.LastSeenUserName = userName;
            record.LastSeenHWId = hwId;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<PlayerRecord?> GetPlayerRecordByUserName(string userName, CancellationToken cancel)
        {
            await using var db = await GetDb();

            // Sort by descending last seen time.
            // So if, due to account renames, we have two people with the same username in the DB,
            // the most recent one is picked.
            var record = await db.DbContext.Player
                .OrderByDescending(p => p.LastSeenTime)
                .FirstOrDefaultAsync(p => p.LastSeenUserName == userName, cancel);

            return record == null ? null : MakePlayerRecord(record);
        }

        public async Task<PlayerRecord?> GetPlayerRecordByUserId(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb();

            var record = await db.DbContext.Player
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            return record == null ? null : MakePlayerRecord(record);
        }

        protected async Task<bool> PlayerRecordExists(DbGuard db, NetUserId userId)
        {
            return await db.DbContext.Player.AnyAsync(p => p.UserId == userId);
        }

        [return: NotNullIfNotNull(nameof(player))]
        protected PlayerRecord? MakePlayerRecord(Player? player)
        {
            if (player == null)
                return null;

            return new PlayerRecord(
                new NetUserId(player.UserId),
                new DateTimeOffset(NormalizeDatabaseTime(player.FirstSeenTime)),
                player.LastSeenUserName,
                new DateTimeOffset(NormalizeDatabaseTime(player.LastSeenTime)),
                player.LastSeenAddress,
                player.LastSeenHWId);
        }

        #endregion

        #region Connection Logs
        /*
         * CONNECTION LOG
         */
        public abstract Task<int> AddConnectionLogAsync(NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableTypedHwid? hwId,
            float trust,
            ConnectionDenyReason? denied,
            int serverId);

        public async Task AddServerBanHitsAsync(int connection, IEnumerable<ServerBanDef> bans)
        {
            await using var db = await GetDb();

            foreach (var ban in bans)
            {
                db.DbContext.ServerBanHit.Add(new ServerBanHit
                {
                    ConnectionId = connection, BanId = ban.Id!.Value
                });
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Admin Ranks
        /*
         * ADMIN RANKS
         */
        public async Task<Admin?> GetAdminDataForAsync(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.Admin
                .Include(p => p.Flags)
                .Include(p => p.AdminRank)
                .ThenInclude(p => p!.Flags)
                .AsSplitQuery() // tests fail because of a random warning if you dont have this!
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);
        }

        public abstract Task<((Admin admin, string? lastUserName, DateTime? lastSeenTime)[] admins, AdminRank[] ranks)>
            GetAllAdminAndRanksAsync(CancellationToken cancel);

        public async Task<AdminRank?> GetAdminRankDataForAsync(int id, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.AdminRank
                .Include(r => r.Flags)
                .SingleOrDefaultAsync(r => r.Id == id, cancel);
        }

        public async Task RemoveAdminAsync(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var admin = await db.DbContext.Admin.SingleAsync(a => a.UserId == userId.UserId, cancel);
            db.DbContext.Admin.Remove(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task AddAdminAsync(Admin admin, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            db.DbContext.Admin.Add(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateAdminAsync(Admin admin, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var existing = await db.DbContext.Admin.Include(a => a.Flags).SingleAsync(a => a.UserId == admin.UserId, cancel);
            existing.Flags = admin.Flags;
            existing.Title = admin.Title;
            existing.AdminRankId = admin.AdminRankId;
            existing.Deadminned = admin.Deadminned;
            existing.Suspended = admin.Suspended;

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateAdminDeadminnedAsync(NetUserId userId, bool deadminned, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var adminRecord = db.DbContext.Admin.Where(a => a.UserId == userId);
            await adminRecord.ExecuteUpdateAsync(
                set => set.SetProperty(p => p.Deadminned, deadminned),
                cancellationToken: cancel);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task RemoveAdminRankAsync(int rankId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var admin = await db.DbContext.AdminRank.SingleAsync(a => a.Id == rankId, cancel);
            db.DbContext.AdminRank.Remove(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task AddAdminRankAsync(AdminRank rank, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            db.DbContext.AdminRank.Add(rank);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task<int> AddNewRound(Server server, params Guid[] playerIds)
        {
            await using var db = await GetDb();

            var playerIdsList = playerIds.ToList();

            var players = await db.DbContext.Player
                .Where(player => playerIdsList.Contains(player.UserId))
                .ToListAsync();

            var round = new Round
            {
                StartDate = DateTime.UtcNow,
                Players = players,
                ServerId = server.Id
            };

            db.DbContext.Round.Add(round);

            await db.DbContext.SaveChangesAsync();

            return round.Id;
        }

        public async Task<Round> GetRound(int id)
        {
            await using var db = await GetDb();

            var round = await db.DbContext.Round
                .Include(round => round.Players)
                .SingleAsync(round => round.Id == id);

            return round;
        }

        public async Task AddRoundPlayers(int id, Guid[] playerIds)
        {
            await using var db = await GetDb();
            var playerIdsList = playerIds.ToList();

            // ReSharper disable once SuggestVarOrType_Elsewhere
            Dictionary<Guid, int> players = await db.DbContext.Player
                .Where(player => playerIdsList.Contains(player.UserId))
                .ToDictionaryAsync(player => player.UserId, player => player.Id);

            foreach (var player in playerIds)
            {
                await db.DbContext.Database.ExecuteSqlAsync($"""
INSERT INTO player_round (players_id, rounds_id) VALUES ({players[player]}, {id}) ON CONFLICT DO NOTHING
""");
            }

            await db.DbContext.SaveChangesAsync();
        }

        [return: NotNullIfNotNull(nameof(round))]
        protected RoundRecord? MakeRoundRecord(Round? round)
        {
            if (round == null)
                return null;

            return new RoundRecord(
                round.Id,
                NormalizeDatabaseTime(round.StartDate),
                MakeServerRecord(round.Server));
        }

        public async Task UpdateAdminRankAsync(AdminRank rank, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var existing = await db.DbContext.AdminRank
                .Include(r => r.Flags)
                .SingleAsync(a => a.Id == rank.Id, cancel);

            existing.Flags = rank.Flags;
            existing.Name = rank.Name;
            existing.ShortName = rank.ShortName; // Mono
            existing.HierarchyLevel = rank.HierarchyLevel;

            await db.DbContext.SaveChangesAsync(cancel);
        }
        #endregion

        #region Admin Logs

        public async Task<(Server, bool existed)> AddOrGetServer(string serverName)
        {
            await using var db = await GetDb();
            var server = await db.DbContext.Server
                .Where(server => server.Name.Equals(serverName))
                .SingleOrDefaultAsync();

            if (server != default)
                return (server, true);

            server = new Server
            {
                Name = serverName
            };

            db.DbContext.Server.Add(server);

            await db.DbContext.SaveChangesAsync();

            return (server, false);
        }

        [return: NotNullIfNotNull(nameof(server))]
        protected ServerRecord? MakeServerRecord(Server? server)
        {
            if (server == null)
                return null;

            return new ServerRecord(server.Id, server.Name);
        }

        public async Task AddAdminLogs(List<AdminLog> logs)
        {
            const int maxRetryAttempts = 5;
            var initialRetryDelay = TimeSpan.FromSeconds(5);

            DebugTools.Assert(logs.All(x => x.RoundId > 0), "Adding logs with invalid round ids.");

            var attempt = 0;
            var retryDelay = initialRetryDelay;

            while (attempt < maxRetryAttempts)
            {
                try
                {
                    await using var db = await GetDb();
                    db.DbContext.AdminLog.AddRange(logs);
                    await db.DbContext.SaveChangesAsync();
                    _opsLog.Debug($"Successfully saved {logs.Count} admin logs.");
                    break;
                }
                catch (Exception ex)
                {
                    attempt += 1;
                    _opsLog.Error($"Attempt {attempt} failed to save logs: {ex}");

                    if (attempt >= maxRetryAttempts)
                    {
                        _opsLog.Error($"Max retry attempts reached. Failed to save {logs.Count} admin logs.");
                        return;
                    }

                    _opsLog.Warning($"Retrying in {retryDelay.TotalSeconds} seconds...");
                    await Task.Delay(retryDelay);

                    retryDelay *= 2;
                }
            }
        }

        protected abstract IQueryable<AdminLog> StartAdminLogsQuery(ServerDbContext db, LogFilter? filter = null);

        private IQueryable<AdminLog> GetAdminLogsQuery(ServerDbContext db, LogFilter? filter = null)
        {
            // Save me from SQLite
            var query = StartAdminLogsQuery(db, filter);

            if (filter == null)
            {
                return query.OrderBy(log => log.Date);
            }

            if (filter.Round != null)
            {
                query = query.Where(log => log.RoundId == filter.Round);
            }

            if (filter.Types != null)
            {
                query = query.Where(log => filter.Types.Contains(log.Type));
            }

            if (filter.Impacts != null)
            {
                query = query.Where(log => filter.Impacts.Contains(log.Impact));
            }

            if (filter.Before != null)
            {
                query = query.Where(log => log.Date < filter.Before);
            }

            if (filter.After != null)
            {
                query = query.Where(log => log.Date > filter.After);
            }

            if (filter.IncludePlayers)
            {
                if (filter.AnyPlayers != null)
                {
                    query = query.Where(log =>
                        log.Players.Any(p => filter.AnyPlayers.Contains(p.PlayerUserId)) ||
                        log.Players.Count == 0 && filter.IncludeNonPlayers);
                }

                if (filter.AllPlayers != null)
                {
                    query = query.Where(log =>
                        log.Players.All(p => filter.AllPlayers.Contains(p.PlayerUserId)) ||
                        log.Players.Count == 0 && filter.IncludeNonPlayers);
                }
            }
            else
            {
                query = query.Where(log => log.Players.Count == 0);
            }

            if (filter.LastLogId != null)
            {
                query = filter.DateOrder switch
                {
                    DateOrder.Ascending => query.Where(log => log.Id > filter.LastLogId),
                    DateOrder.Descending => query.Where(log => log.Id < filter.LastLogId),
                    _ => throw new ArgumentOutOfRangeException(nameof(filter),
                        $"Unknown {nameof(DateOrder)} value {filter.DateOrder}")
                };
            }

            query = filter.DateOrder switch
            {
                DateOrder.Ascending => query.OrderBy(log => log.Date),
                DateOrder.Descending => query.OrderByDescending(log => log.Date),
                _ => throw new ArgumentOutOfRangeException(nameof(filter),
                    $"Unknown {nameof(DateOrder)} value {filter.DateOrder}")
            };

            const int hardLogLimit = 500_000;
            if (filter.Limit != null)
            {
                query = query.Take(Math.Min(filter.Limit.Value, hardLogLimit));
            }
            else
            {
                query = query.Take(hardLogLimit);
            }

            return query;
        }

        public async IAsyncEnumerable<string> GetAdminLogMessages(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);

            await foreach (var log in query.Select(log => log.Message).AsAsyncEnumerable())
            {
                yield return log;
            }
        }

        public async IAsyncEnumerable<SharedAdminLog> GetAdminLogs(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);
            query = query.Include(log => log.Players);

            await foreach (var log in query.AsAsyncEnumerable())
            {
                var players = new Guid[log.Players.Count];
                for (var i = 0; i < log.Players.Count; i++)
                {
                    players[i] = log.Players[i].PlayerUserId;
                }

                yield return new SharedAdminLog(log.Id, log.Type, log.Impact, log.Date, log.Message, players);
            }
        }

        public async IAsyncEnumerable<JsonDocument> GetAdminLogsJson(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);

            await foreach (var json in query.Select(log => log.Json).AsAsyncEnumerable())
            {
                yield return json;
            }
        }

        public async Task<int> CountAdminLogs(int round)
        {
            await using var db = await GetDb();
            return await db.DbContext.AdminLog.CountAsync(log => log.RoundId == round);
        }

        #endregion

        #region Whitelist

        public async Task<bool> GetWhitelistStatusAsync(NetUserId player)
        {
            await using var db = await GetDb();

            return await db.DbContext.Whitelist.AnyAsync(w => w.UserId == player);
        }

        public async Task AddToWhitelistAsync(NetUserId player)
        {
            await using var db = await GetDb();

            db.DbContext.Whitelist.Add(new Whitelist { UserId = player });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task RemoveFromWhitelistAsync(NetUserId player)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.Whitelist.SingleAsync(w => w.UserId == player);
            db.DbContext.Whitelist.Remove(entry);
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<DateTimeOffset?> GetLastReadRules(NetUserId player)
        {
            await using var db = await GetDb();

            return NormalizeDatabaseTime(await db.DbContext.Player
                .Where(dbPlayer => dbPlayer.UserId == player)
                .Select(dbPlayer => dbPlayer.LastReadRules)
                .SingleOrDefaultAsync());
        }

        public async Task SetLastReadRules(NetUserId player, DateTimeOffset? date)
        {
            await using var db = await GetDb();

            var dbPlayer = await db.DbContext.Player.Where(dbPlayer => dbPlayer.UserId == player).SingleOrDefaultAsync();
            if (dbPlayer == null)
            {
                return;
            }

            dbPlayer.LastReadRules = date?.UtcDateTime;
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<bool> GetBlacklistStatusAsync(NetUserId player)
        {
            await using var db = await GetDb();

            return await db.DbContext.Blacklist.AnyAsync(w => w.UserId == player);
        }

        public async Task AddToBlacklistAsync(NetUserId player)
        {
            await using var db = await GetDb();

            db.DbContext.Blacklist.Add(new Blacklist() { UserId = player });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task RemoveFromBlacklistAsync(NetUserId player)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.Blacklist.SingleAsync(w => w.UserId == player);
            db.DbContext.Blacklist.Remove(entry);
            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Uploaded Resources Logs

        public async Task AddUploadedResourceLogAsync(NetUserId user, DateTimeOffset date, string path, byte[] data)
        {
            await using var db = await GetDb();

            db.DbContext.UploadedResourceLog.Add(new UploadedResourceLog() { UserId = user, Date = date.UtcDateTime, Path = path, Data = data });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task PurgeUploadedResourceLogAsync(int days)
        {
            await using var db = await GetDb();

            var date = DateTime.UtcNow.Subtract(TimeSpan.FromDays(days));

            await foreach (var log in db.DbContext.UploadedResourceLog
                               .Where(l => date > l.Date)
                               .AsAsyncEnumerable())
            {
                db.DbContext.UploadedResourceLog.Remove(log);
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Admin Notes

        public virtual async Task<int> AddAdminNote(AdminNote note)
        {
            await using var db = await GetDb();
            db.DbContext.AdminNotes.Add(note);
            await db.DbContext.SaveChangesAsync();
            return note.Id;
        }

        public virtual async Task<int> AddAdminWatchlist(AdminWatchlist watchlist)
        {
            await using var db = await GetDb();
            db.DbContext.AdminWatchlists.Add(watchlist);
            await db.DbContext.SaveChangesAsync();
            return watchlist.Id;
        }

        public virtual async Task<int> AddAdminMessage(AdminMessage message)
        {
            await using var db = await GetDb();
            db.DbContext.AdminMessages.Add(message);
            await db.DbContext.SaveChangesAsync();
            return message.Id;
        }

        public async Task<AdminNoteRecord?> GetAdminNote(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminNotes
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminNoteRecord(entity);
        }

        private AdminNoteRecord MakeAdminNoteRecord(AdminNote entity)
        {
            return new AdminNoteRecord(
                entity.Id,
                MakeRoundRecord(entity.Round),
                MakePlayerRecord(entity.Player),
                entity.PlaytimeAtNote,
                entity.Message,
                entity.Severity,
                MakePlayerRecord(entity.CreatedBy),
                NormalizeDatabaseTime(entity.CreatedAt),
                MakePlayerRecord(entity.LastEditedBy),
                NormalizeDatabaseTime(entity.LastEditedAt),
                NormalizeDatabaseTime(entity.ExpirationTime),
                entity.Deleted,
                MakePlayerRecord(entity.DeletedBy),
                NormalizeDatabaseTime(entity.DeletedAt),
                entity.Secret);
        }

        public async Task<AdminWatchlistRecord?> GetAdminWatchlist(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminWatchlists
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminWatchlistRecord(entity);
        }

        public async Task<AdminMessageRecord?> GetAdminMessage(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminMessages
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminMessageRecord(entity);
        }

        private AdminMessageRecord MakeAdminMessageRecord(AdminMessage entity)
        {
            return new AdminMessageRecord(
                entity.Id,
                MakeRoundRecord(entity.Round),
                MakePlayerRecord(entity.Player),
                entity.PlaytimeAtNote,
                entity.Message,
                MakePlayerRecord(entity.CreatedBy),
                NormalizeDatabaseTime(entity.CreatedAt),
                MakePlayerRecord(entity.LastEditedBy),
                NormalizeDatabaseTime(entity.LastEditedAt),
                NormalizeDatabaseTime(entity.ExpirationTime),
                entity.Deleted,
                MakePlayerRecord(entity.DeletedBy),
                NormalizeDatabaseTime(entity.DeletedAt),
                entity.Seen,
                entity.Dismissed);
        }

        public async Task<ServerBanNoteRecord?> GetServerBanAsNoteAsync(int id)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .SingleOrDefaultAsync(b => b.Id == id);

            if (ban is null)
                return null;

            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == ban.PlayerUserId);
            return new ServerBanNoteRecord(
                ban.Id,
                MakeRoundRecord(ban.Round),
                MakePlayerRecord(player),
                ban.PlaytimeAtNote,
                ban.Reason,
                ban.Severity,
                MakePlayerRecord(ban.CreatedBy),
                ban.BanTime,
                MakePlayerRecord(ban.LastEditedBy),
                ban.LastEditedAt,
                ban.ExpirationTime,
                ban.Hidden,
                MakePlayerRecord(ban.Unban?.UnbanningAdmin == null
                    ? null
                    : await db.DbContext.Player.SingleOrDefaultAsync(p =>
                        p.UserId == ban.Unban.UnbanningAdmin.Value)),
                ban.Unban?.UnbanTime);
        }

        public async Task<ServerRoleBanNoteRecord?> GetServerRoleBanAsNoteAsync(int id)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.RoleBan
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .SingleOrDefaultAsync(b => b.Id == id);

            if (ban is null)
                return null;

            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == ban.PlayerUserId);
            var unbanningAdmin =
                ban.Unban is null
                ? null
                : await db.DbContext.Player.SingleOrDefaultAsync(b => b.UserId == ban.Unban.UnbanningAdmin);

            return new ServerRoleBanNoteRecord(
                ban.Id,
                MakeRoundRecord(ban.Round),
                MakePlayerRecord(player),
                ban.PlaytimeAtNote,
                ban.Reason,
                ban.Severity,
                MakePlayerRecord(ban.CreatedBy),
                ban.BanTime,
                MakePlayerRecord(ban.LastEditedBy),
                ban.LastEditedAt,
                ban.ExpirationTime,
                ban.Hidden,
                new [] { ban.RoleId.Replace(BanManager.JobPrefix, null) },
                MakePlayerRecord(unbanningAdmin),
                ban.Unban?.UnbanTime);
        }

        public async Task<List<IAdminRemarksRecord>> GetAllAdminRemarks(Guid player)
        {
            await using var db = await GetDb();
            List<IAdminRemarksRecord> notes = new();
            notes.AddRange(
                (await (from note in db.DbContext.AdminNotes
                        where note.PlayerUserId == player &&
                              !note.Deleted &&
                              (note.ExpirationTime == null || DateTime.UtcNow < note.ExpirationTime)
                        select note)
                    .Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.LastEditedBy)
                    .Include(note => note.Player)
                    .ToListAsync()).Select(MakeAdminNoteRecord));
            notes.AddRange(await GetActiveWatchlistsImpl(db, player));
            notes.AddRange(await GetMessagesImpl(db, player));
            notes.AddRange(await GetServerBansAsNotesForUser(db, player));
            notes.AddRange(await GetGroupedServerRoleBansAsNotesForUser(db, player));
            return notes;
        }
        public async Task EditAdminNote(int id, string message, NoteSeverity severity, bool secret, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminNotes.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.Severity = severity;
            note.Secret = secret;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditAdminWatchlist(int id, string message, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminWatchlists.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditAdminMessage(int id, string message, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminMessages.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminNote(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminNotes.Where(note => note.Id == id).SingleAsync();

            note.Deleted = true;
            note.DeletedById = deletedBy;
            note.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminWatchlist(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var watchlist = await db.DbContext.AdminWatchlists.Where(note => note.Id == id).SingleAsync();

            watchlist.Deleted = true;
            watchlist.DeletedById = deletedBy;
            watchlist.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminMessage(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var message = await db.DbContext.AdminMessages.Where(note => note.Id == id).SingleAsync();

            message.Deleted = true;
            message.DeletedById = deletedBy;
            message.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task HideServerBanFromNotes(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban.Where(ban => ban.Id == id).SingleAsync();

            ban.Hidden = true;
            ban.LastEditedById = deletedBy;
            ban.LastEditedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task HideServerRoleBanFromNotes(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var roleBan = await db.DbContext.RoleBan.Where(roleBan => roleBan.Id == id).SingleAsync();

            roleBan.Hidden = true;
            roleBan.LastEditedById = deletedBy;
            roleBan.LastEditedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<List<IAdminRemarksRecord>> GetVisibleAdminRemarks(Guid player)
        {
            await using var db = await GetDb();
            List<IAdminRemarksRecord> notesCol = new();
            notesCol.AddRange(
                (await (from note in db.DbContext.AdminNotes
                        where note.PlayerUserId == player &&
                              !note.Secret &&
                              !note.Deleted &&
                              (note.ExpirationTime == null || DateTime.UtcNow < note.ExpirationTime)
                        select note)
                    .Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.Player)
                    .ToListAsync()).Select(MakeAdminNoteRecord));
            notesCol.AddRange(await GetMessagesImpl(db, player));
            notesCol.AddRange(await GetServerBansAsNotesForUser(db, player));
            notesCol.AddRange(await GetGroupedServerRoleBansAsNotesForUser(db, player));
            return notesCol;
        }

        public async Task<List<AdminWatchlistRecord>> GetActiveWatchlists(Guid player)
        {
            await using var db = await GetDb();
            return await GetActiveWatchlistsImpl(db, player);
        }

        protected async Task<List<AdminWatchlistRecord>> GetActiveWatchlistsImpl(DbGuard db, Guid player)
        {
            var entities = await (from watchlist in db.DbContext.AdminWatchlists
                          where watchlist.PlayerUserId == player &&
                                !watchlist.Deleted &&
                                (watchlist.ExpirationTime == null || DateTime.UtcNow < watchlist.ExpirationTime)
                          select watchlist)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.Player)
                .ToListAsync();

            return entities.Select(MakeAdminWatchlistRecord).ToList();
        }

        private AdminWatchlistRecord MakeAdminWatchlistRecord(AdminWatchlist entity)
        {
            return new AdminWatchlistRecord(entity.Id, MakeRoundRecord(entity.Round), MakePlayerRecord(entity.Player), entity.PlaytimeAtNote, entity.Message, MakePlayerRecord(entity.CreatedBy), NormalizeDatabaseTime(entity.CreatedAt), MakePlayerRecord(entity.LastEditedBy), NormalizeDatabaseTime(entity.LastEditedAt), NormalizeDatabaseTime(entity.ExpirationTime), entity.Deleted, MakePlayerRecord(entity.DeletedBy), NormalizeDatabaseTime(entity.DeletedAt));
        }

        public async Task<List<AdminMessageRecord>> GetMessages(Guid player)
        {
            await using var db = await GetDb();
            return await GetMessagesImpl(db, player);
        }

        protected async Task<List<AdminMessageRecord>> GetMessagesImpl(DbGuard db, Guid player)
        {
            var entities = await (from message in db.DbContext.AdminMessages
                        where message.PlayerUserId == player && !message.Deleted &&
                              (message.ExpirationTime == null || DateTime.UtcNow < message.ExpirationTime)
                        select message).Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.LastEditedBy)
                    .Include(note => note.Player)
                    .ToListAsync();

            return entities.Select(MakeAdminMessageRecord).ToList();
        }

        public async Task MarkMessageAsSeen(int id, bool dismissedToo)
        {
            await using var db = await GetDb();
            var message = await db.DbContext.AdminMessages.SingleAsync(m => m.Id == id);
            message.Seen = true;
            if (dismissedToo)
                message.Dismissed = true;
            await db.DbContext.SaveChangesAsync();
        }

        // These two are here because they get converted into notes later
        protected async Task<List<ServerBanNoteRecord>> GetServerBansAsNotesForUser(DbGuard db, Guid user)
        {
            // You can't group queries, as player will not always exist. When it doesn't, the
            // whole query returns nothing
            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == user);
            var bans = await db.DbContext.Ban
                .Where(ban => ban.PlayerUserId == user && !ban.Hidden)
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .ToArrayAsync();

            var banNotes = new List<ServerBanNoteRecord>();
            foreach (var ban in bans)
            {
                var banNote = new ServerBanNoteRecord(
                    ban.Id,
                    MakeRoundRecord(ban.Round),
                    MakePlayerRecord(player),
                    ban.PlaytimeAtNote,
                    ban.Reason,
                    ban.Severity,
                    MakePlayerRecord(ban.CreatedBy),
                    NormalizeDatabaseTime(ban.BanTime),
                    MakePlayerRecord(ban.LastEditedBy),
                    NormalizeDatabaseTime(ban.LastEditedAt),
                    NormalizeDatabaseTime(ban.ExpirationTime),
                    ban.Hidden,
                    MakePlayerRecord(ban.Unban?.UnbanningAdmin == null
                        ? null
                        : await db.DbContext.Player.SingleOrDefaultAsync(
                            p => p.UserId == ban.Unban.UnbanningAdmin.Value)),
                    NormalizeDatabaseTime(ban.Unban?.UnbanTime));

                banNotes.Add(banNote);
            }

            return banNotes;
        }

        protected async Task<List<ServerRoleBanNoteRecord>> GetGroupedServerRoleBansAsNotesForUser(DbGuard db, Guid user)
        {
            // Server side query
            var bansQuery = await db.DbContext.RoleBan
                .Where(ban => ban.PlayerUserId == user && !ban.Hidden)
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .ToArrayAsync();

            // Client side query, as EF can't do groups yet
            var bansEnumerable = bansQuery
                    .GroupBy(ban => new { ban.BanTime, CreatedBy = (Player?)ban.CreatedBy, ban.Reason, Unbanned = ban.Unban == null })
                    .Select(banGroup => banGroup)
                    .ToArray();

            List<ServerRoleBanNoteRecord> bans = new();
            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == user);
            foreach (var banGroup in bansEnumerable)
            {
                var firstBan = banGroup.First();
                Player? unbanningAdmin = null;

                if (firstBan.Unban?.UnbanningAdmin is not null)
                    unbanningAdmin = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == firstBan.Unban.UnbanningAdmin.Value);

                bans.Add(new ServerRoleBanNoteRecord(
                    firstBan.Id,
                    MakeRoundRecord(firstBan.Round),
                    MakePlayerRecord(player),
                    firstBan.PlaytimeAtNote,
                    firstBan.Reason,
                    firstBan.Severity,
                    MakePlayerRecord(firstBan.CreatedBy),
                    NormalizeDatabaseTime(firstBan.BanTime),
                    MakePlayerRecord(firstBan.LastEditedBy),
                    NormalizeDatabaseTime(firstBan.LastEditedAt),
                    NormalizeDatabaseTime(firstBan.ExpirationTime),
                    firstBan.Hidden,
                    banGroup.Select(ban => ban.RoleId.Replace(BanManager.JobPrefix, null)).ToArray(),
                    MakePlayerRecord(unbanningAdmin),
                    NormalizeDatabaseTime(firstBan.Unban?.UnbanTime)));
            }

            return bans;
        }

        #endregion

        #region Job Whitelists

        public async Task<bool> AddJobWhitelist(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            var exists = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .AnyAsync();

            if (exists)
                return false;

            var whitelist = new RoleWhitelist
            {
                PlayerUserId = player,
                RoleId = job
            };
            db.DbContext.RoleWhitelists.Add(whitelist);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<List<string>> GetJobWhitelists(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Select(w => w.RoleId)
                .ToListAsync(cancellationToken: cancel);
        }

        public async Task<bool> IsJobWhitelisted(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .AnyAsync();
        }

        public async Task<bool> RemoveJobWhitelist(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .SingleOrDefaultAsync();

            if (entry == null)
                return false;

            db.DbContext.RoleWhitelists.Remove(entry);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        // Frontier: Ghost role handling
        # endregion

        # region Ghost Role Whitelists

        public async Task<bool> AddGhostRoleWhitelist(Guid player, ProtoId<GhostRolePrototype> ghostRole)
        {
            await using var db = await GetDb();
            var exists = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == ghostRole.Id)
                .AnyAsync();

            if (exists)
                return false;

            var whitelist = new RoleWhitelist
            {
                PlayerUserId = player,
                RoleId = ghostRole
            };
            db.DbContext.RoleWhitelists.Add(whitelist);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsGhostRoleWhitelisted(Guid player, ProtoId<GhostRolePrototype> ghostRole)
        {
            await using var db = await GetDb();
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == ghostRole.Id)
                .AnyAsync();
        }

        public async Task<bool> RemoveGhostRoleWhitelist(Guid player, ProtoId<GhostRolePrototype> ghostRole)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == ghostRole.Id)
                .SingleOrDefaultAsync();

            if (entry == null)
                return false;

            db.DbContext.RoleWhitelists.Remove(entry);
            await db.DbContext.SaveChangesAsync();
            return true;
        }
        // End Frontier: Ghost role handling

        #endregion

        # region IPIntel

        public async Task<bool> UpsertIPIntelCache(DateTime time, IPAddress ip, float score)
        {
            while (true)
            {
                try
                {
                    await using var db = await GetDb();

                    var existing = await db.DbContext.IPIntelCache
                        .Where(w => ip.Equals(w.Address))
                        .SingleOrDefaultAsync();

                    if (existing == null)
                    {
                        var newCache = new IPIntelCache
                        {
                            Time = time,
                            Address = ip,
                            Score = score,
                        };
                        db.DbContext.IPIntelCache.Add(newCache);
                    }
                    else
                    {
                        existing.Time = time;
                        existing.Score = score;
                    }

                    await Task.Delay(5000);

                    await db.DbContext.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException)
                {
                    _opsLog.Warning("IPIntel UPSERT failed with a db exception... retrying.");
                }
            }
        }

        public async Task<IPIntelCache?> GetIPIntelCache(IPAddress ip)
        {
            await using var db = await GetDb();

            return await db.DbContext.IPIntelCache
                .SingleOrDefaultAsync(w => ip.Equals(w.Address));
        }

        public async Task<bool> CleanIPIntelCache(TimeSpan range)
        {
            await using var db = await GetDb();

            // Calculating this here cause otherwise sqlite whines.
            var cutoffTime = DateTime.UtcNow.Subtract(range);

            await db.DbContext.IPIntelCache
                .Where(w => w.Time <= cutoffTime)
                .ExecuteDeleteAsync();

            await db.DbContext.SaveChangesAsync();
            return true;
        }

        #endregion

        // Mono
        #region Company

        public async Task<bool> AddCompanyMember(Guid player, ProtoId<CompanyPrototype> company)
        {
            await using var db = await GetDb();
            var exists = await db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CompanyId == company.Id)
                .AnyAsync();

            if (exists)
                return false;

            var member = new CompanyMember
            {
                PlayerUserId = player,
                CompanyId = company,
            };
            db.DbContext.CompanyMembers.Add(member);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<List<string>> GetPlayerCompanies(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Select(w => w.CompanyId)
                .ToListAsync(cancel);
        }

        public async Task<IEnumerable<CompanyMemberRecord>> GetCompanyMembers(ProtoId<CompanyPrototype> company, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var members = await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Include(c => c.Player)
                .ToListAsync(cancel);

            return members.Select(m => new CompanyMemberRecord()
            {
                Company = company,
                Owner = m.Owner,
                PlayerUserId = m.PlayerUserId,
                LastSeenUserName = m.Player.LastSeenUserName,
            });
        }

        public async Task<IEnumerable<CompanyMemberRecord>> GetAllCompanyMembers(CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var members = await db.DbContext.CompanyMembers
                .Include(c => c.Player)
                .ToListAsync(cancel);

            return members.Select(m => new CompanyMemberRecord()
            {
                Company = m.CompanyId,
                Owner = m.Owner,
                PlayerUserId = m.PlayerUserId,
                LastSeenUserName = m.Player.LastSeenUserName,
            });
        }

        public async Task<CompanyMemberRecord?> GetCompanyMember(ProtoId<CompanyPrototype> company, Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var member = await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Where(w => w.PlayerUserId == player)
                .Include(c => c.Player)
                .FirstOrDefaultAsync();

            if (member == null)
                return null;

            return new CompanyMemberRecord()
            {
                Company = company,
                LastSeenUserName = member.Player.LastSeenUserName,
                Owner = member.Owner,
                PlayerUserId = member.PlayerUserId,
            };
        }

        public async Task SetCompanyOwner(ProtoId<CompanyPrototype> company, Guid player, bool owner)
        {
            await using var db = await GetDb();
            await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Where(w => w.PlayerUserId == player)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Owner, owner));
        }

        public async Task<bool> RemoveCompanyMember(Guid player, ProtoId<CompanyPrototype> company)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CompanyId == company.Id)
                .SingleOrDefaultAsync();

            if (entry == null)
                return false;

            db.DbContext.CompanyMembers.Remove(entry);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        #endregion

        #region Dialogue Persistent Memory

        public async Task<DialoguePersistentMemoryData?> GetDialoguePersistentMemoryAsync(
            NetUserId userId,
            string memoryKey,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var entry = await db.DbContext.DialoguePersistentMemories.SingleOrDefaultAsync(
                memory => memory.PlayerUserId == userId.UserId && memory.MemoryKey == memoryKey,
                cancel);

            return entry == null
                ? null
                : JsonSerializer.Deserialize<DialoguePersistentMemoryData>(entry.Data);
        }

        public async Task SetDialoguePersistentMemoryAsync(
            NetUserId userId,
            string memoryKey,
            DialoguePersistentMemoryData data,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var entry = await db.DbContext.DialoguePersistentMemories.SingleOrDefaultAsync(
                memory => memory.PlayerUserId == userId.UserId && memory.MemoryKey == memoryKey,
                cancel);
            var serialized = JsonSerializer.Serialize(data);

            if (entry == null)
            {
                db.DbContext.DialoguePersistentMemories.Add(new DialoguePersistentMemory
                {
                    PlayerUserId = userId.UserId,
                    MemoryKey = memoryKey,
                    Data = serialized,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                entry.Data = serialized;
                entry.UpdatedAt = DateTime.UtcNow;
            }

            await db.DbContext.SaveChangesAsync(cancel);
        }

        #endregion

        #region Ghost Permissions

        public async Task<GhostPermissionData?> GetGhostPermissionAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var entry = await db.DbContext.GhostPermissions.SingleOrDefaultAsync(
                permission => permission.PlayerUserId == userId.UserId,
                cancel);

            return entry == null
                ? null
                : new GhostPermissionData(entry.RemainingUses, entry.ExpiresAt);
        }

        public async Task SetGhostPermissionAsync(
            NetUserId userId,
            GhostPermissionData permission,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var entry = await db.DbContext.GhostPermissions.SingleOrDefaultAsync(
                existing => existing.PlayerUserId == userId.UserId,
                cancel);

            if (entry == null)
            {
                db.DbContext.GhostPermissions.Add(new GhostPermission
                {
                    PlayerUserId = userId.UserId,
                    RemainingUses = permission.RemainingUses,
                    ExpiresAt = permission.ExpiresAt,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                entry.RemainingUses = permission.RemainingUses;
                entry.ExpiresAt = permission.ExpiresAt;
                entry.UpdatedAt = DateTime.UtcNow;
            }

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task RemoveGhostPermissionAsync(NetUserId userId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var entry = await db.DbContext.GhostPermissions.SingleOrDefaultAsync(
                permission => permission.PlayerUserId == userId.UserId,
                cancel);

            if (entry == null)
                return;

            db.DbContext.GhostPermissions.Remove(entry);
            await db.DbContext.SaveChangesAsync(cancel);
        }

        #endregion

        public abstract Task SendNotification(DatabaseNotification notification);

        // SQLite returns DateTime as Kind=Unspecified, Npgsql actually knows for sure it's Kind=Utc.
        // Normalize DateTimes here so they're always Utc. Thanks.
        protected abstract DateTime NormalizeDatabaseTime(DateTime time);

        [return: NotNullIfNotNull(nameof(time))]
        protected DateTime? NormalizeDatabaseTime(DateTime? time)
        {
            return time != null ? NormalizeDatabaseTime(time.Value) : time;
        }

        public async Task<bool> HasPendingModelChanges()
        {
            await using var db = await GetDb();
            return db.DbContext.Database.HasPendingModelChanges();
        }

        protected abstract Task<DbGuard> GetDb(
            CancellationToken cancel = default,
            [CallerMemberName] string? name = null);

        protected void LogDbOp(string? name)
        {
            _opsLog.Verbose($"Running DB operation: {name ?? "unknown"}");
        }

        protected abstract class DbGuard : IAsyncDisposable
        {
            public abstract ServerDbContext DbContext { get; }

            public abstract ValueTask DisposeAsync();
        }

        protected void NotificationReceived(DatabaseNotification notification)
        {
            OnNotificationReceived?.Invoke(notification);
        }

        public virtual void Shutdown()
        {

        }
    }
}
