using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Shifts;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class ShiftService : IShiftService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public ShiftService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<bool> IsShiftRequiredAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var value = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "shift.required"))?.SettingValue;
        return !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ShiftInfo?> GetOpenShiftAsync()
    {
        if (!_user.IsLoggedIn) return null;

        await using var db = await _factory.CreateDbContextAsync();
        var session = await OpenSessions(db).AsNoTracking().OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();
        return session is null ? null : new ShiftInfo(session.Id, session.OpenedAt, session.OpeningCash);
    }

    public async Task<ShiftInfo> OpenShiftAsync(decimal openingCash)
    {
        if (!_user.IsLoggedIn)
            throw new BusinessRuleException("Your session has ended. Please sign in again.");
        if (openingCash < 0)
            throw new BusinessRuleException("The opening cash cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();

        if (await OpenSessions(db).AnyAsync())
            throw new BusinessRuleException("You already have an open shift.");

        var session = new CashDrawerSession
        {
            Id = Guid.CreateVersion7(),
            BranchId = _user.BranchId,
            UserId = _user.UserId,
            OpenedAt = DateTime.UtcNow,
            OpeningCash = Math.Round(openingCash, 2, MidpointRounding.AwayFromZero)
        };
        db.CashDrawerSessions.Add(session);
        AddAudit(db, "ShiftOpened", session.Id);

        await db.SaveChangesAsync();
        return new ShiftInfo(session.Id, session.OpenedAt, session.OpeningCash);
    }

    public async Task<ShiftSummary?> GetOpenShiftSummaryAsync()
    {
        if (!_user.IsLoggedIn) return null;

        await using var db = await _factory.CreateDbContextAsync();
        var session = await OpenSessions(db).AsNoTracking().OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();
        return session is null ? null : await BuildSummaryAsync(db, session);
    }

    public async Task<ShiftSummary> CloseShiftAsync(decimal countedCash, string? notes)
    {
        if (countedCash < 0)
            throw new BusinessRuleException("The counted cash cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();

        var session = await OpenSessions(db).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync()
                      ?? throw new BusinessRuleException("You do not have an open shift.");

        var counted = Math.Round(countedCash, 2, MidpointRounding.AwayFromZero);

        session.ClosedAt = DateTime.UtcNow;                          // the summary counts up to this moment
        var summary = await BuildSummaryAsync(db, session);

        session.ClosingCash = counted;
        session.ExpectedCash = summary.ExpectedCash;
        session.Difference = counted - summary.ExpectedCash;
        session.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        AddAudit(db, "ShiftClosed", session.Id);
        await db.SaveChangesAsync();

        return summary with { CountedCash = counted, Difference = counted - summary.ExpectedCash };
    }

    // ================================================================ helpers

    private IQueryable<CashDrawerSession> OpenSessions(ShopPosDbContext db)
    {
        var userId = _user.UserId;
        var branchId = _user.BranchId;
        return db.CashDrawerSessions.Where(s => s.UserId == userId && s.BranchId == branchId && s.ClosedAt == null);
    }

    private static async Task<ShiftSummary> BuildSummaryAsync(ShopPosDbContext db, CashDrawerSession session)
    {
        var from = session.OpenedAt;
        var to = session.ClosedAt ?? DateTime.UtcNow;
        var userId = session.UserId;
        var branchId = session.BranchId;

        // Money that this cashier took or paid back during the shift, per payment method.
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.CreatedByUserId == userId && p.BranchId == branchId && p.PaidAt >= from && p.PaidAt <= to)
            .Join(db.PaymentMethods.AsNoTracking(), p => p.PaymentMethodId, m => m.Id,
                (p, m) => new { Method = m.Name, p.Direction, p.Amount })
            .ToListAsync();

        var methods = payments
            .GroupBy(x => x.Method)
            .Select(g => new MethodTotal(
                g.Key,
                g.Where(x => x.Direction == "In").Sum(x => x.Amount),
                g.Where(x => x.Direction == "Out").Sum(x => x.Amount)))
            .OrderBy(m => m.Method)
            .ToList();

        var cash = methods.FirstOrDefault(m => string.Equals(m.Method, "Cash", StringComparison.OrdinalIgnoreCase));
        var cashIn = cash?.In ?? 0m;
        var cashOut = cash?.Out ?? 0m;

        var sales = db.Sales.AsNoTracking().Where(s =>
            s.UserId == userId && s.BranchId == branchId && s.Status == "Completed"
            && s.SaleDate >= from && s.SaleDate <= to);
        var salesCount = await sales.CountAsync();
        var salesTotal = await sales.SumAsync(s => (decimal?)s.Total) ?? 0m;

        return new ShiftSummary(
            session.Id, session.OpenedAt, session.ClosedAt, session.OpeningCash,
            salesCount, salesTotal, methods,
            cashIn, cashOut, session.OpeningCash + cashIn - cashOut,
            session.ClosingCash, session.Difference);
    }

    private void AddAudit(ShopPosDbContext db, string action, Guid entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = action,
            EntityName = "CashDrawerSession",
            EntityId = entityId.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }
}
