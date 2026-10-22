using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Billing;
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class BillingService : IBillingService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public BillingService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    public async Task<LookupResult> LookupAsync(string input)
    {
        var term = input.Trim();
        if (term.Length == 0)
            return LookupResult.Many(Array.Empty<LookupMatch>());

        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        // 1. Exact barcode or SKU
        var variant = await db.ProductVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Barcode == term || v.Sku == term);
        if (variant is not null)
        {
            var item = await GetItemAsync(db, variant.Id, branchId);
            return item is null
                ? LookupResult.Problem($"'{term}' is inactive and cannot be sold.")
                : LookupResult.Found(new LookupMatch(item));
        }

        // 2. Exact IMEI / serial number
        var serial = await db.SerialItems.AsNoTracking().FirstOrDefaultAsync(s => s.SerialNo == term);
        if (serial is not null)
        {
            if (serial.Status != "InStock")
                return LookupResult.Problem($"IMEI / serial {serial.SerialNo} is not in stock (status: {serial.Status}).");

            var item = await GetItemAsync(db, serial.VariantId, branchId);
            return item is null
                ? LookupResult.Problem($"The item for {serial.SerialNo} is inactive and cannot be sold.")
                : LookupResult.Found(new LookupMatch(item, serial.Id, serial.SerialNo));
        }

        // 3. Partial matches
        var matches = await SearchCoreAsync(db, term, branchId);
        return matches.Count == 0
            ? LookupResult.Problem($"No item found for '{term}'.")
            : LookupResult.Many(matches);
    }

    public async Task<List<LookupMatch>> SearchAsync(string term)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await SearchCoreAsync(db, term.Trim(), _user.BranchId);
    }

    public async Task<decimal> GetAvailableQtyAsync(Guid variantId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var branchId = _user.BranchId;

        var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
        if (variant is null) return 0m;
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == variant.ProductId);

        if (product.ProductType != "Stock") return decimal.MaxValue;

        if (product.TracksBatch && !product.TracksSerial)
        {
            // Only stock in batches that are not expired can be sold.
            var today = DateOnly.FromDateTime(DateTime.Today);
            return await db.StockMovements
                .Where(m => m.VariantId == variantId && m.BranchId == branchId && m.BatchId != null
                            && db.Batches.Any(b => b.Id == m.BatchId && (b.ExpiryDate == null || b.ExpiryDate >= today)))
                .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;
        }

        return await db.StockMovements
            .Where(m => m.VariantId == variantId && m.BranchId == branchId)
            .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;
    }

    public async Task<bool> AllowNegativeStockAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var value = (await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "stock.allow_negative"))?.SettingValue;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ helpers

    private static async Task<SellableItem?> GetItemAsync(ShopPosDbContext db, Guid variantId, Guid branchId)
    {
        var v = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == variantId);
        if (v is null) return null;
        var p = await db.Products.AsNoTracking().FirstAsync(x => x.Id == v.ProductId);
        if (p.IsActive != true || v.IsActive != true) return null;

        var qty = await db.StockMovements
            .Where(m => m.VariantId == v.Id && m.BranchId == branchId)
            .SumAsync(m => (decimal?)m.QtyChange) ?? 0m;

        return new SellableItem(v.Id, p.Name, v.VariantName, v.Sku, v.Barcode, v.SellingPrice, v.CostPrice,
            p.ProductType == "Service", p.TracksSerial, p.TracksBatch, qty);
    }

    private static async Task<List<LookupMatch>> SearchCoreAsync(ShopPosDbContext db, string term, Guid branchId)
    {
        var results = new List<LookupMatch>();
        if (term.Length < 2) return results;

        // Every word typed must match something: "iphone black" finds "iPhone 15 - Black / 128GB".
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5);

        var q = db.ProductVariants.AsNoTracking()
            .Join(db.Products.AsNoTracking(), v => v.ProductId, p => p.Id, (v, p) => new { v, p })
            .Where(x => x.p.IsActive == true && x.v.IsActive == true);

        foreach (var w in words)
        {
            var word = w;
            q = q.Where(x =>
                x.p.Name.Contains(word)
                || (x.v.VariantName != null && x.v.VariantName.Contains(word))
                || (x.p.Brand != null && x.p.Brand.Contains(word))
                || x.v.Sku.Contains(word)
                || (x.v.Barcode != null && x.v.Barcode.Contains(word)));
        }

        var items = await q
            .OrderBy(x => x.p.Name).ThenBy(x => x.v.VariantName)
            .Take(15)
            .Select(x => new SellableItem(
                x.v.Id, x.p.Name, x.v.VariantName, x.v.Sku, x.v.Barcode,
                x.v.SellingPrice, x.v.CostPrice,
                x.p.ProductType == "Service", x.p.TracksSerial, x.p.TracksBatch,
                db.StockMovements
                    .Where(m => m.VariantId == x.v.Id && m.BranchId == branchId)
                    .Sum(m => (decimal?)m.QtyChange) ?? 0m))
            .ToListAsync();

        results.AddRange(items.Select(i => new LookupMatch(i)));

        // Typing the last digits of an IMEI finds that exact phone.
        if (term.Length >= 4 && !term.Contains(' '))
        {
            var serials = await db.SerialItems.AsNoTracking()
                .Where(s => s.Status == "InStock" && s.SerialNo.Contains(term))
                .OrderBy(s => s.SerialNo)
                .Take(8)
                .ToListAsync();

            foreach (var s in serials)
            {
                var item = await GetItemAsync(db, s.VariantId, branchId);
                if (item is not null)
                    results.Add(new LookupMatch(item, s.Id, s.SerialNo));
            }
        }

        return results;
    }
}
