namespace ShopPos.Application.Customers;

/// <summary>Short form used by the customer picker on the billing screen.</summary>
public record CustomerDto(Guid Id, string Name, string? Phone);

public record CustomerRow(
    Guid Id, string Name, string? Phone, string? Address, decimal Due, bool IsActive, DateTime? LastPurchaseUtc)
{
    public string LastPurchaseText => LastPurchaseUtc is { } d
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy")
        : "";

    public string DisplayName => IsActive ? Name : Name + " (inactive)";
}

public record CustomerDetail(
    Guid Id, string Name, string? Phone, string? Address, string? Notes,
    decimal OpeningDue, bool IsActive, decimal Due);

public record CustomerEdit(
    Guid Id, string Name, string? Phone, string? Address, string? Notes, decimal OpeningDue, bool IsActive);

public record CustomerSaleRow(
    Guid SaleId, DateTime SaleDateUtc, string? InvoiceNo, decimal Total, decimal Paid, decimal Due, string Status)
{
    public string DateText =>
        DateTime.SpecifyKind(SaleDateUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");
}

public record CustomerPaymentRow(
    DateTime PaidAtUtc, string Direction, decimal Amount, string? Method, string? Invoice, string? Reference)
{
    public string DateText =>
        DateTime.SpecifyKind(PaidAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");

    /// <summary>Money received is shown plain, money paid back (refund) with a minus sign.</summary>
    public string AmountText => Direction == "Out" ? "-" + Amount.ToString("N2") : Amount.ToString("N2");
}

public record OpenSaleRow(Guid SaleId, string? InvoiceNo, DateTime SaleDateUtc, decimal Total, decimal Paid, decimal Due)
{
    public string DateText =>
        DateTime.SpecifyKind(SaleDateUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy");
}

/// <summary>What a customer still owes: the old balance from before the app, plus unpaid bills.</summary>
public record DueSummary(decimal OpeningRemaining, IReadOnlyList<OpenSaleRow> OpenSales, decimal TotalDue);

public record CollectDueRequest(Guid CustomerId, int PaymentMethodId, decimal Amount, string? Reference);

public record CollectionResult(decimal Collected, decimal RemainingDue);

public interface ICustomerService
{
    // ---- used by the billing screen
    /// <summary>Active customers whose name or phone contains the text.</summary>
    Task<List<CustomerDto>> SearchAsync(string? term);

    /// <summary>Adds a customer with just a name and an optional phone number.</summary>
    Task<CustomerDto> QuickAddAsync(string name, string? phone);

    // ---- the customers screen
    Task<List<CustomerRow>> ListAsync(string? search, bool owingOnly, bool includeInactive);
    Task<CustomerDetail?> GetAsync(Guid id);
    Task<Guid> SaveAsync(CustomerEdit customer);
    Task<List<CustomerSaleRow>> GetSalesAsync(Guid customerId, int take = 100);
    Task<List<CustomerPaymentRow>> GetPaymentsAsync(Guid customerId, int take = 200);

    // ---- money the customer owes
    Task<DueSummary> GetDueSummaryAsync(Guid customerId);

    /// <summary>
    /// Records a payment against what the customer owes: the oldest unpaid bill first, then the old balance.
    /// </summary>
    Task<CollectionResult> CollectDueAsync(CollectDueRequest request);
}
