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

    // =========================================================== stock list

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

    // ============================================== quantity-only adjustments

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

        var (variant, product) = await LoadVariantAsync(db, r.VariantId);
        if (product.ProductType != "Stock")
            throw new BusinessRuleException("Service items do not have stock.");
        if (product.TracksSerial || product.TracksBatch)
            throw new BusinessRuleException("This item tracks IMEI/serial numbers or batches. Use the IMEI or batch panel instead.");

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

        if (!await AllowNegativeAsync(db) && current + change < 0)
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

        var movement = NewMovement(branchId, variant, change,
            r.Action == StockAction.Opening ? "Opening" : "Adjustment",
            "Adjustment", null, Combine(label, r.Note));
        db.StockMovements.Add(movement);
        AddAudit(db, "StockAdjust", "StockMovement", movement.Id);

        await db.SaveChangesAsync();
        return current + change;
    }

    // ================================================== IMEI / serial items

    public async Task<List<SerialRow>> GetSerialsAsync(Guid variantId, bool includeAll)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var q = db.SerialItems.AsNoTracking().Where(s => s.VariantId == variantId);
        if (!includeAll)
            q = q.Where(s => s.Status != "Sold" && s.Status != "Removed");

        return await q.OrderBy(s => s.Status).ThenBy(s => s.SerialNo)
            .Take(500)
            .Select(s => new SerialRow(s.Id, s.SerialNo, s.Status, s.CreatedAt))
            .ToListAsync();
    }

    public async Task<int> AddSerialsAsync(Guid variantId, IReadOnlyList<string> serials, bool isOpening, string? note)
    {
        var cleaned = serials.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (cleaned.Count == 0)
            throw new BusinessRuleException("Enter at least one IMEI / serial number.");
        if (cleaned.Count > 500)
            throw new BusinessRuleException("Add at most 500 numbers at a time.");

        var invalid = cleaned.Where(s => !IsValidSerial(s)).Take(3).ToList();
        if (invalid.Count > 0)
            throw new BusinessRuleException(
                "Not a valid IMEI / serial number: " + string.Join(", ", invalid) +
                ". A 15-digit IMEI must pass the IMEI check digit, so please re-check what you typed or scanned.");

        var repeated = cleaned.GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).Take(3).ToList();
        if (repeated.Count > 0)
            throw new BusinessRuleException("The same number appears twice: " + string.Join(", ", repeated));

        await using var db = await _factory.CreateDbContextAsync();

        var (variant, product) = await LoadVariantAsync(db, variantId);
        if (product.ProductType != "Stock" || !product.TracksSerial)
            throw new BusinessRuleException("This item does not track IMEI / serial numbers.");

        var existing = await db.SerialItems.Where(s => cleaned.Contains(s.SerialNo)).ToListAsync();

        // A number that was written off ("Removed") for this same item may be added back.
        var conflicts = existing.Where(s => !(s.Status == "Removed" && s.VariantId == variantId)).ToList();
        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                "Already in the system: " +
                string.Join(", ", conflicts.Take(3).Select(s => $"{s.SerialNo} ({s.Status})")) +
                (conflicts.Count > 3 ? " ..." : ""));

        var byNumber = existing.ToDictionary(s => s.SerialNo, StringComparer.OrdinalIgnoreCase);
        var branchId = _user.BranchId;
        var reason = Combine((isOpening ? "Opening stock" : "Stock added") + " (IMEI/serial)", note);

        foreach (var number in cleaned)
        {
            SerialItem item;
            if (byNumber.TryGetValue(number, out var found))
            {
                item = found;
                item.Status = "InStock";
            }
            else
            {
                item = new SerialItem
                {
                    Id = Guid.CreateVersion7(),
                    VariantId = variantId,
                    SerialNo = number,
                    Status = "InStock",
                    CreatedAt = DateTime.UtcNow
                };
                db.SerialItems.Add(item);
            }

            var movement = NewMovement(branchId, variant, 1, isOpening ? "Opening" : "Adjustment",
                "Serial", item.Id, reason);
            movement.SerialItemId = item.Id;
            db.StockMovements.Add(movement);
        }

        AddAudit(db, "StockSerialsAdded", "ProductVariant", variantId);
        await db.SaveChangesAsync();
        return cleaned.Count;
    }

    public async Task RemoveSerialAsync(Guid serialItemId, string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new BusinessRuleException(
                "Type the reason in the Note box first (for example: damaged, lost, entered by mistake).");

        await using var db = await _factory.CreateDbContextAsync();

        var serial = await db.SerialItems.FirstOrDefaultAsync(s => s.Id == serialItemId)
                     ?? throw new BusinessRuleException("This IMEI / serial number no longer exists.");
        if (serial.Status != "InStock")
            throw new BusinessRuleException($"Only in-stock items can be removed. This one is '{serial.Status}'.");

        var (variant, _) = await LoadVariantAsync(db, serial.VariantId);

        serial.Status = "Removed";

        var movement = NewMovement(_user.BranchId, variant, -1, "Adjustment", "Serial", serial.Id,
            Combine("Removed IMEI/serial " + serial.SerialNo, note));
        movement.SerialItemId = serial.Id;
        db.StockMovements.Add(movement);

        AddAudit(db, "StockSerialRemoved", "SerialItem", serial.Id);
        await db.SaveChangesAsync();
    }

    // ================================================== batch / expiry items

    public async Task<List<BatchRow>> GetBatchesAsync(Guid variantId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var rows = await db.Batches.AsNoTracking()
            .Where(b => b.VariantId == variantId)
            .Select(b => new BatchRow(
                b.Id,
                b.BatchNo,
                b.ExpiryDate,
                db.StockMovements
                    .Where(m => m.BatchId == b.Id && m.BranchId == branchId)
                    .Sum(m => (decimal?)m.QtyChange) ?? 0m))
            .ToListAsync();

        // Earliest expiry first (that is the batch to sell first); batches with nothing left are hidden.
        return rows.Where(r => r.QtyOnHand > 0)
                   .OrderBy(r => r.ExpiryDate ?? DateOnly.MaxValue)
                   .ThenBy(r => r.BatchNo)
                   .ToList();
    }

    public async Task AddBatchStockAsync(Guid variantId, string batchNo, DateOnly? expiry,
        decimal quantity, bool isOpening, string? note)
    {
        var no = batchNo?.Trim() ?? "";
        if (no.Length == 0)
            throw new BusinessRuleException("Enter the batch number (it is printed on the package).");
        if (no.Length > 60)
            throw new BusinessRuleException("The batch number is too long.");
        if (quantity <= 0)
            throw new BusinessRuleException("Quantity must be greater than zero.");
        if (expiry is { } e && e < DateOnly.FromDateTime(DateTime.Today))
            throw new BusinessRuleException("The expiry date is in the past.");

        await using var db = await _factory.CreateDbContextAsync();

        var (variant, product) = await LoadVariantAsync(db, variantId);
        if (product.ProductType != "Stock" || !product.TracksBatch)
            throw new BusinessRuleException("This item does not track batches.");

        var batch = await db.Batches.FirstOrDefaultAsync(b => b.VariantId == variantId && b.BatchNo == no);
        if (batch is null)
        {
            batch = new Batch
            {
                Id = Guid.CreateVersion7(),
                VariantId = variantId,
                BatchNo = no,
                ExpiryDate = expiry,
                CostPrice = variant.CostPrice,
                CreatedAt = DateTime.UtcNow
            };
            db.Batches.Add(batch);
        }
        else if (expiry is not null)
        {
            if (batch.ExpiryDate is null)
                batch.ExpiryDate = expiry;
            else if (batch.ExpiryDate != expiry)
                throw new BusinessRuleException(
                    $"Batch '{no}' already exists with expiry {batch.ExpiryDate.Value.ToString("dd MMM yyyy")}.");
        }

        var movement = NewMovement(_user.BranchId, variant, quantity, isOpening ? "Opening" : "Adjustment",
            "Batch", batch.Id, Combine((isOpening ? "Opening stock" : "Stock added") + $" (batch {no})", note));
        movement.BatchId = batch.Id;
        db.StockMovements.Add(movement);

        AddAudit(db, "StockBatchAdded", "Batch", batch.Id);
        await db.SaveChangesAsync();
    }

    public async Task RemoveBatchStockAsync(Guid batchId, decimal quantity, string reason, string? note)
    {
        if (quantity <= 0)
            throw new BusinessRuleException("Quantity must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId)
                    ?? throw new BusinessRuleException("This batch no longer exists.");
        var (variant, _) = await LoadVariantAsync(db, batch.VariantId);

        var onHand = await db.StockMovements
            .Where(m => m.BatchId == batchId && m.BranchId == branchId)
            .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;

        if (quantity > onHand)
            throw new BusinessRuleException($"This batch only has {onHand:0.###} in stock.");

        var movement = NewMovement(branchId, variant, -quantity, "Adjustment", "Batch", batch.Id,
            Combine($"{reason} (batch {batch.BatchNo})", note));
        movement.BatchId = batch.Id;
        db.StockMovements.Add(movement);

        AddAudit(db, "StockBatchRemoved", "Batch", batch.Id);
        await db.SaveChangesAsync();
    }

    // ================================================================ helpers

    private static async Task<(ProductVariant Variant, Product Product)> LoadVariantAsync(ShopPosDbContext db, Guid variantId)
    {
        var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId)
                      ?? throw new BusinessRuleException("This item no longer exists.");
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == variant.ProductId);
        return (variant, product);
    }

    private static async Task<bool> AllowNegativeAsync(ShopPosDbContext db)
    {
        var value = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "stock.allow_negative"))?.SettingValue;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private StockMovement NewMovement(Guid branchId, ProductVariant variant, decimal change,
        string movementType, string referenceType, Guid? referenceId, string reason) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = branchId,
        VariantId = variant.Id,
        QtyChange = change,
        MovementType = movementType,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        Reason = reason,
        UnitCost = variant.CostPrice,
        CreatedByUserId = _user.IsLoggedIn ? _user.UserId : (Guid?)null,
        CreatedAt = DateTime.UtcNow
    };

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

    private static string Combine(string label, string? note)
    {
        var n = note?.Trim();
        var text = string.IsNullOrEmpty(n) ? label : $"{label}: {n}";
        return text.Length > 255 ? text[..255] : text;
    }

    /// <summary>
    /// Serial numbers: 4-40 letters, digits, "-" or "/". A number that is exactly 15 digits is treated as an
    /// IMEI and must pass the IMEI (Luhn) check digit, which catches most typing mistakes.
    /// </summary>
    private static bool IsValidSerial(string s)
    {
        if (s.Length < 4 || s.Length > 40) return false;
        if (!s.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '/')) return false;
        if (s.Length == 15 && s.All(char.IsAsciiDigit)) return PassesLuhn(s);
        return true;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}
