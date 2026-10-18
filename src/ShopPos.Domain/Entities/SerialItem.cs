using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class SerialItem
{
    public Guid Id { get; set; }

    public Guid VariantId { get; set; }

    public string SerialNo { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid? GoodsReceiptLineId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual GoodsReceiptLine? GoodsReceiptLine { get; set; }

    public virtual ICollection<SaleLine> SaleLines { get; set; } = new List<SaleLine>();

    public virtual ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public virtual ProductVariant Variant { get; set; } = null!;
}
