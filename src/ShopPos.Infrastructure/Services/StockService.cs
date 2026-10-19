using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Application.Stock;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class StockService : IStockService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public StockService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<List<StockRow>> GetStockAsync(string? search, bool lowOnly)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;
        var term = search?.Trim();

        var q = db.ProductVariants.AsNoTracking()
            .Join(db.Products.AsNoTracking(), v => v.ProductId, p => p.Id, (v, p) => new { v, p })
            .Where(x => x.p.ProductType == "Stock" && x.p.IsActive == true && x.v.IsActive == true);

        if (!string.IsNullOrEmpty(term))
        {
            q = q.Where(x =>
                x.p.Name.Contains(term)
                || (x.v.VariantName != null && x.v.VariantName.Contains(term))
                || x.v.Sku.Contains(term)
                || (x.v.Barcode != null && x.v.Barcode.Contains(term)));
        }

        var rows = await q
            .OrderBy(x => x.p.Name).ThenBy(x => x.v.VariantName)
            .Select(x => new StockRow(
                x.v.Id,
                x.p.Name,
                x.v.VariantName,
                x.v.Sku,
                x.v.Barcode,
                db.StockMovements
                    .Where(m => m.VariantId == x.v.Id && m.BranchId == branchId)
                    .Sum(m => (decimal?)m.QtyChange) ?? 0m,
                x.v.ReorderLevel,
                x.p.TracksSerial,
                x.p.TracksBatch))
            .ToListAsync();

        return lowOnly ? rows.Where(r => r.IsLow).ToList() : rows;
    }

    public async Task<int> CountLowStockAsync() => (await GetStockAsync(null, lowOnly: true)).Count;

    public async Task<decimal> AdjustStockAsync(StockAdjustmentRequest r)
    {
        if (r.Action == StockAction.SetCount)
        {
            if (r.Quantity < 0) throw new BusinessRuleException("The counted quantity cannot be negative.");
        }
        else if (r.Quantity <= 0)
        {
            throw new BusinessRuleException("Quantity must be greater than zero.");
        }

        await using var db = await _factory.CreateDbContextAsync();

        var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == r.VariantId)
                      ?? throw new BusinessRuleException("This item no longer exists.");
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == variant.ProductId);

        if (product.ProductType != "Stock")
            throw new BusinessRuleException("Service items do not have stock.");
        if (product.TracksSerial || product.TracksBatch)
            throw new BusinessRuleException(
                "This item tracks IMEI/serial numbers or batches. Its stock is added by entering them (next step).");

        var branchId = _user.BranchId;

        var current = await db.StockMovements
            .Where(m => m.VariantId == variant.Id && m.BranchId == branchId)
            .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;

        if (r.Action == StockAction.Opening &&
            await db.StockMovements.AnyAsync(m => m.VariantId == variant.Id && m.BranchId == branchId
                                                  && m.MovementType == "Opening"))
        {
            throw new BusinessRuleException(
                "Opening stock was already entered for this item. Use 'Add stock' or 'Set counted quantity' instead.");
        }

        decimal change = r.Action switch
        {
            StockAction.Opening or StockAction.Add => r.Quantity,
            StockAction.Damaged or StockAction.Lost => -r.Quantity,
            StockAction.SetCount => r.Quantity - current,
            _ => throw new BusinessRuleException("Unknown stock action.")
        };

        if (change == 0)
            throw new BusinessRuleException($"Nothing to change: the quantity is already {current:0.###}.");

        var allowNegative = string.Equals(
            (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.SettingKey == "stock.allow_negative"))?.SettingValue,
            "true", StringComparison.OrdinalIgnoreCase);

        if (!allowNegative && current + change < 0)
            throw new BusinessRuleException($"Not enough stock. Quantity on hand is {current:0.###}.");

        var label = r.Action switch
        {
            StockAction.Opening => "Opening stock",
            StockAction.Add => "Stock added",
            StockAction.Damaged => "Damaged",
            StockAction.Lost => "Lost / stolen",
            StockAction.SetCount => "Stock count correction",
            _ => "Adjustment"
        };
        var note = r.Note?.Trim();
        var reason = string.IsNullOrEmpty(note) ? label : $"{label}: {note}";
        if (reason.Length > 255) reason = reason[..255];

        var movement = new StockMovement
        {
            Id = Guid.CreateVersion7(),
            BranchId = branchId,
            VariantId = variant.Id,
            QtyChange = change,
            MovementType = r.Action == StockAction.Opening ? "Opening" : "Adjustment",
            ReferenceType = "Adjustment",
            Reason = reason,
            UnitCost = variant.CostPrice,
            CreatedByUserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            CreatedAt = DateTime.UtcNow
        };
        db.StockMovements.Add(movement);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
            Action = "StockAdjust",
            EntityName = "StockMovement",
            EntityId = movement.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return current + change;
    }

    public async Task<List<MovementRow>> GetMovementsAsync(Guid variantId, int take = 20)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        return await db.StockMovements.AsNoTracking()
            .Where(m => m.VariantId == variantId && m.BranchId == branchId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new MovementRow(
                m.CreatedAt,
                m.MovementType,
                m.QtyChange,
                m.Reason,
                db.Users.Where(u => u.Id == m.CreatedByUserId).Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync();
    }
}
