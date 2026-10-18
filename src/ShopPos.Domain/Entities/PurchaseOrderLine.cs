using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class PurchaseOrderLine
{
    public Guid Id { get; set; }

    public Guid PoId { get; set; }

    public Guid VariantId { get; set; }

    public decimal QtyOrdered { get; set; }

    public decimal QtyReceived { get; set; }

    public decimal UnitCost { get; set; }

    public virtual ICollection<GoodsReceiptLine> GoodsReceiptLines { get; set; } = new List<GoodsReceiptLine>();

    public virtual PurchaseOrder Po { get; set; } = null!;

    public virtual ProductVariant Variant { get; set; } = null!;
}
