using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Shifts;
using ShopPos.Application.Suppliers;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

/// <summary>
/// Suppliers and what you owe them. It works like customer dues, the other way round:
/// due = old balance + unpaid purchases (goods receipts) - payments that were not tied to one purchase.
/// Every payment is also written to the payments table (as money going out, without a customer) so that the
/// cash drawer of the shift stays right.
/// </summary>
public class SupplierService : ISupplierService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public SupplierService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    public async Task<List<SupplierOption>> GetOptionsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive == true)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierOption(s.Id, s.Name))
            .ToListAsync();
    }

    public async Task<List<SupplierRow>> ListAsync(string? search, bool owingOnly, bool includeInactive)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(s => s.IsActive == true);

        var t = search?.Trim();
        if (!string.IsNullOrEmpty(t))
            q = q.Where(s => s.Name.Contains(t)
                             || (s.Phone != null && s.Phone.Contains(t))
                             || (s.ContactPerson != null && s.ContactPerson.Contains(t)));

        var rows = await q.OrderBy(s => s.Name)
            .Take(500)
            .Select(s => new
            {
                s.Id, s.Name, s.ContactPerson, s.Phone, s.Address, s.IsActive, s.OpeningDue,
                ReceiptsDue = db.GoodsReceipts.Where(g => g.SupplierId == s.Id).Sum(g => (decimal?)g.DueAmount) ?? 0m,
                Unallocated = db.SupplierPayments.Where(p => p.SupplierId == s.Id && p.ReceiptId == null)
                    .Sum(p => (decimal?)p.Amount) ?? 0m
            })
            .ToListAsync();

        var result = rows.Select(r => new SupplierRow(
            r.Id, r.Name, r.ContactPerson, r.Phone, r.Address, r.OpeningDue + r.ReceiptsDue - r.Unallocated, r.IsActive == true)).ToList();

        return owingOnly ? result.Where(r => r.Due > 0).OrderByDescending(r => r.Due).ToList() : result;
    }

    public async Task<SupplierDetail?> GetAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;

        var due = await GetDueAsync(db, s);
        return new SupplierDetail(s.Id, s.Name, s.ContactPerson, s.Phone, s.Address, s.OpeningDue, s.IsActive == true, due);
    }

    public async Task<Guid> SaveAsync(SupplierEdit e)
    {
        var name = Clean(e.Name) ?? throw new BusinessRuleException("Supplier name is required.");
        if (e.OpeningDue < 0)
            throw new BusinessRuleException("The old balance cannot be negative.");

        var opening = Money(e.OpeningDue);
        var privileged = _user.IsInRole("Owner", "Manager");

        await using var db = await _factory.CreateDbContextAsync();

        if (await db.Suppliers.AnyAsync(s => s.Id != e.Id && s.Name == name))
            throw new BusinessRuleException("Another supplier already has this name.");

        Supplier entity;
        var isNew = e.Id == Guid.Empty;

        if (isNew)
        {
            if (opening != 0 && !privileged)
                throw new BusinessRuleException("Only an Owner or Manager can enter an old balance.");

            entity = new Supplier { Id = Guid.CreateVersion7(), CreatedAt = DateTime.UtcNow };
            db.Suppliers.Add(entity);
        }
        else
        {
            entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == e.Id)
                     ?? throw new BusinessRuleException("This supplier no longer exists.");

            var openingChanged = entity.OpeningDue != opening;
            var activeChanged = entity.IsActive != e.IsActive;

            if ((openingChanged || activeChanged) && !privileged)
                throw new BusinessRuleException("Only an Owner or Manager can change the old balance or deactivate a supplier.");

            if (openingChanged)
            {
                var paid = await UnallocatedAsync(db, entity.Id);
                if (opening < paid)
                    throw new BusinessRuleException(
                        $"The old balance cannot be less than what was already paid against it ({paid:N2}).");
            }

            if (activeChanged && !e.IsActive && await GetDueAsync(db, entity) > 0)
                throw new BusinessRuleException("You still owe this supplier money, so it cannot be deactivated.");
        }

        entity.Name = name;
        entity.ContactPerson = Clean(e.ContactPerson);
        entity.Phone = Clean(e.Phone);
        entity.Address = Clean(e.Address);
        entity.OpeningDue = opening;
        entity.IsActive = e.IsActive;

        AddAudit(db, isNew ? "Create" : "Update", entity.Id);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<List<SupplierPurchaseRow>> GetPurchasesAsync(Guid supplierId, int take = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var rows = await db.GoodsReceipts.AsNoTracking()
            .Where(g => g.SupplierId == supplierId)
            .OrderByDescending(g => g.ReceivedDate).ThenByDescending(g => g.ReceiptNo)
            .Take(take)
            .Select(g => new
            {
                g.Id, g.ReceiptNo, g.ReceivedDate, g.SupplierInvoiceNo, g.Total, g.PaidAmount, g.DueAmount,
                PoNo = db.PurchaseOrders.Where(p => p.Id == g.PoId).Select(p => p.PoNo).FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(r => new SupplierPurchaseRow(
            r.Id, r.ReceiptNo, r.ReceivedDate, r.SupplierInvoiceNo, r.PoNo, r.Total, r.PaidAmount, r.DueAmount)).ToList();
    }

    public async Task<List<SupplierPaymentRow>> GetPaymentsAsync(Guid supplierId, int take = 200)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var rows = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.SupplierId == supplierId)
            .OrderByDescending(p => p.PaidAt)
            .Take(take)
            .Select(p => new
            {
                p.PaidAt, p.Amount, p.ReferenceNo,
                Method = db.PaymentMethods.Where(m => m.Id == p.PaymentMethodId).Select(m => m.Name).FirstOrDefault(),
                ReceiptNo = db.GoodsReceipts.Where(g => g.Id == p.ReceiptId).Select(g => g.ReceiptNo).FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(r => new SupplierPaymentRow(r.PaidAt, r.ReceiptNo, r.Method ?? "", r.Amount, r.ReferenceNo)).ToList();
    }

    public async Task<SupplierDueSummary> GetDueSummaryAsync(Guid supplierId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId)
                       ?? throw new BusinessRuleException("This supplier no longer exists.");

        var openingRemaining = Math.Max(0m, supplier.OpeningDue - await UnallocatedAsync(db, supplierId));

        var open = await db.GoodsReceipts.AsNoTracking()
            .Where(g => g.SupplierId == supplierId && g.DueAmount > 0)
            .OrderBy(g => g.ReceivedDate).ThenBy(g => g.ReceiptNo)
            .Select(g => new OpenReceiptRow(g.Id, g.ReceiptNo, g.ReceivedDate, g.SupplierInvoiceNo, g.Total, g.DueAmount))
            .ToListAsync();

        return new SupplierDueSummary(openingRemaining, open, openingRemaining + open.Sum(o => o.Due));
    }

    public async Task<SupplierPayResult> PayAsync(PaySupplierRequest r)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");

        var amount = Money(r.Amount);
        if (amount <= 0)
            throw new BusinessRuleException("Enter an amount greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.SupplierId)
                       ?? throw new BusinessRuleException("This supplier no longer exists.");
        var method = await db.PaymentMethods.AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == r.PaymentMethodId && m.IsActive == true)
                     ?? throw new BusinessRuleException("This payment method is not available.");

        if (string.Equals(method.Name, "Cash", StringComparison.OrdinalIgnoreCase) &&
            await _shifts.IsShiftRequiredAsync() && await _shifts.GetOpenShiftAsync() is null)
            throw new BusinessRuleException("Open your shift (cash drawer) first: this cash comes out of your drawer.");

        var receipts = await db.GoodsReceipts
            .Where(g => g.SupplierId == r.SupplierId && g.DueAmount > 0)
            .OrderBy(g => g.ReceivedDate).ThenBy(g => g.ReceiptNo)
            .ToListAsync();

        var openingRemaining = Math.Max(0m, supplier.OpeningDue - await UnallocatedAsync(db, r.SupplierId));
        var totalDue = openingRemaining + receipts.Sum(g => g.DueAmount);

        if (totalDue <= 0)
            throw new BusinessRuleException("You do not owe this supplier anything.");
        if (amount > totalDue)
            throw new BusinessRuleException($"You owe this supplier only {totalDue:N2}.");

        var reference = Clean(r.Reference);
        var now = DateTime.UtcNow;
        var left = amount;

        foreach (var receipt in receipts)
        {
            if (left <= 0) break;

            var part = Math.Min(left, receipt.DueAmount);
            receipt.PaidAmount += part;
            receipt.DueAmount -= part;
            left -= part;

            AddPayment(db, supplier.Id, receipt.Id, method.Id, part, reference, now);
        }

        if (left > 0)                                   // what is left pays off the old balance
            AddPayment(db, supplier.Id, null, method.Id, left, reference, now);

        AddAudit(db, "SupplierPaid", supplier.Id);
        await db.SaveChangesAsync();

        return new SupplierPayResult(amount, totalDue - amount);
    }

    // ================================================================ helpers

    private void AddPayment(ShopPosDbContext db, Guid supplierId, Guid? receiptId, int methodId,
        decimal amount, string? reference, DateTime now)
    {
        var payment = new SupplierPayment
        {
            Id = Guid.CreateVersion7(),
            BranchId = _user.BranchId,
            SupplierId = supplierId,
            ReceiptId = receiptId,
            PaymentMethodId = methodId,
            Amount = amount,
            ReferenceNo = reference,
            PaidAt = now,
            CreatedByUserId = _user.UserId
        };
        db.SupplierPayments.Add(payment);

        // the same money, as seen by the cash drawer / shift totals
        db.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            BranchId = _user.BranchId,
            SaleId = null,
            CustomerId = null,
            PaymentMethodId = methodId,
            Direction = "Out",
            Amount = amount,
            ReferenceNo = $"SUPPAY:{payment.Id}",
            PaidAt = now,
            CreatedByUserId = _user.UserId
        });
    }

    private static async Task<decimal> UnallocatedAsync(ShopPosDbContext db, Guid supplierId) =>
        await db.SupplierPayments.Where(p => p.SupplierId == supplierId && p.ReceiptId == null)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

    private static async Task<decimal> GetDueAsync(ShopPosDbContext db, Supplier supplier)
    {
        var receiptsDue = await db.GoodsReceipts.Where(g => g.SupplierId == supplier.Id)
            .SumAsync(g => (decimal?)g.DueAmount) ?? 0m;
        return supplier.OpeningDue + receiptsDue - await UnallocatedAsync(db, supplier.Id);
    }

    private void AddAudit(ShopPosDbContext db, string action, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = "Supplier",
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
