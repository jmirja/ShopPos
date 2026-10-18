using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public DateOnly EntryDate { get; set; }

    public int AccountCategoryId { get; set; }

    public string Type { get; set; } = null!;

    public decimal Amount { get; set; }

    public int? PaymentMethodId { get; set; }

    public string SourceType { get; set; } = null!;

    public Guid? SourceId { get; set; }

    public string? Note { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AccountCategory AccountCategory { get; set; } = null!;

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual PaymentMethod? PaymentMethod { get; set; }
}
