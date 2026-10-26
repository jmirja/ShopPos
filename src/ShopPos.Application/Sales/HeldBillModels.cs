using ShopPos.Application.Customers;

namespace ShopPos.Application.Sales;

public record HeldBillInfo(
    Guid Id, DateTime HeldAtUtc, string? Note, string? CustomerName, string HeldBy, int LineCount, decimal Total)
{
    public string HeldAtText =>
        DateTime.SpecifyKind(HeldAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM HH:mm");

    public string Title => !string.IsNullOrWhiteSpace(Note) ? Note! : CustomerName ?? "(no note)";
}

public record HeldLine(Guid VariantId, Guid? SerialItemId, string? SerialNo, decimal Qty, decimal UnitPrice);

public record HeldBill(Guid Id, CustomerDto? Customer, IReadOnlyList<HeldLine> Lines);

public record HoldRequest(Guid? CustomerId, string? Note, IReadOnlyList<SaleLineRequest> Lines);

/// <summary>Bills that were put on one side ("hold") and can be finished later. No stock is touched while a bill is held.</summary>
public interface IHeldBillService
{
    Task<Guid> HoldAsync(HoldRequest request);
    Task<List<HeldBillInfo>> ListAsync();
    Task<int> CountAsync();
    Task<HeldBill?> GetAsync(Guid id);
    Task DeleteAsync(Guid id);
}
