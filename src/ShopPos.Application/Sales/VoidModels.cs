namespace ShopPos.Application.Sales;

public record SaleToVoidLine(string Description, decimal Qty, decimal LineTotal);

public record SaleToVoid(
    Guid SaleId, string InvoiceNo, DateTime SaleDateUtc, string Cashier, string? Customer,
    decimal Total, decimal Paid, decimal Due, string Status, IReadOnlyList<SaleToVoidLine> Lines)
{
    public string DateText =>
        DateTime.SpecifyKind(SaleDateUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");

    public bool CanVoid => Status == "Completed";
}

public interface ISaleVoidService
{
    /// <summary>Finds a sale of this branch by its invoice number (null when there is none).</summary>
    Task<SaleToVoid?> FindAsync(string invoiceNo);

    /// <summary>
    /// Cancels a completed sale: items go back into stock, IMEIs become available again and every payment is
    /// recorded as refunded. Only an Owner or Manager may do this.
    /// </summary>
    Task VoidAsync(Guid saleId, string reason);
}
