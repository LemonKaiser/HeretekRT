using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbContext
{
    public DbSet<Wh40kFactionStorageSlot> Wh40kFactionStorageSlots { get; set; } = null!;
}

// Empty rows retain their revision so an old UI request cannot withdraw a replacement item.
[Table("wh40k_faction_storage_slot"), PrimaryKey(nameof(Faction), nameof(Slot))]
public sealed class Wh40kFactionStorageSlot
{
    [MaxLength(128)] public string Faction { get; set; } = null!;
    public int Slot { get; set; }
    [ConcurrencyCheck] public Guid Revision { get; set; }
    public byte[]? Payload { get; set; }
    public byte[]? Sha256 { get; set; }
    [MaxLength(128)] public string? Prototype { get; set; }
    [MaxLength(4096)] public string? Name { get; set; }
    [MaxLength(16384)] public string? Description { get; set; }
    public int Count { get; set; }
}
