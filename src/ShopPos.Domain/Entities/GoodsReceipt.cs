using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class GoodsReceipt
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid? PoId { get; set; }

    public Guid SupplierId { get; set; }

    public string ReceiptNo { get; set; } = null!;

    public string? SupplierInvoiceNo { get; set; }

    public DateOnly ReceivedDate { get; set; }

    public decimal Total { get; set; }

    public decimal PaidAmount { get; set; }

    public decimal DueAmount { get; set; }

    public string? Notes { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual ICollection<GoodsReceiptLine> GoodsReceiptLines { get; set; } = new List<GoodsReceiptLine>();

    public virtual PurchaseOrder? Po { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;

    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();
}
