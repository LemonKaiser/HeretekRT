using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public static class PersistentInventoryStoragePolicy
{
    public const int AccountBytes = 1024 * 1024;
    public const int SnapshotBytes = 128 * 1024;
    public const int PayloadBytes = 512 * 1024;
    public const int HistoryBytes = 64 * 1024;
    public const int HistoryEntries = 100;
    public const int ReceiptEntries = 64;
    public static readonly TimeSpan HistoryLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan LostLifetime = TimeSpan.FromHours(72);
}

[Table("wh40k_persistent_inventory_storage")]
public sealed class Wh40kPersistentInventoryStorage
{
    [Key] public Guid UserId { get; set; }
    public Guid? LostSnapshotId { get; set; }
    public DateTime? LostExpiresAt { get; set; }
    public long LastCountedRevision { get; set; }
    public long PrunedEvents { get; set; }
    public long Saves { get; set; }
    public long Losses { get; set; }
    public long Recoveries { get; set; }
}

[Table("wh40k_persistent_inventory_blob")]
[Index(nameof(UserId), nameof(Sha256), IsUnique = true)]
public sealed class Wh40kPersistentInventoryBlob
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [Required, MaxLength(32)] public byte[] Sha256 { get; set; } = default!;
    [Required] public byte[] Payload { get; set; } = default!;
}

[Table("wh40k_persistent_inventory_receipt")]
[PrimaryKey(nameof(UserId), nameof(OperationId), nameof(Action))]
[Index(nameof(UserId), nameof(Revision))]
public sealed class Wh40kPersistentInventoryReceipt
{
    public Guid UserId { get; set; }
    public Guid OperationId { get; set; }
    public int Action { get; set; }
    public long Revision { get; set; }
    public int NewState { get; set; }
    public Guid? SnapshotId { get; set; }
}

public sealed record PersistentInventoryStorageUsage(
    int Snapshots, int Blobs, long PayloadBytes, int HistoryEntries, long HistoryBytes,
    int Receipts, long EstimatedBytes, long PrunedEvents, long Saves, long Losses, long Recoveries);

public sealed record PersistentInventoryStorageTotals(long Accounts, long PayloadBytes, long HistoryEntries, long OverBudgetAccounts);

public sealed record PersistentInventoryMaintenanceResult(Guid? NextUser, int Accounts);
