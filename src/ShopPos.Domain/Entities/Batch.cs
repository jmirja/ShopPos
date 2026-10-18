using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Batch
{
    public Guid Id { get; set; }

    public Guid VariantId { get; set; }

    public string BatchNo { get; set; } = null!;

    public DateOnly? ExpiryDate { get; set; }

    public decimal CostPrice { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<SaleLine> SaleLines { get; set; } = new List<SaleLine>();

    public virtual ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public virtual ProductVariant Variant { get; set; } = null!;
}
