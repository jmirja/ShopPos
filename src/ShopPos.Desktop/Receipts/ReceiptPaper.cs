namespace ShopPos.Desktop.Receipts;

/// <summary>Paper sizes in millimetres. Roll paper has no fixed height: the receipt is as long as it needs to be.</summary>
public record ReceiptPaper(string Key, string Label, double PageWidthMm, double? PageHeightMm, double MarginMm, double FontSize)
{
    public static readonly ReceiptPaper[] All =
    {
        new("80mm", "Thermal roll 80 mm", 80, null, 4, 12),    // about 72 mm printable
        new("58mm", "Thermal roll 58 mm", 58, null, 5, 11),    // about 48 mm printable
        new("A4",   "A4 page (invoice)",  210, 297, 15, 14)
    };

    public static ReceiptPaper Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
