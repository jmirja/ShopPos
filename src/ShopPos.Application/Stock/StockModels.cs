namespace ShopPos.Application.Stock;

public enum StockAction
{
    Opening,     // stock you already had on the shelf when you started using the app
    Add,         // found / returned to shelf / new stock
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

public record SerialRow(Guid Id, string SerialNo, string Status, DateTime CreatedAtUtc)
{
    public string Added => DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy");
}

public record BatchRow(Guid Id, string BatchNo, DateOnly? ExpiryDate, decimal QtyOnHand)
{
    public string ExpiryText => ExpiryDate?.ToString("dd MMM yyyy") ?? "";

    public string Status
    {
        get
        {
            if (ExpiryDate is not { } e) return "";
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (e < today) return "Expired";
            if (e <= today.AddDays(30)) return "Expiring soon";
            return "OK";
        }
    }
}

public record StockAdjustmentRequest(Guid VariantId, StockAction Action, decimal Quantity, string? Note);

public interface IStockService
{
    /// <summary>Stock items (not services) with quantity on hand for the current branch.</summary>
    Task<List<StockRow>> GetStockAsync(string? search, bool lowOnly);

    Task<int> CountLowStockAsync();

    /// <summary>Quantity-only items. Records one stock movement and returns the new quantity on hand.</summary>
    Task<decimal> AdjustStockAsync(StockAdjustmentRequest request);

    Task<List<MovementRow>> GetMovementsAsync(Guid variantId, int take = 20);

    // ---- IMEI / serial tracked items ----
    Task<List<SerialRow>> GetSerialsAsync(Guid variantId, bool includeAll);
    Task<int> AddSerialsAsync(Guid variantId, IReadOnlyList<string> serials, bool isOpening, string? note);
    Task RemoveSerialAsync(Guid serialItemId, string? note);

    // ---- batch / expiry tracked items ----
    Task<List<BatchRow>> GetBatchesAsync(Guid variantId);
    Task AddBatchStockAsync(Guid variantId, string batchNo, DateOnly? expiry, decimal quantity, bool isOpening, string? note);
    Task RemoveBatchStockAsync(Guid batchId, decimal quantity, string reason, string? note);
}
