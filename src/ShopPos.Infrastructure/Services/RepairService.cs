using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Repairs;
using ShopPos.Application.Sales;
using ShopPos.Application.Shifts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

/// <summary>
/// Repair jobs. How the money is kept without extra database tables:
///  - An advance payment is stored as a normal payment on a hidden "held" sale that belongs to the job
///    (repair_jobs.sale_id). That sale never shows in the held-bills list.
///  - When the device is delivered, that same sale becomes the final, completed bill (labour + parts),
///    so the advance counts as already paid.
///  - Parts leave stock when they are added to the job (movement type RepairPart), so the final bill does
///    not take them out of stock a second time.
/// </summary>
public class RepairService : IRepairService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public RepairService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    // ============================================================ reading

    public async Task<List<RepairListItem>> ListAsync(string? search, RepairFilter filter)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var q = db.RepairJobs.AsNoTracking().Where(j => j.BranchId == branchId);

        q = filter switch
        {
            RepairFilter.Open => q.Where(j => j.Status == "Received" || j.Status == "Repairing" || j.Status == "Ready"),
            RepairFilter.Delivered => q.Where(j => j.Status == "Delivered"),
            RepairFilter.Cancelled => q.Where(j => j.Status == "Cancelled"),
            _ => q
        };

        var t = search?.Trim();
        if (!string.IsNullOrEmpty(t))
        {
            q = q.Where(j =>
                j.JobNo.Contains(t)
                || (j.Imei != null && j.Imei.Contains(t))
                || (j.DeviceBrand != null && j.DeviceBrand.Contains(t))
                || (j.DeviceModel != null && j.DeviceModel.Contains(t))
                || db.Customers.Any(c => c.Id == j.CustomerId
                                         && (c.Name.Contains(t) || (c.Phone != null && c.Phone.Contains(t)))));
        }

        var rows = await q.OrderByDescending(j => j.ReceivedAt)
            .Take(300)
            .Select(j => new
            {
                j.Id, j.JobNo, j.ReceivedAt, j.DeviceBrand, j.DeviceModel, j.Imei, j.Status,
                j.EstimatedCost, j.AdvancePaid,
                CustomerName = db.Customers.Where(c => c.Id == j.CustomerId).Select(c => c.Name).FirstOrDefault(),
                CustomerPhone = db.Customers.Where(c => c.Id == j.CustomerId).Select(c => c.Phone).FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(r => new RepairListItem(
            r.Id, r.JobNo, r.ReceivedAt, r.CustomerName ?? "", r.CustomerPhone,
            r.DeviceBrand, r.DeviceModel, r.Imei, r.Status, r.EstimatedCost, r.AdvancePaid)).ToList();
    }

    public async Task<RepairDetail?> GetAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var job = await db.RepairJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null) return null;

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == job.CustomerId);

        var partRows = await db.RepairJobParts.AsNoTracking()
            .Where(p => p.JobId == id).OrderBy(p => p.Id).ToListAsync();

        var variantIds = partRows.Select(p => p.VariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        var productIds = variants.Values.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var parts = partRows.Select(p =>
        {
            var name = "(unknown part)";
            if (variants.TryGetValue(p.VariantId, out var v) && products.TryGetValue(v.ProductId, out var pr))
                name = string.IsNullOrWhiteSpace(v.VariantName) ? pr.Name : $"{pr.Name} - {v.VariantName}";
            return new RepairPartRow(p.Id, p.VariantId, name, p.Qty, p.UnitPrice);
        }).ToList();

        var historyRows = await db.Set<RepairStatusHistory>().AsNoTracking()
            .Where(h => h.JobId == id).OrderBy(h => h.ChangedAt).ToListAsync();
        var userIds = historyRows.Where(h => h.ChangedByUserId != null).Select(h => h.ChangedByUserId!.Value).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        var history = historyRows.Select(h => new RepairHistoryRow(
            h.Status, h.Note,
            h.ChangedByUserId is Guid uid && userNames.TryGetValue(uid, out var n) ? n : null,
            h.ChangedAt)).ToList();

        return new RepairDetail(
            job.Id, job.JobNo, job.CustomerId, customer?.Name ?? "", customer?.Phone,
            job.DeviceBrand, job.DeviceModel, job.Imei, job.ProblemDescription, job.EstimatedCost, job.AdvancePaid,
            job.Status, job.ReceivedAt, job.DeliveredAt, job.WarrantyDays, job.FinalCharge,
            job.Notes, job.SaleId, parts, history);
    }

    // ========================================================== job changes

    public async Task<Guid> CreateAsync(RepairIntake intake)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");

        var brand = Clean(intake.Brand);
        var model = Clean(intake.Model);
        var problem = Clean(intake.Problem);

        if (brand is null && model is null)
            throw new BusinessRuleException("Enter the device (brand or model).");
        if (problem is null)
            throw new BusinessRuleException("Describe the problem.");
        if (intake.Estimate < 0)
            throw new BusinessRuleException("The estimate cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();

        if (!await db.Customers.AnyAsync(c => c.Id == intake.CustomerId && c.IsActive == true))
            throw new BusinessRuleException("Choose the customer first.");

        var job = new RepairJob
        {
            Id = Guid.CreateVersion7(),
            JobNo = await NextJobNoAsync(db),
            BranchId = _user.BranchId,
            CustomerId = intake.CustomerId,
            DeviceBrand = brand,
            DeviceModel = model,
            Imei = Clean(intake.Imei),
            ProblemDescription = problem,
            EstimatedCost = Money(intake.Estimate),
            AdvancePaid = 0,
            Status = RepairStatus.Received,
            ReceivedAt = DateTime.UtcNow,
            WarrantyDays = 0,
            Notes = Clean(intake.Notes),
            CreatedByUserId = _user.UserId
        };
        db.RepairJobs.Add(job);

        AddHistory(db, job.Id, RepairStatus.Received, "Device received");
        AddAudit(db, "RepairCreated", job.Id);

        await db.SaveChangesAsync();
        return job.Id;
    }

    public async Task UpdateAsync(RepairEdit e)
    {
        var brand = Clean(e.Brand);
        var model = Clean(e.Model);
        var problem = Clean(e.Problem);

        if (brand is null && model is null)
            throw new BusinessRuleException("Enter the device (brand or model).");
        if (problem is null)
            throw new BusinessRuleException("Describe the problem.");
        if (e.Estimate < 0)
            throw new BusinessRuleException("The estimate cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var job = await LoadOpenJobAsync(db, e.Id);

        job.DeviceBrand = brand;
        job.DeviceModel = model;
        job.Imei = Clean(e.Imei);
        job.ProblemDescription = problem;
        job.EstimatedCost = Money(e.Estimate);
        job.Notes = Clean(e.Notes);

        AddAudit(db, "RepairUpdated", job.Id);
        await db.SaveChangesAsync();
    }

    public async Task ChangeStatusAsync(Guid jobId, string newStatus, string? note)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var job = await LoadOpenJobAsync(db, jobId);

        var allowed =
            (job.Status == RepairStatus.Received && newStatus == RepairStatus.Repairing) ||
            (job.Status == RepairStatus.Repairing && newStatus == RepairStatus.Ready) ||
            (job.Status == RepairStatus.Ready && newStatus == RepairStatus.Repairing);

        if (!allowed)
            throw new BusinessRuleException($"A job that is '{job.Status}' cannot be moved to '{newStatus}'.");

        job.Status = newStatus;
        AddHistory(db, job.Id, newStatus, Clean(note));
        AddAudit(db, "RepairStatus", job.Id);

        await db.SaveChangesAsync();
    }

    // ================================================================ parts

    public async Task AddPartAsync(Guid jobId, Guid variantId, decimal qty)
    {
        if (qty <= 0)
            throw new BusinessRuleException("Quantity must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var job = await LoadOpenJobAsync(db, jobId);

        var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId && v.IsActive == true)
                      ?? throw new BusinessRuleException("This item is not available.");
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == variant.ProductId);

        if (product.ProductType != "Stock")
            throw new BusinessRuleException("Only stock items can be used as spare parts.");
        if (product.TracksSerial || product.TracksBatch)
            throw new BusinessRuleException("Parts that are tracked by IMEI or batch cannot be added to a job yet.");

        var branchId = _user.BranchId;
        var onHand = await db.StockMovements
            .Where(m => m.VariantId == variantId && m.BranchId == branchId)
            .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;

        if (!await AllowNegativeAsync(db) && onHand < qty)
            throw new BusinessRuleException($"Not enough stock. Available: {onHand:0.###}.");

        var part = await db.RepairJobParts.FirstOrDefaultAsync(p => p.JobId == jobId && p.VariantId == variantId);
        if (part is null)
        {
            db.RepairJobParts.Add(new RepairJobPart
            {
                Id = Guid.CreateVersion7(),
                JobId = jobId,
                VariantId = variantId,
                Qty = qty,
                UnitPrice = variant.SellingPrice,
                UnitCost = variant.CostPrice
            });
        }
        else
        {
            part.Qty += qty;
        }

        db.StockMovements.Add(NewMovement(branchId, variantId, -qty, "RepairPart", job, variant.CostPrice,
            $"Used in repair {job.JobNo}"));

        AddAudit(db, "RepairPartAdded", job.Id);
        await db.SaveChangesAsync();
    }

    public async Task RemovePartAsync(Guid partId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var part = await db.RepairJobParts.FirstOrDefaultAsync(p => p.Id == partId)
                   ?? throw new BusinessRuleException("This part is no longer on the job.");
        var job = await LoadOpenJobAsync(db, part.JobId);

        db.StockMovements.Add(NewMovement(_user.BranchId, part.VariantId, part.Qty, "Return", job, part.UnitCost,
            $"Removed from repair {job.JobNo}"));
        db.RepairJobParts.Remove(part);

        AddAudit(db, "RepairPartRemoved", job.Id);
        await db.SaveChangesAsync();
    }

    // ============================================================== advance

    public async Task TakeAdvanceAsync(AdvanceRequest r)
    {
        var amount = Money(r.Amount);
        if (amount <= 0)
            throw new BusinessRuleException("Enter an amount greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var job = await LoadOpenJobAsync(db, r.JobId);

        var method = await db.PaymentMethods.AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == r.PaymentMethodId && m.IsActive == true)
                     ?? throw new BusinessRuleException("This payment method is not available.");

        await RequireShiftForCashAsync(IsCash(method.Name), "this cash goes into your drawer. Open it on the Billing screen");

        var sale = await GetOrCreateRepairSaleAsync(db, job);

        var reference = Clean(r.Reference);
        db.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            BranchId = _user.BranchId,
            SaleId = sale.Id,
            CustomerId = job.CustomerId,
            PaymentMethodId = method.Id,
            Direction = "In",
            Amount = amount,
            ReferenceNo = reference is null ? $"Advance {job.JobNo}" : $"Advance {job.JobNo} - {reference}",
            PaidAt = DateTime.UtcNow,
            CreatedByUserId = _user.UserId
        });

        job.AdvancePaid += amount;
        AddHistory(db, job.Id, job.Status, $"Advance payment {amount:N2} ({method.Name})");
        AddAudit(db, "RepairAdvance", job.Id);

        await db.SaveChangesAsync();
    }

    // ============================================================= delivery

    public async Task<CompletedSale> DeliverAsync(DeliverRequest r)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");
        if (r.Labour < 0)
            throw new BusinessRuleException("The labour charge cannot be negative.");
        if (r.WarrantyDays < 0 || r.WarrantyDays > 3650)
            throw new BusinessRuleException("Warranty days must be between 0 and 3650.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var job = await db.RepairJobs.FirstOrDefaultAsync(j => j.Id == r.JobId && j.BranchId == branchId)
                  ?? throw new BusinessRuleException("This repair job no longer exists.");
        if (job.Status != RepairStatus.Ready)
            throw new BusinessRuleException("Mark the job as Ready before handing the device over.");

        var parts = await db.RepairJobParts.Where(p => p.JobId == job.Id).OrderBy(p => p.Id).ToListAsync();

        var labour = Money(r.Labour);
        var partsTotal = parts.Sum(p => Money(p.Qty * p.UnitPrice));
        var total = labour + partsTotal;

        // ---- what was already paid in advance
        Sale? sale = null;
        if (job.SaleId is Guid existingSaleId)
            sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == existingSaleId);

        var advance = sale is null
            ? 0m
            : await db.Payments.Where(p => p.SaleId == sale.Id && p.Direction == "In")
                  .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.IsActive == true).ToDictionaryAsync(m => m.Id);
        foreach (var p in r.Payments)
            if (!methods.ContainsKey(p.PaymentMethodId))
                throw new BusinessRuleException("One of the payment methods is no longer available.");

        var appliedAdvance = Math.Min(advance, total);
        var refund = advance - appliedAdvance;                  // advance was more than the final bill
        var balance = total - appliedAdvance;

        var summary = PaymentCalculator.Calculate(balance, r.Payments.Select(p =>
            new PaymentInput(IsCash(methods[p.PaymentMethodId].Name), p.Amount)));
        if (summary.Error is not null)
            throw new BusinessRuleException(summary.Error);

        // cash in or cash out means the cashier's drawer must be open
        await RequireShiftForCashAsync(summary.CashApplied > 0 || refund > 0,
            "the cash goes into (or out of) your drawer. Open it on the Billing screen");

        Payment? refundPayment = null;
        if (refund > 0)
        {
            var cash = methods.Values.FirstOrDefault(m => IsCash(m.Name))
                       ?? throw new BusinessRuleException("A cash payment method is needed to refund the extra advance.");
            refundPayment = new Payment
            {
                Id = Guid.CreateVersion7(),
                BranchId = branchId,
                SaleId = sale!.Id,
                CustomerId = job.CustomerId,
                PaymentMethodId = cash.Id,
                Direction = "Out",
                Amount = refund,
                ReferenceNo = $"Advance refund {job.JobNo}",
                PaidAt = DateTime.UtcNow,
                CreatedByUserId = _user.UserId
            };
        }

        // ---- the final bill: reuse the hidden sale if there is one
        var now = DateTime.UtcNow;
        var invoiceNo = await NextInvoiceNoAsync(db);

        if (sale is null)
        {
            sale = new Sale { Id = Guid.CreateVersion7(), BranchId = branchId, CustomerId = job.CustomerId };
            db.Sales.Add(sale);
        }

        var paidTotal = appliedAdvance + summary.Paid;

        sale.InvoiceNo = invoiceNo;
        sale.UserId = _user.UserId;
        sale.SaleDate = now;
        sale.SubTotal = total;
        sale.DiscountAmount = 0;
        sale.TaxAmount = 0;
        sale.Total = total;
        sale.PaidAmount = paidTotal;
        sale.DueAmount = total - paidTotal;
        sale.Status = "Completed";
        sale.Notes = $"Repair {job.JobNo}";

        var device = $"{job.DeviceBrand} {job.DeviceModel}".Trim();
        var warrantyText = r.WarrantyDays > 0 ? $", warranty {r.WarrantyDays} days" : "";

        if (labour > 0)
        {
            var service = await EnsureRepairServiceVariantAsync(db);
            AddLine(db, sale, service.Id, $"Repair {job.JobNo}: {device} - labour{warrantyText}", 1, labour, 0, labour);
        }

        foreach (var part in parts)
        {
            var name = await DescribeVariantAsync(db, part.VariantId);
            AddLine(db, sale, part.VariantId, name, part.Qty, part.UnitPrice, part.UnitCost, Money(part.Qty * part.UnitPrice));
            // no stock movement here: the part left stock when it was added to the job
        }

        // ---- payments made now
        var cashLeft = summary.CashApplied;
        foreach (var p in r.Payments)
        {
            var amount = Money(p.Amount);
            if (amount <= 0) continue;

            var applied = amount;
            if (IsCash(methods[p.PaymentMethodId].Name))
            {
                applied = Math.Min(amount, cashLeft);
                cashLeft -= applied;
                if (applied <= 0) continue;
            }

            db.Payments.Add(new Payment
            {
                Id = Guid.CreateVersion7(),
                BranchId = branchId,
                SaleId = sale.Id,
                CustomerId = job.CustomerId,
                PaymentMethodId = p.PaymentMethodId,
                Direction = "In",
                Amount = applied,
                ReferenceNo = string.IsNullOrWhiteSpace(p.Reference) ? $"Repair {job.JobNo}" : p.Reference.Trim(),
                PaidAt = now,
                CreatedByUserId = _user.UserId
            });
        }

        if (refundPayment is not null)
            db.Payments.Add(refundPayment);

        // ---- the job
        job.Status = RepairStatus.Delivered;
        job.DeliveredAt = now;
        job.FinalCharge = labour;
        job.WarrantyDays = r.WarrantyDays;
        job.SaleId = sale.Id;

        AddHistory(db, job.Id, RepairStatus.Delivered,
            $"Billed {invoiceNo}, total {total:N2}" + (r.WarrantyDays > 0 ? $", warranty {r.WarrantyDays} days" : ""));
        AddAudit(db, "RepairDelivered", job.Id);

        await db.SaveChangesAsync();

        return new CompletedSale(sale.Id, invoiceNo, total, paidTotal, summary.Change, total - paidTotal);
    }

    // =============================================================== cancel

    public async Task CancelAsync(Guid jobId, string reason)
    {
        var why = Clean(reason) ?? throw new BusinessRuleException("Type the reason for cancelling this job.");

        await using var db = await _factory.CreateDbContextAsync();
        var job = await LoadOpenJobAsync(db, jobId);

        if (await db.RepairJobParts.AnyAsync(p => p.JobId == jobId))
            throw new BusinessRuleException("Remove the spare parts from the job first (they go back into stock), then cancel it.");

        if (job.SaleId is Guid saleId)
        {
            var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == saleId);
            if (sale is not null)
            {
                var payments = await db.Payments.AsNoTracking()
                    .Where(p => p.SaleId == sale.Id && p.Direction == "In").ToListAsync();

                if (payments.Count > 0)
                {
                    if (!_user.IsInRole("Owner", "Manager"))
                        throw new BusinessRuleException(
                            "Only an Owner or Manager can cancel a job that has an advance payment (it has to be refunded).");

                    var methods = await db.PaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id);
                    var cashRefund = payments.Where(p => IsCash(methods[p.PaymentMethodId].Name)).Sum(p => p.Amount);
                    await RequireShiftForCashAsync(cashRefund > 0, "the cash refund is paid out of your drawer");

                    foreach (var p in payments)
                    {
                        db.Payments.Add(new Payment
                        {
                            Id = Guid.CreateVersion7(),
                            BranchId = p.BranchId,
                            SaleId = sale.Id,
                            CustomerId = p.CustomerId,
                            PaymentMethodId = p.PaymentMethodId,
                            Direction = "Out",
                            Amount = p.Amount,
                            ReferenceNo = $"Refund {job.JobNo}",
                            PaidAt = DateTime.UtcNow,
                            CreatedByUserId = _user.UserId
                        });
                    }
                }

                var voidReason = $"Repair job cancelled: {why}";
                sale.Status = "Voided";
                sale.VoidReason = voidReason.Length > 255 ? voidReason[..255] : voidReason;
                sale.VoidedByUserId = _user.UserId;
            }
        }

        job.Status = RepairStatus.Cancelled;
        AddHistory(db, job.Id, RepairStatus.Cancelled, why);
        AddAudit(db, "RepairCancelled", job.Id);

        await db.SaveChangesAsync();
    }

    // ================================================================ helpers

    private async Task<RepairJob> LoadOpenJobAsync(ShopPosDbContext db, Guid jobId)
    {
        var branchId = _user.BranchId;
        var job = await db.RepairJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.BranchId == branchId)
                  ?? throw new BusinessRuleException("This repair job no longer exists.");

        if (!RepairStatus.IsOpen(job.Status))
            throw new BusinessRuleException($"This job is already {job.Status.ToLowerInvariant()} and cannot be changed.");

        return job;
    }

    /// <summary>The hidden sale that carries the advance payments (created when the first advance is taken).</summary>
    private async Task<Sale> GetOrCreateRepairSaleAsync(ShopPosDbContext db, RepairJob job)
    {
        if (job.SaleId is Guid existingId)
        {
            var existing = await db.Sales.FirstOrDefaultAsync(s => s.Id == existingId);
            if (existing is not null) return existing;
        }

        var sale = new Sale
        {
            Id = Guid.CreateVersion7(),
            InvoiceNo = null,
            BranchId = _user.BranchId,
            CustomerId = job.CustomerId,
            UserId = _user.UserId,
            SaleDate = DateTime.UtcNow,
            SubTotal = 0,
            DiscountAmount = 0,
            TaxAmount = 0,
            Total = 0,
            PaidAmount = 0,
            DueAmount = 0,
            Status = "Held",
            Notes = $"Repair {job.JobNo}"
        };
        db.Sales.Add(sale);
        job.SaleId = sale.Id;
        return sale;
    }

    /// <summary>The "Repair service" item that the labour line of a final bill points to (created on first use).</summary>
    private static async Task<ProductVariant> EnsureRepairServiceVariantAsync(ShopPosDbContext db)
    {
        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Sku == "SRV-REPAIR");
        if (variant is not null) return variant;

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Name = "Repair service",
            ProductType = "Service",
            TracksSerial = false,
            TracksBatch = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        variant = new ProductVariant
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Sku = "SRV-REPAIR",
            CostPrice = 0,
            SellingPrice = 0,
            ReorderLevel = 0,
            IsActive = true
        };

        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        return variant;
    }

    private static async Task<string> DescribeVariantAsync(ShopPosDbContext db, Guid variantId)
    {
        var v = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == variantId);
        if (v is null) return "Part";
        var p = await db.Products.AsNoTracking().FirstAsync(x => x.Id == v.ProductId);
        return string.IsNullOrWhiteSpace(v.VariantName) ? p.Name : $"{p.Name} - {v.VariantName}";
    }

    private static void AddLine(ShopPosDbContext db, Sale sale, Guid variantId, string description,
        decimal qty, decimal unitPrice, decimal unitCost, decimal lineTotal)
    {
        db.SaleLines.Add(new SaleLine
        {
            // ids grow with every line so the receipt shows them in this order
            Id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(db.SaleLines.Local.Count)),
            SaleId = sale.Id,
            VariantId = variantId,
            Description = description.Length > 250 ? description[..250] : description,
            Qty = qty,
            UnitPrice = unitPrice,
            UnitCostSnapshot = unitCost,
            DiscountAmount = 0,
            TaxAmount = 0,
            LineTotal = lineTotal
        });
    }

    private StockMovement NewMovement(Guid branchId, Guid variantId, decimal change, string type,
        RepairJob job, decimal unitCost, string reason) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = branchId,
        VariantId = variantId,
        QtyChange = change,
        MovementType = type,
        ReferenceType = "RepairJob",
        ReferenceId = job.Id,
        Reason = reason,
        UnitCost = unitCost,
        CreatedByUserId = _user.UserId,
        CreatedAt = DateTime.UtcNow
    };

    private void AddHistory(ShopPosDbContext db, Guid jobId, string status, string? note)
    {
        db.Set<RepairStatusHistory>().Add(new RepairStatusHistory
        {
            Id = Guid.CreateVersion7(),
            JobId = jobId,
            Status = status,
            Note = note,
            ChangedByUserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            ChangedAt = DateTime.UtcNow
        });
    }

    private void AddAudit(ShopPosDbContext db, string action, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = "RepairJob",
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task RequireShiftForCashAsync(bool cashInvolved, string why)
    {
        if (cashInvolved && await _shifts.IsShiftRequiredAsync() && await _shifts.GetOpenShiftAsync() is null)
            throw new BusinessRuleException($"Open your shift (cash drawer) first: {why}.");
    }

    private static bool IsCash(string methodName) =>
        string.Equals(methodName, "Cash", StringComparison.OrdinalIgnoreCase);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static async Task<bool> AllowNegativeAsync(ShopPosDbContext db)
    {
        var value = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "stock.allow_negative"))?.SettingValue;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>RJ-000001, RJ-000002 ... (the counter is saved together with the job).</summary>
    private static async Task<string> NextJobNoAsync(ShopPosDbContext db)
    {
        var counter = await db.Settings.FirstOrDefaultAsync(s => s.SettingKey == "repair.next");
        if (counter is null)
        {
            counter = new Setting { SettingKey = "repair.next", SettingValue = "1" };
            db.Settings.Add(counter);
        }

        var number = long.TryParse(counter.SettingValue, out var n) ? n : 1;
        counter.SettingValue = (number + 1).ToString();
        return $"RJ-{number:D6}";
    }

    /// <summary>The same invoice numbers as normal sales (INV-000001 ...).</summary>
    private static async Task<string> NextInvoiceNoAsync(ShopPosDbContext db)
    {
        var prefix = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "invoice.prefix"))?.SettingValue;
        if (string.IsNullOrWhiteSpace(prefix)) prefix = "INV";

        var counter = await db.Settings.FirstOrDefaultAsync(s => s.SettingKey == "invoice.next");
        if (counter is null)
        {
            counter = new Setting { SettingKey = "invoice.next", SettingValue = "1" };
            db.Settings.Add(counter);
        }

        var number = long.TryParse(counter.SettingValue, out var n) ? n : 1;
        counter.SettingValue = (number + 1).ToString();
        return $"{prefix.Trim()}-{number:D6}";
    }
}
