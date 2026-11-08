using ShopPos.Application.Sales;

namespace ShopPos.Application.Repairs;

public static class RepairStatus
{
    public const string Received = "Received";
    public const string Repairing = "Repairing";
    public const string Ready = "Ready";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";

    /// <summary>The job is still being worked on (it can be edited, get parts and take advance payments).</summary>
    public static bool IsOpen(string status) => status is Received or Repairing or Ready;
}

public enum RepairFilter { Open, All, Delivered, Cancelled }

public record RepairListItem(
    Guid Id, string JobNo, DateTime ReceivedAtUtc, string CustomerName, string? CustomerPhone,
    string? Brand, string? Model, string? Imei, string Status, decimal Estimate, decimal Advance)
{
    public string Device => $"{Brand} {Model}".Trim();

    public string ReceivedText =>
        DateTime.SpecifyKind(ReceivedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM HH:mm");
}

public record RepairPartRow(Guid Id, Guid VariantId, string Name, decimal Qty, decimal UnitPrice)
{
    public decimal LineTotal => Math.Round(Qty * UnitPrice, 2, MidpointRounding.AwayFromZero);
}

public record RepairHistoryRow(string Status, string? Note, string? By, DateTime AtUtc)
{
    public string AtText => DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");
}

public record RepairDetail(
    Guid Id, string JobNo, Guid CustomerId, string CustomerName, string? CustomerPhone,
    string? Brand, string? Model, string? Imei, string Problem, decimal Estimate, decimal Advance,
    string Status, DateTime ReceivedAtUtc, DateTime? DeliveredAtUtc, int WarrantyDays, decimal? FinalCharge,
    string? Notes, Guid? SaleId,
    IReadOnlyList<RepairPartRow> Parts, IReadOnlyList<RepairHistoryRow> History)
{
    public string Device => $"{Brand} {Model}".Trim();
    public decimal PartsTotal => Parts.Sum(p => p.LineTotal);
    public bool IsOpen => RepairStatus.IsOpen(Status);

    public string WarrantyText
    {
        get
        {
            if (Status != RepairStatus.Delivered || DeliveredAtUtc is not { } delivered) return "";
            if (WarrantyDays <= 0) return "No warranty given.";

            var until = DateTime.SpecifyKind(delivered, DateTimeKind.Utc).ToLocalTime().Date.AddDays(WarrantyDays);
            return until >= DateTime.Today
                ? $"Warranty until {until:dd MMM yyyy}"
                : $"Warranty ended on {until:dd MMM yyyy}";
        }
    }
}

public record RepairIntake(Guid CustomerId, string? Brand, string? Model, string? Imei, string Problem, decimal Estimate, string? Notes);

public record RepairEdit(Guid Id, string? Brand, string? Model, string? Imei, string Problem, decimal Estimate, string? Notes);

public record AdvanceRequest(Guid JobId, int PaymentMethodId, decimal Amount, string? Reference);

/// <summary>Labour is what the technician charges for the work; the parts are added on top of it.</summary>
public record DeliverRequest(Guid JobId, decimal Labour, int WarrantyDays, IReadOnlyList<PaymentRequest> Payments);

public interface IRepairService
{
    Task<List<RepairListItem>> ListAsync(string? search, RepairFilter filter);
    Task<RepairDetail?> GetAsync(Guid id);

    Task<Guid> CreateAsync(RepairIntake intake);
    Task UpdateAsync(RepairEdit edit);

    /// <summary>Received -> Repairing -> Ready (and Ready -> Repairing if more work is needed).</summary>
    Task ChangeStatusAsync(Guid jobId, string newStatus, string? note);

    /// <summary>Takes a spare part out of stock and puts it on the job.</summary>
    Task AddPartAsync(Guid jobId, Guid variantId, decimal qty);

    /// <summary>Removes a part from the job and puts it back into stock.</summary>
    Task RemovePartAsync(Guid partId);

    Task TakeAdvanceAsync(AdvanceRequest request);

    /// <summary>Makes the final bill, takes the remaining payment and hands the device over.</summary>
    Task<CompletedSale> DeliverAsync(DeliverRequest request);

    Task CancelAsync(Guid jobId, string reason);
}
