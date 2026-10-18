using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class PurchaseOrder
{
    public Guid Id { get; set; }

    public string PoNo { get; set; } = null!;

    public Guid BranchId { get; set; }

    public Guid SupplierId { get; set; }

    public DateOnly OrderDate { get; set; }

    public string Status { get; set; } = null!;

    public decimal Total { get; set; }

    public string? Notes { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();

    public virtual ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; } = new List<PurchaseOrderLine>();

    public virtual Supplier Supplier { get; set; } = null!;
}
