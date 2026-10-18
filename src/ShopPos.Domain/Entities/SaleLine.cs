using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class SaleLine
{
    public Guid Id { get; set; }

    public Guid SaleId { get; set; }

    public Guid VariantId { get; set; }

    public Guid? SerialItemId { get; set; }

    public Guid? BatchId { get; set; }

    public string? Description { get; set; }

    public decimal Qty { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal UnitCostSnapshot { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal LineTotal { get; set; }

    public virtual Batch? Batch { get; set; }

    public virtual Sale Sale { get; set; } = null!;

    public virtual SerialItem? SerialItem { get; set; }

    public virtual ProductVariant Variant { get; set; } = null!;
}
