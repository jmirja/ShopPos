namespace ShopPos.Application.Stock;

public enum StockAction
{
    Opening,     // stock you already had on the shelf when you started using the app
    Add,         // found / returned to shelf
    Damaged,
    Lost,
    SetCount     // you counted the shelf; the system corrects itself to your count
}

public record StockRow(
    Guid VariantId, string ProductName, string? VariantName, string Sku, string? Barcode,
    decimal QtyOnHand, decimal ReorderLevel, bool TracksSerial, bool TracksBatch)
{
    public string DisplayName => string.IsNullOrWhiteSpace(VariantName) ? ProductName : $"{ProductName} - {VariantName}";
    public bool IsLow => QtyOnHand <= ReorderLevel;
    public string Status => QtyOnHand <= 0 ? "Out of stock" : IsLow ? "Low" : "OK";
    public bool NeedsSpecialTracking => TracksSerial || TracksBatch;
    public string Tracking => TracksSerial ? "IMEI/serial" : TracksBatch ? "Batch" : "";
}

public record MovementRow(DateTime CreatedAtUtc, string MovementType, decimal QtyChange, string? Reason, string? UserName)
{
    public string When => DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");
    public string Change => QtyChange > 0 ? $"+{QtyChange:0.###}" : $"{QtyChange:0.###}";
}

public record StockAdjustmentRequest(Guid VariantId, StockAction Action, decimal Quantity, string? Note);

public interface IStockService
{
    /// <summary>Stock items (not services) with quantity on hand for the current branch.</summary>
    Task<List<StockRow>> GetStockAsync(string? search, bool lowOnly);

    Task<int> CountLowStockAsync();

    /// <summary>Records one stock movement. Returns the new quantity on hand.</summary>
    Task<decimal> AdjustStockAsync(StockAdjustmentRequest request);

    Task<List<MovementRow>> GetMovementsAsync(Guid variantId, int take = 20);
}
