using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbContext
{
    public DbSet<Wh40kShuttleSlot> Wh40kShuttleSlots { get; set; } = null!;
}

public enum Wh40kShuttleState : byte { Empty, Storing, Stored, Loading, RecoveryRequired }

[Table("wh40k_shuttle_slot")]
public sealed class Wh40kShuttleSlot
{
    [Key] public Guid Owner { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; }
    public Guid Operation { get; set; }
    public Guid Ship { get; set; }
    public Guid Epoch { get; set; }
    public Wh40kShuttleState State { get; set; }
    public bool Authorized { get; set; }
    public int Result { get; set; }
    public int Schema { get; set; }
    [MaxLength(4096)] public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int Entities { get; set; }
    public int Bytes { get; set; }
    public DateTime? SavedAt { get; set; }
    public byte[]? Payload { get; set; }
    public byte[]? Sha256 { get; set; }

    public Wh40kShuttleSlot Copy() => (Wh40kShuttleSlot) MemberwiseClone();

    public bool MatchesTransition(Wh40kShuttleSlot other) => Owner == other.Owner && Revision == other.Revision &&
        Operation == other.Operation && Ship == other.Ship && Epoch == other.Epoch && State == other.State &&
        Authorized == other.Authorized && Result == other.Result &&
        (State == Wh40kShuttleState.RecoveryRequired || Schema == other.Schema && Name == other.Name &&
            Width == other.Width && Height == other.Height && Entities == other.Entities && Bytes == other.Bytes &&
            EqualBytes(Sha256, other.Sha256) && EqualBytes(Payload, other.Payload));

    private static bool EqualBytes(byte[]? first, byte[]? second) =>
        ReferenceEquals(first, second) || first != null && second != null && first.SequenceEqual(second);
}
