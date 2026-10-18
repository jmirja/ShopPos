using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class GoodsReceiptLine
{
    public Guid Id { get; set; }

    public Guid ReceiptId { get; set; }

    public Guid VariantId { get; set; }

    public Guid? PoLineId { get; set; }

    public decimal Qty { get; set; }

    public decimal UnitCost { get; set; }

    public string? BatchNo { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public virtual PurchaseOrderLine? PoLine { get; set; }

    public virtual GoodsReceipt Receipt { get; set; } = null!;

    public virtual ICollection<SerialItem> SerialItems { get; set; } = new List<SerialItem>();

    public virtual ProductVariant Variant { get; set; } = null!;
}
