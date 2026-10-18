using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class ProductVariant
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Sku { get; set; } = null!;

    public string? Barcode { get; set; }

    public string? VariantName { get; set; }

    public decimal CostPrice { get; set; }

    public decimal SellingPrice { get; set; }

    public decimal ReorderLevel { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<Batch> Batches { get; set; } = new List<Batch>();

    public virtual ICollection<GoodsReceiptLine> GoodsReceiptLines { get; set; } = new List<GoodsReceiptLine>();

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; } = new List<PurchaseOrderLine>();

    public virtual ICollection<RepairJobPart> RepairJobParts { get; set; } = new List<RepairJobPart>();

    public virtual ICollection<SaleLine> SaleLines { get; set; } = new List<SaleLine>();

    public virtual ICollection<SerialItem> SerialItems { get; set; } = new List<SerialItem>();

    public virtual ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
