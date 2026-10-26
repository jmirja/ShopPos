using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Sales;
using ShopPos.Application.Shifts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class SaleVoidService : ISaleVoidService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;
    private readonly IShiftService _shifts;

    public SaleVoidService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user, IShiftService shifts)
    {
        _factory = factory;
        _user = user;
        _shifts = shifts;
    }

    public async Task<SaleToVoid?> FindAsync(string invoiceNo)
    {
        var no = invoiceNo?.Trim() ?? "";
        if (no.Length == 0) return null;

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var sale = await db.Sales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.InvoiceNo == no && s.BranchId == branchId);
        if (sale is null) return null;

        var cashier = await db.Users.AsNoTracking()
            .Where(u => u.Id == sale.UserId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "";

        string? customer = null;
        if (sale.CustomerId is Guid customerId)
            customer = await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId).Select(c => c.Name).FirstOrDefaultAsync();

        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => l.SaleId == sale.Id)
            .OrderBy(l => l.Id)
            .Select(l => new { l.Description, l.Qty, l.LineTotal })
            .ToListAsync();

        return new SaleToVoid(
            sale.Id, sale.InvoiceNo ?? no, sale.SaleDate, cashier, customer,
            sale.Total, sale.PaidAmount, sale.DueAmount, sale.Status,
            lines.Select(l => new SaleToVoidLine(l.Description ?? "", l.Qty, l.LineTotal)).ToList());
    }

    public async Task VoidAsync(Guid saleId, string reason)
    {
        if (!_user.IsInRole("Owner", "Manager"))
            throw new BusinessRuleException("Only an Owner or Manager can void a sale.");

        var why = reason?.Trim() ?? "";
        if (why.Length == 0)
            throw new BusinessRuleException("Type the reason for voiding this sale.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == saleId && s.BranchId == branchId)
                   ?? throw new BusinessRuleException("This sale no longer exists.");
        if (sale.Status != "Completed")
            throw new BusinessRuleException($"This sale is already {sale.Status.ToLowerInvariant()}.");

        // ---- money that has to go back
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId == sale.Id && p.Direction == "In")
            .ToListAsync();
        var methods = await db.PaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id);

        var cashRefund = payments
            .Where(p => string.Equals(methods[p.PaymentMethodId].Name, "Cash", StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Amount);

        // The cash is handed back from the drawer of the person who voids, so that drawer must be open.
        if (cashRefund > 0 && await _shifts.IsShiftRequiredAsync() && await _shifts.GetOpenShiftAsync() is null)
            throw new BusinessRuleException(
                "Open your shift (cash drawer) first: the cash refund is paid out of your drawer.");

        var now = DateTime.UtcNow;
        var label = $"Void {sale.InvoiceNo}: {why}";
        if (label.Length > 255) label = label[..255];

        // ---- put the stock back
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceType == "Sale" && m.ReferenceId == sale.Id)
            .ToListAsync();

        foreach (var m in movements)
        {
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.CreateVersion7(),
                BranchId = m.BranchId,
                VariantId = m.VariantId,
                BatchId = m.BatchId,
                SerialItemId = m.SerialItemId,
                QtyChange = -m.QtyChange,
                MovementType = "Return",
                ReferenceType = "Void",
                ReferenceId = sale.Id,
                Reason = label,
                UnitCost = m.UnitCost,
                CreatedByUserId = _user.UserId,
                CreatedAt = now
            });
        }

        // ---- phones become sellable again
        var serialIds = movements.Where(m => m.SerialItemId != null).Select(m => m.SerialItemId!.Value).ToList();
        if (serialIds.Count > 0)
        {
            var serials = await db.SerialItems.Where(s => serialIds.Contains(s.Id)).ToListAsync();
            foreach (var s in serials)
                if (s.Status == "Sold") s.Status = "InStock";
        }

        // ---- refunds, one per original payment, same method
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
                ReferenceNo = $"Void {sale.InvoiceNo}",
                PaidAt = now,
                CreatedByUserId = _user.UserId
            });
        }

        // ---- the sale itself stays in the books, marked as voided
        sale.Status = "Voided";
        sale.VoidReason = why.Length > 255 ? why[..255] : why;
        sale.VoidedByUserId = _user.UserId;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.UserId,
            Action = "SaleVoided",
            EntityName = "Sale",
            EntityId = sale.Id.ToString(),
            CreatedAt = now
        });

        await db.SaveChangesAsync();
    }
}
