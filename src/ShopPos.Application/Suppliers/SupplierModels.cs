namespace ShopPos.Application.Suppliers;

public record SupplierOption(Guid Id, string Name);

public record SupplierRow(
    Guid Id, string Name, string? ContactPerson, string? Phone, string? Address, decimal Due, bool IsActive)
{
    public string DisplayName => IsActive ? Name : Name + " (inactive)";
}

public record SupplierDetail(
    Guid Id, string Name, string? ContactPerson, string? Phone, string? Address,
    decimal OpeningDue, bool IsActive, decimal Due);

public record SupplierEdit(
    Guid Id, string Name, string? ContactPerson, string? Phone, string? Address, decimal OpeningDue, bool IsActive);

public record SupplierPurchaseRow(
    Guid ReceiptId, string ReceiptNo, DateOnly Date, string? InvoiceNo, string? PoNo,
    decimal Total, decimal Paid, decimal Due)
{
    public string DateText => Date.ToString("dd MMM yyyy");
}

public record SupplierPaymentRow(DateTime PaidAtUtc, string? ReceiptNo, string Method, decimal Amount, string? Reference)
{
    public string DateText =>
        DateTime.SpecifyKind(PaidAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");
}

public record OpenReceiptRow(Guid ReceiptId, string ReceiptNo, DateOnly Date, string? InvoiceNo, decimal Total, decimal Due)
{
    public string DateText => Date.ToString("dd MMM yyyy");
}

/// <summary>What you owe a supplier: the old balance from before the app plus unpaid purchases.</summary>
public record SupplierDueSummary(decimal OpeningRemaining, IReadOnlyList<OpenReceiptRow> OpenReceipts, decimal TotalDue);

public record PaySupplierRequest(Guid SupplierId, int PaymentMethodId, decimal Amount, string? Reference);

public record SupplierPayResult(decimal Paid, decimal RemainingDue);

public interface ISupplierService
{
    Task<List<SupplierOption>> GetOptionsAsync();

    Task<List<SupplierRow>> ListAsync(string? search, bool owingOnly, bool includeInactive);
    Task<SupplierDetail?> GetAsync(Guid id);
    Task<Guid> SaveAsync(SupplierEdit supplier);

    Task<List<SupplierPurchaseRow>> GetPurchasesAsync(Guid supplierId, int take = 100);
    Task<List<SupplierPaymentRow>> GetPaymentsAsync(Guid supplierId, int take = 200);

    Task<SupplierDueSummary> GetDueSummaryAsync(Guid supplierId);

    /// <summary>Pays what you owe: the oldest unpaid purchase first, then the old balance.</summary>
    Task<SupplierPayResult> PayAsync(PaySupplierRequest request);
}
