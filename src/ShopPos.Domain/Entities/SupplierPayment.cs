using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class SupplierPayment
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid SupplierId { get; set; }

    public Guid? ReceiptId { get; set; }

    public int PaymentMethodId { get; set; }

    public decimal Amount { get; set; }

    public string? ReferenceNo { get; set; }

    public DateTime PaidAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual User? CreatedByUser { get; set; }

    public virtual PaymentMethod PaymentMethod { get; set; } = null!;

    public virtual GoodsReceipt? Receipt { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;
}
