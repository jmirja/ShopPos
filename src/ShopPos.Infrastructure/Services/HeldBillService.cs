using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Customers;
using ShopPos.Application.Sales;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

/// <summary>
/// A held bill is stored as a sale with the status "Held": it has lines but no invoice number,
/// no payment and no stock movement. Resuming it deletes the held copy.
/// </summary>
public class HeldBillService : IHeldBillService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public HeldBillService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<Guid> HoldAsync(HoldRequest request)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("The bill is empty.");

        await using var db = await _factory.CreateDbContextAsync();

        decimal total = 0;
        foreach (var l in request.Lines)
            total += Math.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero);

        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note)) note = null;
        else if (note.Length > 480) note = note[..480];

        var sale = new Sale
        {
            Id = Guid.CreateVersion7(),
            InvoiceNo = null,
            BranchId = _user.BranchId,
            CustomerId = request.CustomerId,
            UserId = _user.UserId,
            SaleDate = DateTime.UtcNow,
            SubTotal = total,
            DiscountAmount = 0,
            TaxAmount = 0,
            Total = total,
            PaidAmount = 0,
            DueAmount = 0,
            Status = "Held",
            Notes = note
        };
        db.Sales.Add(sale);

        foreach (var l in request.Lines)
        {
            db.SaleLines.Add(new SaleLine
            {
                // ids grow with every line so the original order comes back when the bill is resumed
                Id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(db.SaleLines.Local.Count)),
                SaleId = sale.Id,
                VariantId = l.VariantId,
                SerialItemId = l.SerialItemId,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                UnitCostSnapshot = 0,
                DiscountAmount = 0,
                TaxAmount = 0,
                LineTotal = Math.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)
            });
        }

        AddAudit(db, "BillHeld", sale.Id);
        await db.SaveChangesAsync();
        return sale.Id;
    }

    public async Task<List<HeldBillInfo>> ListAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var rows = await db.Sales.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Status == "Held")
            .OrderBy(s => s.SaleDate)
            .Select(s => new
            {
                s.Id,
                s.SaleDate,
                s.Notes,
                s.Total,
                CustomerName = db.Customers.Where(c => c.Id == s.CustomerId).Select(c => c.Name).FirstOrDefault(),
                HeldBy = db.Users.Where(u => u.Id == s.UserId).Select(u => u.FullName).FirstOrDefault(),
                Lines = db.SaleLines.Count(l => l.SaleId == s.Id)
            })
            .ToListAsync();

        return rows.Select(r => new HeldBillInfo(
            r.Id, r.SaleDate, r.Notes, r.CustomerName, r.HeldBy ?? "", r.Lines, r.Total)).ToList();
    }

    public async Task<int> CountAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;
        return await db.Sales.CountAsync(s => s.BranchId == branchId && s.Status == "Held");
    }

    public async Task<HeldBill?> GetAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.Status == "Held");
        if (sale is null) return null;

        CustomerDto? customer = null;
        if (sale.CustomerId is Guid customerId)
        {
            customer = await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => new CustomerDto(c.Id, c.Name, c.Phone))
                .FirstOrDefaultAsync();
        }

        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => l.SaleId == id)
            .OrderBy(l => l.Id)
            .Select(l => new { l.VariantId, l.SerialItemId, l.Qty, l.UnitPrice })
            .ToListAsync();

        var serialIds = lines.Where(l => l.SerialItemId != null).Select(l => l.SerialItemId!.Value).ToList();
        var serialNumbers = await db.SerialItems.AsNoTracking()
            .Where(s => serialIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SerialNo);

        return new HeldBill(id, customer, lines.Select(l => new HeldLine(
            l.VariantId,
            l.SerialItemId,
            l.SerialItemId is Guid sid && serialNumbers.TryGetValue(sid, out var no) ? no : null,
            l.Qty,
            l.UnitPrice)).ToList());
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == id && s.Status == "Held");
        if (sale is null) return;                       // already gone (for example resumed on another screen)

        db.SaleLines.RemoveRange(await db.SaleLines.Where(l => l.SaleId == id).ToListAsync());
        db.Sales.Remove(sale);

        AddAudit(db, "HeldBillRemoved", id);
        await db.SaveChangesAsync();
    }

    private void AddAudit(ShopPosDbContext db, string action, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = "Sale",
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }
}
