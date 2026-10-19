namespace ShopPos.Application.Catalog;

/// <summary>A rule the user broke (empty name, duplicate barcode...). The message is safe to show on screen.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

public record CategoryDto(Guid Id, string Name, Guid? ParentCategoryId, bool IsActive)
{
    /// <summary>Placeholder used in drop-downs for "no category / no parent".</summary>
    public static readonly CategoryDto None = new(Guid.Empty, "(none)", null, true);
}

public record VariantDto(
    Guid Id, string? Sku, string? Barcode, string? VariantName,
    decimal CostPrice, decimal SellingPrice, decimal ReorderLevel, bool IsActive);

public record ProductListItem(
    Guid Id, string Name, string? CategoryName, string ProductType, string? Brand,
    int VariantCount, bool IsActive)
{
    public string Subtitle =>
        $"{ProductType} · {CategoryName ?? "No category"} · {VariantCount} variant(s)" + (IsActive ? "" : " · inactive");
}

public record ProductDetail(
    Guid Id, string Name, Guid? CategoryId, string ProductType, string? Brand,
    bool TracksSerial, bool TracksBatch, bool IsActive, List<VariantDto> Variants);

public interface ICatalogService
{
    Task<List<CategoryDto>> GetCategoriesAsync();
    Task<Guid> SaveCategoryAsync(CategoryDto category);          // Id = Guid.Empty means "new"
    Task DeleteCategoryAsync(Guid id);

    Task<List<ProductListItem>> SearchProductsAsync(string? search);
    Task<ProductDetail?> GetProductAsync(Guid id);
    Task<Guid> SaveProductAsync(ProductDetail product);          // Id = Guid.Empty means "new"

    /// <summary>Next free internal EAN-13 barcode (starts with 200).</summary>
    Task<string> GenerateBarcodeAsync();
}
