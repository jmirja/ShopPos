namespace ShopPos.Application.Receipts;

public record ReceiptLine(string Description, decimal Qty, decimal UnitPrice, decimal LineTotal);

public record ReceiptPayment(string Method, decimal Amount, string? Reference);

public record ReceiptData(
    string ShopName, string? ShopAddress, string? ShopPhone, string? Footer,
    string InvoiceNo, DateTime SaleDateLocal, string Cashier, string? Customer,
    IReadOnlyList<ReceiptLine> Lines,
    decimal SubTotal, decimal Discount, decimal Tax, decimal Total,
    IReadOnlyList<ReceiptPayment> Payments, decimal Paid, decimal Due,
    decimal? Change,          // only known right after the sale (it is not stored)
    bool IsVoided);

public record ReceiptSettings(string Paper, bool AutoPrint, string? Printer);

public interface IReceiptService
{
    Task<ReceiptData> GetReceiptAsync(Guid saleId, decimal? changeGiven = null);

    /// <summary>The most recent completed sale of this branch (null when there is none yet).</summary>
    Task<ReceiptData?> GetLastReceiptAsync();

    Task<ReceiptSettings> GetSettingsAsync();

    /// <summary>Remember the paper size ("80mm", "58mm" or "A4") as the default.</summary>
    Task SavePaperAsync(string paper);
}
