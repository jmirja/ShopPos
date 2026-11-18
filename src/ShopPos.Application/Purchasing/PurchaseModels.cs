namespace ShopPos.Application.Purchasing;

public static class PoStatus
{
    public const string Draft = "Draft";
    public const string Ordered = "Ordered";
    public const string PartiallyReceived = "PartiallyReceived";
    public const string Received = "Received";
    public const string Cancelled = "Cancelled";

    public static string Label(string status) => status == PartiallyReceived ? "Partly received" : status;
}

public enum PoFilter { Open, All, Received, Cancelled }

public record PoRow(
    Guid Id, string PoNo, DateOnly OrderDate, Guid SupplierId, string SupplierName,
    string Status, decimal Total, int LineCount)
{
    public string StatusText => PoStatus.Label(Status);
    public string DateText => OrderDate.ToString("dd MMM yyyy");
}

public record PoLineRow(
    Guid Id, Guid VariantId, string Name, decimal QtyOrdered, decimal QtyReceived, decimal UnitCost,
    bool TracksSerial, bool TracksBatch)
{
    public decimal Outstanding => Math.Max(0m, QtyOrdered - QtyReceived);
}

public record PoDetail(
    Guid Id, string PoNo, Guid SupplierId, string SupplierName, DateOnly OrderDate,
    string Status, string? Notes, decimal Total, IReadOnlyList<PoLineRow> Lines)
{
    public bool IsDraft => Status == PoStatus.Draft;
    public bool CanReceive => Status is PoStatus.Draft or PoStatus.Ordered or PoStatus.PartiallyReceived;
}

public record PoLineInput(Guid VariantId, decimal Qty, decimal UnitCost);

/// <summary>Id = Guid.Empty creates a new order. Only a draft can be saved again.</summary>
public record PoSave(Guid Id, Guid SupplierId, DateOnly OrderDate, string? Notes, IReadOnlyList<PoLineInput> Lines);

/// <summary>One item that arrived. Phones need their IMEIs (one per unit); cosmetics need a batch number.</summary>
public record ReceiveLine(
    Guid VariantId, Guid? PoLineId, decimal Qty, decimal UnitCost,
    string? BatchNo, DateOnly? ExpiryDate, IReadOnlyList<string> Serials);

public record ReceiveRequest(
    Guid SupplierId, Guid? PoId, string? SupplierInvoiceNo, DateOnly ReceivedDate, string? Notes,
    IReadOnlyList<ReceiveLine> Lines,
    decimal PaidNow, int? PaymentMethodId, string? Reference);

public record ReceiptResult(Guid ReceiptId, string ReceiptNo, decimal Total, decimal Paid, decimal Due);

public interface IPurchaseService
{
    Task<List<PoRow>> ListOrdersAsync(PoFilter filter, string? search);
    Task<PoDetail?> GetOrderAsync(Guid id);

    Task<Guid> SaveOrderAsync(PoSave order);
    Task MarkOrderedAsync(Guid orderId);
    Task CancelOrderAsync(Guid orderId, string reason);

    /// <summary>
    /// Goods arrived (with or without an order). Adds the stock, stores IMEIs and batches, updates the
    /// cost price, records what you owe the supplier and any payment made now. All or nothing.
    /// </summary>
    Task<ReceiptResult> ReceiveAsync(ReceiveRequest request);
}
