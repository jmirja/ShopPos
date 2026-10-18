using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class StockMovement
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid VariantId { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? SerialItemId { get; set; }

    public decimal QtyChange { get; set; }

    public string MovementType { get; set; } = null!;

    public string? ReferenceType { get; set; }

    public Guid? ReferenceId { get; set; }

    public string? Reason { get; set; }

    public decimal? UnitCost { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Batch? Batch { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual SerialItem? SerialItem { get; set; }

    public virtual ProductVariant Variant { get; set; } = null!;
}
