using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbContext
{
    public DbSet<Wh40kQuestOwner> Wh40kQuestOwners { get; set; } = null!;
    public DbSet<Wh40kQuestOffer> Wh40kQuestOffers { get; set; } = null!;
    public DbSet<Wh40kQuestInstance> Wh40kQuestInstances { get; set; } = null!;
    public DbSet<Wh40kQuestObjective> Wh40kQuestObjectives { get; set; } = null!;
    public DbSet<Wh40kQuestInbox> Wh40kQuestInboxes { get; set; } = null!;
    public DbSet<Wh40kQuestReceipt> Wh40kQuestReceipts { get; set; } = null!;
    public DbSet<Wh40kQuestOperation> Wh40kQuestOperations { get; set; } = null!;
    public DbSet<Wh40kQuestRewardIntent> Wh40kQuestRewardIntents { get; set; } = null!;
    public DbSet<Wh40kQuestSubmission> Wh40kQuestSubmissions { get; set; } = null!;
    public DbSet<Wh40kQuestAudit> Wh40kQuestAudits { get; set; } = null!;
    public DbSet<Wh40kQuestAccountState> Wh40kQuestAccountStates { get; set; } = null!;

    private static void ConfigureWh40kQuests(ModelBuilder model)
    {
        model.Entity<Wh40kQuestOffer>().HasOne<Wh40kQuestOwner>().WithMany().HasForeignKey(x => x.OwnerKey).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestInstance>().HasOne<Wh40kQuestOwner>().WithMany().HasForeignKey(x => x.OwnerKey).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestInstance>().HasOne<Wh40kQuestOffer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestObjective>().HasOne<Wh40kQuestInstance>().WithMany().HasForeignKey(x => x.QuestId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestInbox>().HasOne<Wh40kQuestObjective>().WithMany().HasForeignKey(x => new { x.QuestId, x.ObjectiveId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestReceipt>().HasOne<Wh40kQuestObjective>().WithMany().HasForeignKey(x => new { x.QuestId, x.ObjectiveId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestRewardIntent>().HasOne<Wh40kQuestInstance>().WithMany().HasForeignKey(x => x.QuestId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestSubmission>().HasOne<Wh40kQuestObjective>().WithMany().HasForeignKey(x => new { x.QuestId, x.ObjectiveId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestOperation>().HasOne<Wh40kQuestOwner>().WithMany().HasForeignKey(x => x.OwnerKey).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestAudit>().HasOne<Wh40kQuestOwner>().WithMany().HasForeignKey(x => x.OwnerKey).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Wh40kQuestObjective>().ToTable(t => t.HasCheckConstraint("quest_objective_count", "count >= 0 AND count <= required_count AND required_count > 0"));
        model.Entity<Wh40kQuestSubmission>().ToTable(t => t.HasCheckConstraint("quest_submission_amount", "amount > 0"));
    }
}

[Table("wh40k_quest_account_state"), PrimaryKey(nameof(UserId), nameof(Key))]
public sealed class Wh40kQuestAccountState
{
    public Guid UserId { get; set; }
    [MaxLength(128)] public string Key { get; set; } = null!;
    [MaxLength(128)] public string? Value { get; set; }
    public long Amount { get; set; }
}

[Table("wh40k_quest_owner")]
public sealed class Wh40kQuestOwner
{
    [Key, MaxLength(64)] public string Key { get; set; } = null!;
    [ConcurrencyCheck] public long Revision { get; set; }
}

[Table("wh40k_quest_offer"), Index(nameof(OwnerKey), nameof(State), nameof(ExpiresAt))]
public sealed class Wh40kQuestOffer
{
    [Key] public Guid Id { get; set; }
    [Required, MaxLength(64)] public string OwnerKey { get; set; } = null!;
    [Required, MaxLength(128)] public string DefinitionId { get; set; } = null!;
    [Required, MaxLength(131072)] public string TermsJson { get; set; } = null!;
    public int State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ActiveLimit { get; set; }
}

[Table("wh40k_quest_instance"), Index(nameof(OfferId), IsUnique = true)]
[Index(nameof(OwnerKey), nameof(State)), Index(nameof(OwnerKey), nameof(DefinitionId), nameof(ClosedAt))]
public sealed class Wh40kQuestInstance
{
    [Key] public Guid Id { get; set; }
    public Guid OfferId { get; set; }
    [Required, MaxLength(64)] public string OwnerKey { get; set; } = null!;
    [Required, MaxLength(128)] public string DefinitionId { get; set; } = null!;
    [Required, MaxLength(131072)] public string SnapshotJson { get; set; } = null!;
    public int State { get; set; }
    public DateTime AcceptedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? LastGeneration { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; }
}

[Table("wh40k_quest_objective"), PrimaryKey(nameof(QuestId), nameof(ObjectiveId))]
public sealed class Wh40kQuestObjective
{
    public Guid QuestId { get; set; }
    [MaxLength(64)] public string ObjectiveId { get; set; } = null!;
    public int Ordinal { get; set; }
    public int RequiredCount { get; set; }
    public int Count { get; set; }
}

[Table("wh40k_quest_inbox"), PrimaryKey(nameof(QuestId), nameof(ObjectiveId), nameof(EventId))]
[Index(nameof(State), nameof(RetryAfter), nameof(OccurredAt))]
public sealed class Wh40kQuestInbox
{
    public Guid QuestId { get; set; }
    [MaxLength(64)] public string ObjectiveId { get; set; } = null!;
    public Guid EventId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? RetryAfter { get; set; }
    [Required, MaxLength(32768)] public string FactJson { get; set; } = null!;
    public int State { get; set; }
}

[Table("wh40k_quest_receipt"), PrimaryKey(nameof(QuestId), nameof(ObjectiveId), nameof(EventId))]
[Index(nameof(QuestId), nameof(ObjectiveId), nameof(TargetToken), IsUnique = true)]
public sealed class Wh40kQuestReceipt
{
    public Guid QuestId { get; set; }
    [MaxLength(64)] public string ObjectiveId { get; set; } = null!;
    public Guid EventId { get; set; }
    [MaxLength(128)] public string? TargetToken { get; set; }
    public DateTime AppliedAt { get; set; }
}

[Table("wh40k_quest_operation"), PrimaryKey(nameof(OwnerKey), nameof(OperationId))]
public sealed class Wh40kQuestOperation
{
    [MaxLength(64)] public string OwnerKey { get; set; } = null!;
    public Guid OperationId { get; set; }
    [Required, MaxLength(64)] public string RequestHash { get; set; } = null!;
    public int Result { get; set; }
    public Guid? QuestId { get; set; }
    public long Revision { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("wh40k_quest_reward_intent"), PrimaryKey(nameof(QuestId), nameof(EntryId), nameof(Recipient))]
[Index(nameof(State))]
public sealed class Wh40kQuestRewardIntent
{
    public Guid QuestId { get; set; }
    [MaxLength(64)] public string EntryId { get; set; } = null!;
    public Guid Recipient { get; set; }
    [Required, MaxLength(128)] public string RewardId { get; set; } = null!;
    [Required, MaxLength(4096)] public string PayloadJson { get; set; } = null!;
    public int State { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("wh40k_quest_submission"), Index(nameof(QuestId), nameof(ObjectiveId), nameof(State))]
public sealed class Wh40kQuestSubmission
{
    [Key] public Guid Id { get; set; }
    public Guid QuestId { get; set; }
    [MaxLength(64)] public string ObjectiveId { get; set; } = null!;
    public int Amount { get; set; }
    public int State { get; set; }
    [Required, MaxLength(1048576)] public string PayloadJson { get; set; } = null!;
    [ConcurrencyCheck] public long Revision { get; set; }
}

[Table("wh40k_quest_audit"), Index(nameof(OwnerKey), nameof(Id))]
public sealed class Wh40kQuestAudit
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    [Required, MaxLength(64)] public string OwnerKey { get; set; } = null!;
    public Guid? QuestId { get; set; }
    public Guid Actor { get; set; }
    [Required, MaxLength(64)] public string Action { get; set; } = null!;
    [Required, MaxLength(512)] public string Reason { get; set; } = null!;
    public DateTime At { get; set; }
}
