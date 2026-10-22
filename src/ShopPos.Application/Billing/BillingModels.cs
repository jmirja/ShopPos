namespace ShopPos.Application.Billing;

/// <summary>Something that can be put on a bill: a stock item or a service.</summary>
public record SellableItem(
    Guid VariantId, string ProductName, string? VariantName, string Sku, string? Barcode,
    decimal SellingPrice, decimal CostPrice,
    bool IsService, bool TracksSerial, bool TracksBatch, decimal QtyOnHand)
{
    public string DisplayName => string.IsNullOrWhiteSpace(VariantName) ? ProductName : $"{ProductName} - {VariantName}";
}

/// <summary>An item found by a search or scan, optionally pinned to one specific IMEI / serial number.</summary>
public record LookupMatch(SellableItem Item, Guid? SerialItemId = null, string? SerialNo = null)
{
    public string Title => Item.DisplayName;
    public string PriceText => Item.SellingPrice.ToString("N2");

    public string Subtitle
    {
        get
        {
            if (SerialNo is not null) return $"IMEI / serial {SerialNo}";
            if (Item.IsService) return "Service";

            var stock = Item.QtyOnHand <= 0 ? "Out of stock" : $"In stock: {Item.QtyOnHand:0.###}";
            return Item.TracksSerial ? stock + " · scan the IMEI to add" : stock;
        }
    }
}

public record LookupResult(LookupMatch? Match, IReadOnlyList<LookupMatch> Candidates, string? Message)
{
    public static LookupResult Found(LookupMatch match) => new(match, Array.Empty<LookupMatch>(), null);
    public static LookupResult Many(IReadOnlyList<LookupMatch> list) => new(null, list, null);
    public static LookupResult Problem(string message) => new(null, Array.Empty<LookupMatch>(), message);
}

public interface IBillingService
{
    /// <summary>What happens when the cashier presses Enter: exact barcode / SKU / IMEI, else a list of matches.</summary>
    Task<LookupResult> LookupAsync(string input);

    /// <summary>Matches shown while typing.</summary>
    Task<List<LookupMatch>> SearchAsync(string term);

    /// <summary>How many units can be sold right now (expired batches are not counted).</summary>
    Task<decimal> GetAvailableQtyAsync(Guid variantId);

    Task<bool> AllowNegativeStockAsync();
}
