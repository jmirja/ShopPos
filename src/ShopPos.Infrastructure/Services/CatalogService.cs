using Microsoft.EntityFrameworkCore;
using ShopPos.Application.Auth;
using ShopPos.Application.Catalog;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;

namespace ShopPos.Infrastructure.Services;

public class CatalogService : ICatalogService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly ICurrentUser _user;

    public CatalogService(IDbContextFactory<ShopPosDbContext> factory, ICurrentUser user)
    {
        _factory = factory;
        _user = user;
    }

    // ------------------------------------------------------------ categories

    public async Task<List<CategoryDto>> GetCategoriesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.ParentCategoryId, c.IsActive ?? false))
            .ToListAsync();
    }

    public async Task<Guid> SaveCategoryAsync(CategoryDto dto)
    {
        var name = dto.Name?.Trim() ?? "";
        if (name.Length == 0)
            throw new BusinessRuleException("Category name is required.");
        if (dto.Id != Guid.Empty && dto.ParentCategoryId == dto.Id)
            throw new BusinessRuleException("A category cannot be its own parent.");

        await using var db = await _factory.CreateDbContextAsync();

        var duplicate = await db.Categories.AnyAsync(c =>
            c.Id != dto.Id && c.ParentCategoryId == dto.ParentCategoryId && c.Name == name);
        if (duplicate)
            throw new BusinessRuleException($"A category named '{name}' already exists here.");

        Category entity;
        var isNew = dto.Id == Guid.Empty;
        if (isNew)
        {
            entity = new Category { Id = Guid.CreateVersion7() };
            db.Categories.Add(entity);
        }
        else
        {
            entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == dto.Id)
                     ?? throw new BusinessRuleException("This category no longer exists.");
        }

        entity.Name = name;
        entity.ParentCategoryId = dto.ParentCategoryId;
        entity.IsActive = dto.IsActive;

        AddAudit(db, isNew ? "Create" : "Update", "Category", entity.Id);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task DeleteCategoryAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var inUse = await db.Products.AnyAsync(p => p.CategoryId == id)
                    || await db.Categories.AnyAsync(c => c.ParentCategoryId == id);
        if (inUse)
            throw new BusinessRuleException(
                "This category is in use (it has products or sub-categories). Untick 'Active' instead of deleting it.");

        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (entity is null) return;

        db.Categories.Remove(entity);
        AddAudit(db, "Delete", "Category", id);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------- products

    public async Task<List<ProductListItem>> SearchProductsAsync(string? search)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var term = search?.Trim();
        var query = db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(p =>
                p.Name.Contains(term)
                || (p.Brand != null && p.Brand.Contains(term))
                || db.ProductVariants.Any(v => v.ProductId == p.Id
                    && (v.Sku.Contains(term) || (v.Barcode != null && v.Barcode.Contains(term)))));
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(500)
            .Select(p => new ProductListItem(
                p.Id,
                p.Name,
                db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                p.ProductType,
                p.Brand,
                db.ProductVariants.Count(v => v.ProductId == p.Id),
                p.IsActive ?? false))
            .ToListAsync();
    }

    public async Task<ProductDetail?> GetProductAsync(Guid id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return null;

        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == id)
            .OrderBy(v => v.Sku)
            .Select(v => new VariantDto(v.Id, v.Sku, v.Barcode, v.VariantName,
                v.CostPrice, v.SellingPrice, v.ReorderLevel, v.IsActive ?? false))
            .ToListAsync();

        return new ProductDetail(p.Id, p.Name, p.CategoryId, p.ProductType, p.Brand,
            p.TracksSerial, p.TracksBatch, p.IsActive ?? false, variants);
    }

    public async Task<Guid> SaveProductAsync(ProductDetail d)
    {
        // ---- checks that need no database ----
        var name = d.Name?.Trim() ?? "";
        if (name.Length == 0)
            throw new BusinessRuleException("Product name is required.");
        if (d.ProductType != "Stock" && d.ProductType != "Service")
            throw new BusinessRuleException("Product type must be Stock or Service.");
        if (d.Variants.Count == 0)
            throw new BusinessRuleException("Add at least one variant (it holds the price).");
        if (d.Variants.Any(v => v.CostPrice < 0 || v.SellingPrice < 0 || v.ReorderLevel < 0))
            throw new BusinessRuleException("Prices and reorder level cannot be negative.");

        var skus = d.Variants.Select(v => Clean(v.Sku)).Where(s => s != null).Select(s => s!.ToLowerInvariant()).ToList();
        if (skus.Count != skus.Distinct().Count())
            throw new BusinessRuleException("Two variants have the same SKU.");

        var barcodes = d.Variants.Select(v => Clean(v.Barcode)).Where(s => s != null).Select(s => s!).ToList();
        if (barcodes.Count != barcodes.Distinct().Count())
            throw new BusinessRuleException("Two variants have the same barcode.");

        // ---- database work ----
        await using var db = await _factory.CreateDbContextAsync();

        var isStock = d.ProductType == "Stock";
        var isNew = d.Id == Guid.Empty;

        Product entity;
        if (isNew)
        {
            entity = new Product { Id = Guid.CreateVersion7(), CreatedAt = DateTime.UtcNow };
            db.Products.Add(entity);
        }
        else
        {
            entity = await db.Products.FirstOrDefaultAsync(p => p.Id == d.Id)
                     ?? throw new BusinessRuleException("This product no longer exists.");
            entity.UpdatedAt = DateTime.UtcNow;
        }

        // Changing IMEI / batch tracking after stock exists would make stock and IMEI/batch lists disagree.
        if (!isNew
            && (entity.TracksSerial != (isStock && d.TracksSerial) || entity.TracksBatch != (isStock && d.TracksBatch))
            && await db.StockMovements.AnyAsync(m =>
                   db.ProductVariants.Any(v => v.Id == m.VariantId && v.ProductId == entity.Id)))
        {
            throw new BusinessRuleException(
                "You cannot change IMEI / batch tracking after stock has been recorded for this product.");
        }

        entity.Name = name;
        entity.CategoryId = d.CategoryId;
        entity.ProductType = d.ProductType;
        entity.Brand = Clean(d.Brand);
        entity.TracksSerial = isStock && d.TracksSerial;
        entity.TracksBatch = isStock && d.TracksBatch;
        entity.IsActive = d.IsActive;

        var existing = await db.ProductVariants
            .Where(v => v.ProductId == entity.Id)
            .ToDictionaryAsync(v => v.Id);

        foreach (var v in d.Variants)
        {
            ProductVariant row;
            if (v.Id != Guid.Empty && existing.TryGetValue(v.Id, out var found))
            {
                row = found;
            }
            else
            {
                row = new ProductVariant { Id = Guid.CreateVersion7(), ProductId = entity.Id };
                db.ProductVariants.Add(row);
            }

            // SKU: typed by the user, or generated (SKU-000001, ...)
            var sku = Clean(v.Sku) ?? $"SKU-{await NextCounterAsync(db, "sku.next"):D6}";

            // Barcode: typed/scanned by the user; stock items without one get an internal barcode
            var barcode = Clean(v.Barcode);
            if (barcode is null && isStock)
                barcode = await NextBarcodeAsync(db);

            if (await db.ProductVariants.AnyAsync(x => x.Sku == sku && x.Id != row.Id))
                throw new BusinessRuleException($"SKU '{sku}' is already used by another product.");
            if (barcode is not null && await db.ProductVariants.AnyAsync(x => x.Barcode == barcode && x.Id != row.Id))
                throw new BusinessRuleException($"Barcode '{barcode}' is already used by another product.");

            row.Sku = sku;
            row.Barcode = barcode;
            row.VariantName = Clean(v.VariantName);
            row.CostPrice = v.CostPrice;
            row.SellingPrice = v.SellingPrice;
            row.ReorderLevel = v.ReorderLevel;
            row.IsActive = v.IsActive;
        }

        AddAudit(db, isNew ? "Create" : "Update", "Product", entity.Id);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<string> GenerateBarcodeAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var code = await NextBarcodeAsync(db);
        await db.SaveChangesAsync();          // saves the updated counter
        return code;
    }

    // --------------------------------------------------------------- helpers

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>Reads and increments a counter stored in the settings table (saved with the next SaveChanges).</summary>
    private static async Task<long> NextCounterAsync(ShopPosDbContext db, string key)
    {
        var setting = db.Settings.Local.FirstOrDefault(s => s.SettingKey == key)
                      ?? await db.Settings.FirstOrDefaultAsync(s => s.SettingKey == key);

        if (setting is null)
        {
            setting = new Setting { SettingKey = key, SettingValue = "1" };
            db.Settings.Add(setting);
        }

        var current = long.TryParse(setting.SettingValue, out var n) ? n : 1;
        setting.SettingValue = (current + 1).ToString();
        return current;
    }

    private static async Task<string> NextBarcodeAsync(ShopPosDbContext db)
    {
        for (var i = 0; i < 1000; i++)
        {
            var seq = await NextCounterAsync(db, "barcode.next");
            var code = BuildEan13(seq);
            if (!await db.ProductVariants.AnyAsync(v => v.Barcode == code))
                return code;
        }
        throw new BusinessRuleException("Could not find a free barcode number.");
    }

    /// <summary>EAN-13 with the "200" in-store prefix + 9-digit sequence + check digit.</summary>
    private static string BuildEan13(long sequence)
    {
        var first12 = "200" + sequence.ToString("D9");
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = first12[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        var check = (10 - sum % 10) % 10;
        return first12 + check;
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
