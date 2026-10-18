using System;
using System.Collections.Generic;

namespace ShopPos.Domain.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public int RoleId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public bool? IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual Branch Branch { get; set; } = null!;

    public virtual ICollection<CashDrawerSession> CashDrawerSessions { get; set; } = new List<CashDrawerSession>();

    public virtual ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();

    public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

    public virtual ICollection<MoneyTransfer> MoneyTransfers { get; set; } = new List<MoneyTransfer>();

    public virtual ICollection<ParcelBooking> ParcelBookings { get; set; } = new List<ParcelBooking>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();

    public virtual ICollection<RepairJob> RepairJobs { get; set; } = new List<RepairJob>();

    public virtual ICollection<RepairStatusHistory> RepairStatusHistories { get; set; } = new List<RepairStatusHistory>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<Sale> SaleUsers { get; set; } = new List<Sale>();

    public virtual ICollection<Sale> SaleVoidedByUsers { get; set; } = new List<Sale>();

    public virtual ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();
}
