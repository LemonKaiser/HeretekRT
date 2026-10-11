using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbContext
{
    public DbSet<Wh40kMerchantSale> Wh40kMerchantSales { get; set; } = null!;
}

/// <summary>One durable title per approved item. Paid titles cannot be sold again, including restored copies.</summary>
[Table("wh40k_merchant_sale"), Index(nameof(Operation), IsUnique = true), Index(nameof(UserId), nameof(State))]
public sealed class Wh40kMerchantSale
{
    [Key] public Guid Token { get; set; }
    public Guid UserId { get; set; }
    public Guid Operation { get; set; }
    [MaxLength(128)] public string Merchant { get; set; } = null!;
    [MaxLength(128)] public string Product { get; set; } = null!;
    public int Price { get; set; }
    // The concurrency fence also protects one item title claimed through different account locks.
    // 0 reserved, 1 paid, 2 returned unpaid.
    [ConcurrencyCheck] public int State { get; set; }
    [MaxLength(131072)] public string PayloadJson { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
