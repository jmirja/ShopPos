using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Customers;
using ShopPos.Application.Shifts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public CustomerService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    // ==================================================== billing screen

    public async Task<List<CustomerDto>> SearchAsync(string? term)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var t = term?.Trim();
        var q = db.Customers.AsNoTracking().Where(c => c.IsActive == true);

        if (!string.IsNullOrEmpty(t))
            q = q.Where(c => c.Name.Contains(t) || (c.Phone != null && c.Phone.Contains(t)));

        return await q.OrderBy(c => c.Name)
            .Take(30)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Phone))
            .ToListAsync();
    }

    public async Task<CustomerDto> QuickAddAsync(string name, string? phone)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0)
            throw new BusinessRuleException("Customer name is required.");

        var ph = Clean(phone);

        await using var db = await _factory.CreateDbContextAsync();

        if (ph is not null)
        {
            var existing = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Phone == ph);
            if (existing is not null)
                throw new BusinessRuleException($"A customer with this phone number already exists: {existing.Name}.");
        }

        var entity = new Customer
        {
            Id = Guid.CreateVersion7(),
            Name = n,
            Phone = ph,
            OpeningDue = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(entity);
        AddAudit(db, "Create", "Customer", entity.Id);

        await db.SaveChangesAsync();
        return new CustomerDto(entity.Id, entity.Name, entity.Phone);
    }

    // ================================================== customers screen

    public async Task<List<CustomerRow>> ListAsync(string? search, bool owingOnly, bool includeInactive)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var q = db.Customers.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(c => c.IsActive == true);

        var t = search?.Trim();
        if (!string.IsNullOrEmpty(t))
            q = q.Where(c => c.Name.Contains(t)
                             || (c.Phone != null && c.Phone.Contains(t))
                             || (c.Address != null && c.Address.Contains(t)));

        // due = old balance + unpaid on bills - payments that were not tied to one bill
        var rows = await q.OrderBy(c => c.Name)
            .Take(500)
            .Select(c => new
            {
                c.Id, c.Name, c.Phone, c.Address, c.IsActive, c.OpeningDue,
                SalesDue = db.Sales
                    .Where(s => s.CustomerId == c.Id && s.Status == "Completed")
                    .Sum(s => (decimal?)s.DueAmount) ?? 0m,
                Unallocated = db.Payments
                    .Where(p => p.CustomerId == c.Id && p.SaleId == null && p.Direction == "In")
                    .Sum(p => (decimal?)p.Amount) ?? 0m,
                LastPurchase = db.Sales
                    .Where(s => s.CustomerId == c.Id && s.Status == "Completed")
                    .Max(s => (DateTime?)s.SaleDate)
            })
            .ToListAsync();

        var result = rows
            .Select(r => new CustomerRow(r.Id, r.Name, r.Phone, r.Address,
                r.OpeningDue + r.SalesDue - r.Unallocated, r.IsActive == true, r.LastPurchase))
            .ToList();

        return owingOnly ? result.Where(r => r.Due > 0).OrderByDescending(r => r.Due).ToList() : result;
    }

    public async Task<CustomerDetail?> GetAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;

        var due = await GetDueAsync(db, c);
        return new CustomerDetail(c.Id, c.Name, c.Phone, c.Address, c.Notes, c.OpeningDue, c.IsActive == true, due);
    }

    public async Task<Guid> SaveAsync(CustomerEdit e)
    {
        var name = e.Name?.Trim() ?? "";
        if (name.Length == 0)
            throw new BusinessRuleException("Customer name is required.");
        if (e.OpeningDue < 0)
            throw new BusinessRuleException("The old balance cannot be negative.");

        var phone = Clean(e.Phone);
        var opening = Math.Round(e.OpeningDue, 2, MidpointRounding.AwayFromZero);
        var privileged = _user.IsInRole("Owner", "Manager");

        await using var db = await _factory.CreateDbContextAsync();

        if (phone is not null &&
            await db.Customers.AnyAsync(c => c.Id != e.Id && c.Phone == phone))
            throw new BusinessRuleException("Another customer already has this phone number.");

        Customer entity;
        var isNew = e.Id == Guid.Empty;

        if (isNew)
        {
            if (opening != 0 && !privileged)
                throw new BusinessRuleException("Only an Owner or Manager can enter an old balance.");

            entity = new Customer { Id = Guid.CreateVersion7(), CreatedAt = DateTime.UtcNow };
            db.Customers.Add(entity);
        }
        else
        {
            entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == e.Id)
                     ?? throw new BusinessRuleException("This customer no longer exists.");

            var openingChanged = entity.OpeningDue != opening;
            var activeChanged = entity.IsActive != e.IsActive;

            if ((openingChanged || activeChanged) && !privileged)
                throw new BusinessRuleException(
                    "Only an Owner or Manager can change the old balance or deactivate a customer.");

            if (openingChanged)
            {
                var collected = await UnallocatedAsync(db, entity.Id);
                if (opening < collected)
                    throw new BusinessRuleException(
                        $"The old balance cannot be less than what was already collected against it ({collected:N2}).");
            }

            if (activeChanged && !e.IsActive && await GetDueAsync(db, entity) > 0)
                throw new BusinessRuleException("This customer still owes money, so it cannot be deactivated.");
        }

        entity.Name = name;
        entity.Phone = phone;
        entity.Address = Clean(e.Address);
        entity.Notes = Clean(e.Notes);
        entity.OpeningDue = opening;
        entity.IsActive = e.IsActive;

        AddAudit(db, isNew ? "Create" : "Update", "Customer", entity.Id);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<List<CustomerSaleRow>> GetSalesAsync(Guid customerId, int take = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();

        return await db.Sales.AsNoTracking()
            .Where(s => s.CustomerId == customerId && s.Status != "Held")
            .OrderByDescending(s => s.SaleDate)
            .Take(take)
            .Select(s => new CustomerSaleRow(s.Id, s.SaleDate, s.InvoiceNo, s.Total, s.PaidAmount, s.DueAmount, s.Status))
            .ToListAsync();
    }

    public async Task<List<CustomerPaymentRow>> GetPaymentsAsync(Guid customerId, int take = 200)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.PaidAt)
            .Take(take)
            .Select(p => new
            {
                p.PaidAt, p.Direction, p.Amount, p.ReferenceNo,
                Method = db.PaymentMethods.Where(m => m.Id == p.PaymentMethodId).Select(m => m.Name).FirstOrDefault(),
                Invoice = db.Sales.Where(s => s.Id == p.SaleId).Select(s => s.InvoiceNo).FirstOrDefault()
            })
            .ToListAsync();

        return rows.Select(r => new CustomerPaymentRow(r.PaidAt, r.Direction, r.Amount, r.Method, r.Invoice, r.ReferenceNo)).ToList();
    }

    // ==================================================== money owed

    public async Task<DueSummary> GetDueSummaryAsync(Guid customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId)
                       ?? throw new BusinessRuleException("This customer no longer exists.");

        var openingRemaining = Math.Max(0m, customer.OpeningDue - await UnallocatedAsync(db, customerId));

        var open = await db.Sales.AsNoTracking()
            .Where(s => s.CustomerId == customerId && s.Status == "Completed" && s.DueAmount > 0)
            .OrderBy(s => s.SaleDate)
            .Select(s => new OpenSaleRow(s.Id, s.InvoiceNo, s.SaleDate, s.Total, s.PaidAmount, s.DueAmount))
            .ToListAsync();

        return new DueSummary(openingRemaining, open, openingRemaining + open.Sum(o => o.Due));
    }

    public async Task<CollectionResult> CollectDueAsync(CollectDueRequest r)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");

        var amount = Math.Round(r.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            throw new BusinessRuleException("Enter an amount greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CustomerId)
                       ?? throw new BusinessRuleException("This customer no longer exists.");
        var method = await db.PaymentMethods.AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == r.PaymentMethodId && m.IsActive == true)
                     ?? throw new BusinessRuleException("This payment method is not available.");

        // Cash goes into the cashier's drawer, so that drawer has to be open.
        var isCash = string.Equals(method.Name, "Cash", StringComparison.OrdinalIgnoreCase);
        if (isCash && await _shifts.IsShiftRequiredAsync() && await _shifts.GetOpenShiftAsync() is null)
            throw new BusinessRuleException("Open your shift (cash drawer) first: this cash goes into your drawer.");

        // Oldest unpaid bill first (tracked, because we change them below).
        var sales = await db.Sales
            .Where(s => s.CustomerId == r.CustomerId && s.Status == "Completed" && s.DueAmount > 0)
            .OrderBy(s => s.SaleDate)
            .ToListAsync();

        var openingRemaining = Math.Max(0m, customer.OpeningDue - await UnallocatedAsync(db, r.CustomerId));
        var totalDue = openingRemaining + sales.Sum(s => s.DueAmount);

        if (totalDue <= 0)
            throw new BusinessRuleException("This customer does not owe anything.");
        if (amount > totalDue)
            throw new BusinessRuleException($"The customer owes only {totalDue:N2}.");

        var reference = Clean(r.Reference);
        var now = DateTime.UtcNow;
        var left = amount;

        foreach (var sale in sales)
        {
            if (left <= 0) break;

            var part = Math.Min(left, sale.DueAmount);
            sale.PaidAmount += part;
            sale.DueAmount -= part;
            left -= part;

            db.Payments.Add(NewPayment(sale.Id, r, part, reference, now));
        }

        if (left > 0)                                   // what is left pays off the old balance
            db.Payments.Add(NewPayment(null, r, left, reference, now));

        AddAudit(db, "DueCollected", "Customer", r.CustomerId);
        await db.SaveChangesAsync();

        return new CollectionResult(amount, totalDue - amount);
    }

    // ================================================================ helpers

    private Payment NewPayment(Guid? saleId, CollectDueRequest r, decimal amount, string? reference, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = _user.BranchId,
        SaleId = saleId,
        CustomerId = r.CustomerId,
        PaymentMethodId = r.PaymentMethodId,
        Direction = "In",
        Amount = amount,
        ReferenceNo = reference,
        PaidAt = now,
        CreatedByUserId = _user.UserId
    };

    /// <summary>Payments that were not tied to one bill: they pay off the old balance.</summary>
    private static async Task<decimal> UnallocatedAsync(ShopPosDbContext db, Guid customerId) =>
        await db.Payments
            .Where(p => p.CustomerId == customerId && p.SaleId == null && p.Direction == "In")
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

    private static async Task<decimal> GetDueAsync(ShopPosDbContext db, Customer customer)
    {
        var salesDue = await db.Sales
            .Where(s => s.CustomerId == customer.Id && s.Status == "Completed")
            .SumAsync(s => (decimal?)s.DueAmount) ?? 0m;

        return customer.OpeningDue + salesDue - await UnallocatedAsync(db, customer.Id);
    }

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private void AddAudit(ShopPosDbContext db, string action, string entityName, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }
}
