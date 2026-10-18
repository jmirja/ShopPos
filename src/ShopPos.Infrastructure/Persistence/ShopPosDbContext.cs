using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ShopPos.Domain.Entities;

namespace ShopPos.Infrastructure.Persistence;

public partial class ShopPosDbContext : DbContext
{
    public ShopPosDbContext(DbContextOptions<ShopPosDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AccountCategory> AccountCategories { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Batch> Batches { get; set; }

    public virtual DbSet<Branch> Branches { get; set; }

    public virtual DbSet<CashDrawerSession> CashDrawerSessions { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Courier> Couriers { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<GoodsReceipt> GoodsReceipts { get; set; }

    public virtual DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }

    public virtual DbSet<LedgerEntry> LedgerEntries { get; set; }

    public virtual DbSet<MoneyTransfer> MoneyTransfers { get; set; }

    public virtual DbSet<ParcelBooking> ParcelBookings { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentMethod> PaymentMethods { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductVariant> ProductVariants { get; set; }

    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public virtual DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }

    public virtual DbSet<RepairJob> RepairJobs { get; set; }

    public virtual DbSet<RepairJobPart> RepairJobParts { get; set; }

    public virtual DbSet<RepairStatusHistory> RepairStatusHistories { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Sale> Sales { get; set; }

    public virtual DbSet<SaleLine> SaleLines { get; set; }

    public virtual DbSet<SerialItem> SerialItems { get; set; }

    public virtual DbSet<Setting> Settings { get; set; }

    public virtual DbSet<StockMovement> StockMovements { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<SupplierPayment> SupplierPayments { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VCustomerDue> VCustomerDues { get; set; }

    public virtual DbSet<VLowStock> VLowStocks { get; set; }

    public virtual DbSet<VStockBalance> VStockBalances { get; set; }

    public virtual DbSet<VSupplierDue> VSupplierDues { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_0900_ai_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<AccountCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("account_categories");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IsSystem).HasColumnName("is_system");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Type)
                .HasMaxLength(10)
                .HasColumnName("type");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("audit_logs");

            entity.HasIndex(e => e.UserId, "fk_audit_user");

            entity.HasIndex(e => e.CreatedAt, "ix_audit_created");

            entity.HasIndex(e => new { e.EntityName, e.EntityId }, "ix_audit_entity");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.EntityId)
                .HasMaxLength(40)
                .HasColumnName("entity_id");
            entity.Property(e => e.EntityName)
                .HasMaxLength(100)
                .HasColumnName("entity_name");
            entity.Property(e => e.NewValues)
                .HasColumnType("json")
                .HasColumnName("new_values");
            entity.Property(e => e.OldValues)
                .HasColumnType("json")
                .HasColumnName("old_values");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_audit_user");
        });

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("batches");

            entity.HasIndex(e => new { e.VariantId, e.BatchNo }, "uq_batch").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatchNo)
                .HasMaxLength(60)
                .HasColumnName("batch_no");
            entity.Property(e => e.CostPrice)
                .HasPrecision(18, 2)
                .HasColumnName("cost_price");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.Variant).WithMany(p => p.Batches)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_batch_variant");
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("branches");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(300)
                .HasColumnName("address");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
        });

        modelBuilder.Entity<CashDrawerSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("cash_drawer_sessions");

            entity.HasIndex(e => e.BranchId, "fk_cds_branch");

            entity.HasIndex(e => e.UserId, "fk_cds_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.ClosedAt)
                .HasMaxLength(6)
                .HasColumnName("closed_at");
            entity.Property(e => e.ClosingCash)
                .HasPrecision(18, 2)
                .HasColumnName("closing_cash");
            entity.Property(e => e.Difference)
                .HasPrecision(18, 2)
                .HasColumnName("difference");
            entity.Property(e => e.ExpectedCash)
                .HasPrecision(18, 2)
                .HasColumnName("expected_cash");
            entity.Property(e => e.Notes)
                .HasMaxLength(300)
                .HasColumnName("notes");
            entity.Property(e => e.OpenedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("opened_at");
            entity.Property(e => e.OpeningCash)
                .HasPrecision(18, 2)
                .HasColumnName("opening_cash");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Branch).WithMany(p => p.CashDrawerSessions)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cds_branch");

            entity.HasOne(d => d.User).WithMany(p => p.CashDrawerSessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cds_user");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("categories");

            entity.HasIndex(e => e.ParentCategoryId, "fk_cat_parent");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.ParentCategoryId).HasColumnName("parent_category_id");

            entity.HasOne(d => d.ParentCategory).WithMany(p => p.InverseParentCategory)
                .HasForeignKey(d => d.ParentCategoryId)
                .HasConstraintName("fk_cat_parent");
        });

        modelBuilder.Entity<Courier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("couriers");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContactInfo)
                .HasMaxLength(200)
                .HasColumnName("contact_info");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("customers");

            entity.HasIndex(e => e.Name, "ix_customers_name");

            entity.HasIndex(e => e.Phone, "ix_customers_phone");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(300)
                .HasColumnName("address");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.OpeningDue)
                .HasPrecision(18, 2)
                .HasColumnName("opening_due");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
        });

        modelBuilder.Entity<GoodsReceipt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("goods_receipts");

            entity.HasIndex(e => e.BranchId, "fk_gr_branch");

            entity.HasIndex(e => e.PoId, "fk_gr_po");

            entity.HasIndex(e => e.CreatedByUserId, "fk_gr_user");

            entity.HasIndex(e => e.ReceivedDate, "ix_gr_date");

            entity.HasIndex(e => e.SupplierId, "ix_gr_supplier");

            entity.HasIndex(e => e.ReceiptNo, "receipt_no").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.DueAmount)
                .HasPrecision(18, 2)
                .HasColumnName("due_amount");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.PaidAmount)
                .HasPrecision(18, 2)
                .HasColumnName("paid_amount");
            entity.Property(e => e.PoId).HasColumnName("po_id");
            entity.Property(e => e.ReceiptNo)
                .HasMaxLength(30)
                .HasColumnName("receipt_no");
            entity.Property(e => e.ReceivedDate).HasColumnName("received_date");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entity.Property(e => e.SupplierInvoiceNo)
                .HasMaxLength(60)
                .HasColumnName("supplier_invoice_no");
            entity.Property(e => e.Total)
                .HasPrecision(18, 2)
                .HasColumnName("total");

            entity.HasOne(d => d.Branch).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_gr_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_gr_user");

            entity.HasOne(d => d.Po).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.PoId)
                .HasConstraintName("fk_gr_po");

            entity.HasOne(d => d.Supplier).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_gr_supplier");
        });

        modelBuilder.Entity<GoodsReceiptLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("goods_receipt_lines");

            entity.HasIndex(e => e.PoLineId, "fk_grl_poline");

            entity.HasIndex(e => e.ReceiptId, "fk_grl_receipt");

            entity.HasIndex(e => e.VariantId, "fk_grl_variant");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatchNo)
                .HasMaxLength(60)
                .HasColumnName("batch_no");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.PoLineId).HasColumnName("po_line_id");
            entity.Property(e => e.Qty)
                .HasPrecision(18, 3)
                .HasColumnName("qty");
            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.UnitCost)
                .HasPrecision(18, 2)
                .HasColumnName("unit_cost");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.PoLine).WithMany(p => p.GoodsReceiptLines)
                .HasForeignKey(d => d.PoLineId)
                .HasConstraintName("fk_grl_poline");

            entity.HasOne(d => d.Receipt).WithMany(p => p.GoodsReceiptLines)
                .HasForeignKey(d => d.ReceiptId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_grl_receipt");

            entity.HasOne(d => d.Variant).WithMany(p => p.GoodsReceiptLines)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_grl_variant");
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("ledger_entries");

            entity.HasIndex(e => e.BranchId, "fk_le_branch");

            entity.HasIndex(e => e.AccountCategoryId, "fk_le_category");

            entity.HasIndex(e => e.PaymentMethodId, "fk_le_method");

            entity.HasIndex(e => e.CreatedByUserId, "fk_le_user");

            entity.HasIndex(e => e.EntryDate, "ix_le_date");

            entity.HasIndex(e => new { e.SourceType, e.SourceId }, "ix_le_source");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountCategoryId).HasColumnName("account_category_id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.EntryDate).HasColumnName("entry_date");
            entity.Property(e => e.Note)
                .HasMaxLength(300)
                .HasColumnName("note");
            entity.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
            entity.Property(e => e.SourceId).HasColumnName("source_id");
            entity.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Manual'")
                .HasColumnName("source_type");
            entity.Property(e => e.Type)
                .HasMaxLength(10)
                .HasColumnName("type");

            entity.HasOne(d => d.AccountCategory).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.AccountCategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_le_category");

            entity.HasOne(d => d.Branch).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_le_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_le_user");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.PaymentMethodId)
                .HasConstraintName("fk_le_method");
        });

        modelBuilder.Entity<MoneyTransfer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("money_transfers");

            entity.HasIndex(e => e.BranchId, "fk_mt_branch");

            entity.HasIndex(e => e.CustomerId, "fk_mt_customer");

            entity.HasIndex(e => e.UserId, "fk_mt_user");

            entity.HasIndex(e => e.TxnAt, "ix_mt_date");

            entity.HasIndex(e => e.ReferenceNo, "ix_mt_ref");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CommissionEarned)
                .HasPrecision(18, 2)
                .HasColumnName("commission_earned");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasDefaultValueSql("'BDT'")
                .IsFixedLength()
                .HasColumnName("currency");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Direction)
                .HasMaxLength(10)
                .HasColumnName("direction");
            entity.Property(e => e.FeeCharged)
                .HasPrecision(18, 2)
                .HasColumnName("fee_charged");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.PrincipalAmount)
                .HasPrecision(18, 2)
                .HasColumnName("principal_amount");
            entity.Property(e => e.Provider)
                .HasMaxLength(20)
                .HasColumnName("provider");
            entity.Property(e => e.ReceiverName)
                .HasMaxLength(150)
                .HasColumnName("receiver_name");
            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(30)
                .HasColumnName("receiver_phone");
            entity.Property(e => e.ReferenceNo)
                .HasMaxLength(60)
                .HasColumnName("reference_no");
            entity.Property(e => e.SenderName)
                .HasMaxLength(150)
                .HasColumnName("sender_name");
            entity.Property(e => e.SenderPhone)
                .HasMaxLength(30)
                .HasColumnName("sender_phone");
            entity.Property(e => e.TxnAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("txn_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Branch).WithMany(p => p.MoneyTransfers)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mt_branch");

            entity.HasOne(d => d.Customer).WithMany(p => p.MoneyTransfers)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_mt_customer");

            entity.HasOne(d => d.User).WithMany(p => p.MoneyTransfers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mt_user");
        });

        modelBuilder.Entity<ParcelBooking>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("parcel_bookings");

            entity.HasIndex(e => e.BranchId, "fk_pb_branch");

            entity.HasIndex(e => e.CourierId, "fk_pb_courier");

            entity.HasIndex(e => e.CustomerId, "fk_pb_customer");

            entity.HasIndex(e => e.UserId, "fk_pb_user");

            entity.HasIndex(e => e.BookedAt, "ix_pb_date");

            entity.HasIndex(e => e.TrackingNo, "ix_pb_tracking");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BookedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("booked_at");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.ChargeCollected)
                .HasPrecision(18, 2)
                .HasColumnName("charge_collected");
            entity.Property(e => e.CodAmount)
                .HasPrecision(18, 2)
                .HasColumnName("cod_amount");
            entity.Property(e => e.CourierCost)
                .HasPrecision(18, 2)
                .HasColumnName("courier_cost");
            entity.Property(e => e.CourierId).HasColumnName("courier_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Margin)
                .HasPrecision(18, 2)
                .HasComputedColumnSql("`charge_collected` - `courier_cost`", true)
                .HasColumnName("margin");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.ReceiverAddress)
                .HasMaxLength(400)
                .HasColumnName("receiver_address");
            entity.Property(e => e.ReceiverName)
                .HasMaxLength(150)
                .HasColumnName("receiver_name");
            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(30)
                .HasColumnName("receiver_phone");
            entity.Property(e => e.SenderName)
                .HasMaxLength(150)
                .HasColumnName("sender_name");
            entity.Property(e => e.SenderPhone)
                .HasMaxLength(30)
                .HasColumnName("sender_phone");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Booked'")
                .HasColumnName("status");
            entity.Property(e => e.TrackingNo)
                .HasMaxLength(80)
                .HasColumnName("tracking_no");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WeightKg)
                .HasPrecision(10, 3)
                .HasColumnName("weight_kg");

            entity.HasOne(d => d.Branch).WithMany(p => p.ParcelBookings)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pb_branch");

            entity.HasOne(d => d.Courier).WithMany(p => p.ParcelBookings)
                .HasForeignKey(d => d.CourierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pb_courier");

            entity.HasOne(d => d.Customer).WithMany(p => p.ParcelBookings)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_pb_customer");

            entity.HasOne(d => d.User).WithMany(p => p.ParcelBookings)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pb_user");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("payments");

            entity.HasIndex(e => e.BranchId, "fk_pay_branch");

            entity.HasIndex(e => e.PaymentMethodId, "fk_pay_method");

            entity.HasIndex(e => e.CreatedByUserId, "fk_pay_user");

            entity.HasIndex(e => e.CustomerId, "ix_payments_customer");

            entity.HasIndex(e => e.PaidAt, "ix_payments_date");

            entity.HasIndex(e => e.SaleId, "ix_payments_sale");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Direction)
                .HasMaxLength(5)
                .HasDefaultValueSql("'In'")
                .HasColumnName("direction");
            entity.Property(e => e.PaidAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("paid_at");
            entity.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
            entity.Property(e => e.ReferenceNo)
                .HasMaxLength(80)
                .HasColumnName("reference_no");
            entity.Property(e => e.SaleId).HasColumnName("sale_id");

            entity.HasOne(d => d.Branch).WithMany(p => p.Payments)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pay_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.Payments)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_pay_user");

            entity.HasOne(d => d.Customer).WithMany(p => p.Payments)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_pay_customer");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.Payments)
                .HasForeignKey(d => d.PaymentMethodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pay_method");

            entity.HasOne(d => d.Sale).WithMany(p => p.Payments)
                .HasForeignKey(d => d.SaleId)
                .HasConstraintName("fk_pay_sale");
        });

        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("payment_methods");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("products");

            entity.HasIndex(e => e.CategoryId, "fk_products_category");

            entity.HasIndex(e => e.Name, "ix_products_name");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Brand)
                .HasMaxLength(100)
                .HasColumnName("brand");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.ProductType)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Stock'")
                .HasColumnName("product_type");
            entity.Property(e => e.TracksBatch).HasColumnName("tracks_batch");
            entity.Property(e => e.TracksSerial).HasColumnName("tracks_serial");
            entity.Property(e => e.UpdatedAt)
                .HasMaxLength(6)
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("fk_products_category");
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("product_variants");

            entity.HasIndex(e => e.Barcode, "barcode").IsUnique();

            entity.HasIndex(e => e.ProductId, "fk_variant_product");

            entity.HasIndex(e => e.Sku, "sku").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Barcode)
                .HasMaxLength(60)
                .HasColumnName("barcode");
            entity.Property(e => e.CostPrice)
                .HasPrecision(18, 2)
                .HasColumnName("cost_price");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.ReorderLevel)
                .HasPrecision(18, 3)
                .HasColumnName("reorder_level");
            entity.Property(e => e.SellingPrice)
                .HasPrecision(18, 2)
                .HasColumnName("selling_price");
            entity.Property(e => e.Sku)
                .HasMaxLength(60)
                .HasColumnName("sku");
            entity.Property(e => e.VariantName)
                .HasMaxLength(150)
                .HasColumnName("variant_name");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductVariants)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_variant_product");
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("purchase_orders");

            entity.HasIndex(e => e.BranchId, "fk_po_branch");

            entity.HasIndex(e => e.SupplierId, "fk_po_supplier");

            entity.HasIndex(e => e.CreatedByUserId, "fk_po_user");

            entity.HasIndex(e => e.PoNo, "po_no").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.OrderDate).HasColumnName("order_date");
            entity.Property(e => e.PoNo)
                .HasMaxLength(30)
                .HasColumnName("po_no");
            entity.Property(e => e.Status)
                .HasMaxLength(25)
                .HasDefaultValueSql("'Draft'")
                .HasColumnName("status");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entity.Property(e => e.Total)
                .HasPrecision(18, 2)
                .HasColumnName("total");

            entity.HasOne(d => d.Branch).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_po_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_po_user");

            entity.HasOne(d => d.Supplier).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_po_supplier");
        });

        modelBuilder.Entity<PurchaseOrderLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("purchase_order_lines");

            entity.HasIndex(e => e.PoId, "fk_pol_po");

            entity.HasIndex(e => e.VariantId, "fk_pol_variant");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PoId).HasColumnName("po_id");
            entity.Property(e => e.QtyOrdered)
                .HasPrecision(18, 3)
                .HasColumnName("qty_ordered");
            entity.Property(e => e.QtyReceived)
                .HasPrecision(18, 3)
                .HasColumnName("qty_received");
            entity.Property(e => e.UnitCost)
                .HasPrecision(18, 2)
                .HasColumnName("unit_cost");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.Po).WithMany(p => p.PurchaseOrderLines)
                .HasForeignKey(d => d.PoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pol_po");

            entity.HasOne(d => d.Variant).WithMany(p => p.PurchaseOrderLines)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pol_variant");
        });

        modelBuilder.Entity<RepairJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("repair_jobs");

            entity.HasIndex(e => e.BranchId, "fk_rj_branch");

            entity.HasIndex(e => e.SaleId, "fk_rj_sale");

            entity.HasIndex(e => e.CreatedByUserId, "fk_rj_user");

            entity.HasIndex(e => e.CustomerId, "ix_rj_customer");

            entity.HasIndex(e => e.Imei, "ix_rj_imei");

            entity.HasIndex(e => e.Status, "ix_rj_status");

            entity.HasIndex(e => e.JobNo, "job_no").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AdvancePaid)
                .HasPrecision(18, 2)
                .HasColumnName("advance_paid");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DeliveredAt)
                .HasMaxLength(6)
                .HasColumnName("delivered_at");
            entity.Property(e => e.DeviceBrand)
                .HasMaxLength(80)
                .HasColumnName("device_brand");
            entity.Property(e => e.DeviceModel)
                .HasMaxLength(100)
                .HasColumnName("device_model");
            entity.Property(e => e.EstimatedCost)
                .HasPrecision(18, 2)
                .HasColumnName("estimated_cost");
            entity.Property(e => e.FinalCharge)
                .HasPrecision(18, 2)
                .HasColumnName("final_charge");
            entity.Property(e => e.Imei)
                .HasMaxLength(60)
                .HasColumnName("imei");
            entity.Property(e => e.JobNo)
                .HasMaxLength(30)
                .HasColumnName("job_no");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.ProblemDescription)
                .HasMaxLength(1000)
                .HasColumnName("problem_description");
            entity.Property(e => e.ReceivedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("received_at");
            entity.Property(e => e.SaleId).HasColumnName("sale_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Received'")
                .HasColumnName("status");
            entity.Property(e => e.WarrantyDays).HasColumnName("warranty_days");

            entity.HasOne(d => d.Branch).WithMany(p => p.RepairJobs)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rj_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.RepairJobs)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_rj_user");

            entity.HasOne(d => d.Customer).WithMany(p => p.RepairJobs)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rj_customer");

            entity.HasOne(d => d.Sale).WithMany(p => p.RepairJobs)
                .HasForeignKey(d => d.SaleId)
                .HasConstraintName("fk_rj_sale");
        });

        modelBuilder.Entity<RepairJobPart>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("repair_job_parts");

            entity.HasIndex(e => e.JobId, "fk_rjp_job");

            entity.HasIndex(e => e.VariantId, "fk_rjp_variant");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.Qty)
                .HasPrecision(18, 3)
                .HasColumnName("qty");
            entity.Property(e => e.UnitCost)
                .HasPrecision(18, 2)
                .HasColumnName("unit_cost");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 2)
                .HasColumnName("unit_price");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.Job).WithMany(p => p.RepairJobParts)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rjp_job");

            entity.HasOne(d => d.Variant).WithMany(p => p.RepairJobParts)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rjp_variant");
        });

        modelBuilder.Entity<RepairStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("repair_status_history");

            entity.HasIndex(e => e.JobId, "fk_rsh_job");

            entity.HasIndex(e => e.ChangedByUserId, "fk_rsh_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id");
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.Note)
                .HasMaxLength(300)
                .HasColumnName("note");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");

            entity.HasOne(d => d.ChangedByUser).WithMany(p => p.RepairStatusHistories)
                .HasForeignKey(d => d.ChangedByUserId)
                .HasConstraintName("fk_rsh_user");

            entity.HasOne(d => d.Job).WithMany(p => p.RepairStatusHistories)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rsh_job");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("sales");

            entity.HasIndex(e => e.BranchId, "fk_sales_branch");

            entity.HasIndex(e => e.UserId, "fk_sales_user");

            entity.HasIndex(e => e.VoidedByUserId, "fk_sales_voidedby");

            entity.HasIndex(e => e.InvoiceNo, "invoice_no").IsUnique();

            entity.HasIndex(e => e.CustomerId, "ix_sales_customer");

            entity.HasIndex(e => e.SaleDate, "ix_sales_date");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DiscountAmount)
                .HasPrecision(18, 2)
                .HasColumnName("discount_amount");
            entity.Property(e => e.DueAmount)
                .HasPrecision(18, 2)
                .HasColumnName("due_amount");
            entity.Property(e => e.InvoiceNo)
                .HasMaxLength(30)
                .HasColumnName("invoice_no");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.PaidAmount)
                .HasPrecision(18, 2)
                .HasColumnName("paid_amount");
            entity.Property(e => e.SaleDate)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("sale_date");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Completed'")
                .HasColumnName("status");
            entity.Property(e => e.SubTotal)
                .HasPrecision(18, 2)
                .HasColumnName("sub_total");
            entity.Property(e => e.TaxAmount)
                .HasPrecision(18, 2)
                .HasColumnName("tax_amount");
            entity.Property(e => e.Total)
                .HasPrecision(18, 2)
                .HasColumnName("total");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.VoidReason)
                .HasMaxLength(255)
                .HasColumnName("void_reason");
            entity.Property(e => e.VoidedByUserId).HasColumnName("voided_by_user_id");

            entity.HasOne(d => d.Branch).WithMany(p => p.Sales)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sales_branch");

            entity.HasOne(d => d.Customer).WithMany(p => p.Sales)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_sales_customer");

            entity.HasOne(d => d.User).WithMany(p => p.SaleUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sales_user");

            entity.HasOne(d => d.VoidedByUser).WithMany(p => p.SaleVoidedByUsers)
                .HasForeignKey(d => d.VoidedByUserId)
                .HasConstraintName("fk_sales_voidedby");
        });

        modelBuilder.Entity<SaleLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("sale_lines");

            entity.HasIndex(e => e.BatchId, "fk_sl_batch");

            entity.HasIndex(e => e.SerialItemId, "fk_sl_serial");

            entity.HasIndex(e => e.SaleId, "ix_sl_sale");

            entity.HasIndex(e => e.VariantId, "ix_sl_variant");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.Description)
                .HasMaxLength(250)
                .HasColumnName("description");
            entity.Property(e => e.DiscountAmount)
                .HasPrecision(18, 2)
                .HasColumnName("discount_amount");
            entity.Property(e => e.LineTotal)
                .HasPrecision(18, 2)
                .HasColumnName("line_total");
            entity.Property(e => e.Qty)
                .HasPrecision(18, 3)
                .HasColumnName("qty");
            entity.Property(e => e.SaleId).HasColumnName("sale_id");
            entity.Property(e => e.SerialItemId).HasColumnName("serial_item_id");
            entity.Property(e => e.TaxAmount)
                .HasPrecision(18, 2)
                .HasColumnName("tax_amount");
            entity.Property(e => e.UnitCostSnapshot)
                .HasPrecision(18, 2)
                .HasColumnName("unit_cost_snapshot");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 2)
                .HasColumnName("unit_price");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.Batch).WithMany(p => p.SaleLines)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("fk_sl_batch");

            entity.HasOne(d => d.Sale).WithMany(p => p.SaleLines)
                .HasForeignKey(d => d.SaleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sl_sale");

            entity.HasOne(d => d.SerialItem).WithMany(p => p.SaleLines)
                .HasForeignKey(d => d.SerialItemId)
                .HasConstraintName("fk_sl_serial");

            entity.HasOne(d => d.Variant).WithMany(p => p.SaleLines)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sl_variant");
        });

        modelBuilder.Entity<SerialItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("serial_items");

            entity.HasIndex(e => e.GoodsReceiptLineId, "fk_serial_grl");

            entity.HasIndex(e => e.VariantId, "fk_serial_variant");

            entity.HasIndex(e => e.SerialNo, "serial_no").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.GoodsReceiptLineId).HasColumnName("goods_receipt_line_id");
            entity.Property(e => e.SerialNo)
                .HasMaxLength(60)
                .HasColumnName("serial_no");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'InStock'")
                .HasColumnName("status");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.GoodsReceiptLine).WithMany(p => p.SerialItems)
                .HasForeignKey(d => d.GoodsReceiptLineId)
                .HasConstraintName("fk_serial_grl");

            entity.HasOne(d => d.Variant).WithMany(p => p.SerialItems)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_serial_variant");
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.HasKey(e => e.SettingKey).HasName("PRIMARY");

            entity.ToTable("settings");

            entity.Property(e => e.SettingKey)
                .HasMaxLength(100)
                .HasColumnName("setting_key");
            entity.Property(e => e.SettingValue)
                .HasColumnType("text")
                .HasColumnName("setting_value");
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("stock_movements");

            entity.HasIndex(e => e.BatchId, "fk_sm_batch");

            entity.HasIndex(e => e.BranchId, "fk_sm_branch");

            entity.HasIndex(e => e.SerialItemId, "fk_sm_serial");

            entity.HasIndex(e => e.CreatedByUserId, "fk_sm_user");

            entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId }, "ix_stock_reference");

            entity.HasIndex(e => new { e.VariantId, e.CreatedAt }, "ix_stock_variant_date");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.MovementType)
                .HasMaxLength(20)
                .HasColumnName("movement_type");
            entity.Property(e => e.QtyChange)
                .HasPrecision(18, 3)
                .HasColumnName("qty_change");
            entity.Property(e => e.Reason)
                .HasMaxLength(255)
                .HasColumnName("reason");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(30)
                .HasColumnName("reference_type");
            entity.Property(e => e.SerialItemId).HasColumnName("serial_item_id");
            entity.Property(e => e.UnitCost)
                .HasPrecision(18, 2)
                .HasColumnName("unit_cost");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");

            entity.HasOne(d => d.Batch).WithMany(p => p.StockMovements)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("fk_sm_batch");

            entity.HasOne(d => d.Branch).WithMany(p => p.StockMovements)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sm_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.StockMovements)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_sm_user");

            entity.HasOne(d => d.SerialItem).WithMany(p => p.StockMovements)
                .HasForeignKey(d => d.SerialItemId)
                .HasConstraintName("fk_sm_serial");

            entity.HasOne(d => d.Variant).WithMany(p => p.StockMovements)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sm_variant");
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("suppliers");

            entity.HasIndex(e => e.Name, "ix_suppliers_name");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(300)
                .HasColumnName("address");
            entity.Property(e => e.ContactPerson)
                .HasMaxLength(150)
                .HasColumnName("contact_person");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.OpeningDue)
                .HasPrecision(18, 2)
                .HasColumnName("opening_due");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
        });

        modelBuilder.Entity<SupplierPayment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("supplier_payments");

            entity.HasIndex(e => e.BranchId, "fk_sp_branch");

            entity.HasIndex(e => e.PaymentMethodId, "fk_sp_method");

            entity.HasIndex(e => e.ReceiptId, "fk_sp_receipt");

            entity.HasIndex(e => e.CreatedByUserId, "fk_sp_user");

            entity.HasIndex(e => e.SupplierId, "ix_sp_supplier");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.PaidAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("paid_at");
            entity.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.ReferenceNo)
                .HasMaxLength(80)
                .HasColumnName("reference_no");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");

            entity.HasOne(d => d.Branch).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sp_branch");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_sp_user");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.PaymentMethodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sp_method");

            entity.HasOne(d => d.Receipt).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.ReceiptId)
                .HasConstraintName("fk_sp_receipt");

            entity.HasOne(d => d.Supplier).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_sp_supplier");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.BranchId, "fk_users_branch");

            entity.HasIndex(e => e.RoleId, "fk_users_role");

            entity.HasIndex(e => e.Username, "username").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.CreatedAt)
                .HasMaxLength(6)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
                .HasColumnName("created_at");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.Username)
                .HasMaxLength(60)
                .HasColumnName("username");

            entity.HasOne(d => d.Branch).WithMany(p => p.Users)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_users_branch");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_users_role");
        });

        modelBuilder.Entity<VCustomerDue>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_customer_due");

            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DueAmount)
                .HasPrecision(42, 2)
                .HasColumnName("due_amount");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
        });

        modelBuilder.Entity<VLowStock>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_low_stock");

            entity.Property(e => e.ProductName)
                .HasMaxLength(200)
                .HasColumnName("product_name");
            entity.Property(e => e.QtyOnHand)
                .HasPrecision(40, 3)
                .HasColumnName("qty_on_hand");
            entity.Property(e => e.ReorderLevel)
                .HasPrecision(18, 3)
                .HasColumnName("reorder_level");
            entity.Property(e => e.Sku)
                .HasMaxLength(60)
                .HasColumnName("sku");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");
            entity.Property(e => e.VariantName)
                .HasMaxLength(150)
                .HasColumnName("variant_name");
        });

        modelBuilder.Entity<VStockBalance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_stock_balance");

            entity.Property(e => e.QtyOnHand)
                .HasPrecision(40, 3)
                .HasColumnName("qty_on_hand");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");
        });

        modelBuilder.Entity<VSupplierDue>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_supplier_due");

            entity.Property(e => e.DueAmount)
                .HasPrecision(42, 2)
                .HasColumnName("due_amount");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
