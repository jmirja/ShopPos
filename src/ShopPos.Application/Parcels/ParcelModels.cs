namespace ShopPos.Application.Parcels;

public static class ParcelStatus
{
    public const string Booked = "Booked";
    public const string InTransit = "InTransit";
    public const string Delivered = "Delivered";
    public const string Returned = "Returned";
    public const string Cancelled = "Cancelled";

    public static string Label(string status) => status == InTransit ? "In transit" : status;
}

public enum ParcelFilter { Open, Today, Last7Days, Last30Days, All }

public record CourierDto(int Id, string Name, string? ContactInfo);

public record ParcelRow(
    Guid Id, DateTime BookedAtUtc, int CourierId, string CourierName, string? TrackingNo,
    string? SenderName, string? SenderPhone, string ReceiverName, string? ReceiverPhone, string? ReceiverAddress,
    decimal? WeightKg, decimal Charge, decimal CourierCost, decimal Cod, string Status, string? Notes, string UserName,
    bool CostSettled, bool CodReceived, bool CodPaidToSender)
{
    public decimal Margin => Charge - CourierCost;
    public string StatusText => ParcelStatus.Label(Status);

    public string BookedText =>
        DateTime.SpecifyKind(BookedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM HH:mm");

    /// <summary>Where the cash-on-delivery money is.</summary>
    public string CodText =>
        Cod <= 0 ? "" :
        Status == ParcelStatus.Cancelled || Status == ParcelStatus.Returned ? "no COD (not delivered)" :
        Status != ParcelStatus.Delivered ? "COD not collected yet" :
        !CodReceived ? "Courier still owes the COD" :
        !CodPaidToSender ? "COD received, to pay the sender" :
        "COD paid to the sender";
}

public record NewParcel(
    int CourierId, string? TrackingNo, Guid? CustomerId,
    string? SenderName, string? SenderPhone,
    string ReceiverName, string? ReceiverPhone, string ReceiverAddress,
    decimal? WeightKg, decimal Charge, decimal CourierCost, decimal Cod,
    int? PaymentMethodId,          // how the customer pays the charge (not needed when the charge is 0)
    string? Notes);

/// <summary>What is open with one courier.</summary>
public record CourierBalance(
    int CourierId, string CourierName,
    int UnpaidParcels, decimal UnpaidCost,
    int CodPendingParcels, decimal CodPending,
    int CodToPayParcels, decimal CodToPay);

public interface IParcelService
{
    Task<List<CourierDto>> GetCouriersAsync();
    Task<CourierDto> AddCourierAsync(string name, string? contactInfo);

    Task<List<ParcelRow>> ListAsync(ParcelFilter filter, int? courierId, string? search);

    /// <summary>Saves the booking and records the charge the customer paid.</summary>
    Task<Guid> BookAsync(NewParcel parcel);

    Task UpdateTrackingAsync(Guid parcelId, string? trackingNo);

    /// <summary>Booked -> In transit -> Delivered or Returned.</summary>
    Task ChangeStatusAsync(Guid parcelId, string newStatus);

    /// <summary>Cancels a parcel that is still only booked and refunds the charge. Owner or Manager only.</summary>
    Task CancelAsync(Guid parcelId, string reason);

    // ---- settlement with the couriers
    Task<List<CourierBalance>> GetCourierBalancesAsync();

    /// <summary>Pays the courier for every parcel that is not paid yet. Returns the total paid.</summary>
    Task<decimal> PayCourierCostAsync(int courierId, int paymentMethodId, string? reference);

    /// <summary>Records the COD money the courier handed over for delivered parcels. Returns the total received.</summary>
    Task<decimal> ReceiveCodAsync(int courierId, int paymentMethodId, string? reference);

    /// <summary>Pays the COD money to the sender of one parcel.</summary>
    Task PayCodToSenderAsync(Guid parcelId, int paymentMethodId, string? reference);
}
