using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class Sale
{
    public Guid Id { get; set; }

    public string? InvoiceNo { get; set; }

    public Guid BranchId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid UserId { get; set; }

    public DateTime SaleDate { get; set; }

    public decimal SubTotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    public decimal PaidAmount { get; set; }

    public decimal DueAmount { get; set; }

    public string Status { get; set; } = null!;

    public string? VoidReason { get; set; }

    public Guid? VoidedByUserId { get; set; }

    public string? Notes { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<RepairJob> RepairJobs { get; set; } = new List<RepairJob>();

    public virtual ICollection<SaleLine> SaleLines { get; set; } = new List<SaleLine>();

    public virtual User User { get; set; } = null!;

    public virtual User? VoidedByUser { get; set; }
}
