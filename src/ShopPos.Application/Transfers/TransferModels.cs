namespace ShopPos.Application.Transfers;

public static class TransferProviders
{
    public static readonly (string Key, string Label)[] All =
    {
        ("WesternUnion", "Western Union"),
        ("Ria", "Ria"),
        ("MoneyGram", "MoneyGram")
    };

    public static string LabelOf(string key) => All.FirstOrDefault(p => p.Key == key).Label ?? key;
}

public static class TransferDirection
{
    /// <summary>A customer sends money through your shop (they pay you the amount plus the fee).</summary>
    public const string Send = "Send";

    /// <summary>A customer collects money that was sent to them (you pay it out in cash).</summary>
    public const string Receive = "Receive";
}

/// <summary>
/// One line of the transfer ledger. A cancelled transfer is never deleted: a second line with the same
/// reference and negative amounts (a "reversal") cancels it out, so the ledger always adds up.
/// </summary>
public record TransferRow(
    Guid Id, DateTime TxnAtUtc, string Provider, string Direction, string ReferenceNo,
    string? SenderName, string? SenderPhone, string? ReceiverName, string? ReceiverPhone,
    decimal Principal, decimal Fee, decimal Commission, string? Notes, string UserName,
    bool IsReversal, bool IsCancelled)
{
    public string ProviderLabel => TransferProviders.LabelOf(Provider);

    public string TimeText => DateTime.SpecifyKind(TxnAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");

    public string StatusText => IsReversal ? "Reversal" : IsCancelled ? "Cancelled" : "";

    public string Parties => $"{SenderName}  >  {ReceiverName}";
}

public record NewTransfer(
    string Provider, string Direction, string ReferenceNo, Guid? CustomerId,
    string SenderName, string? SenderPhone, string ReceiverName, string? ReceiverPhone,
    decimal Principal, decimal Fee, decimal Commission,
    int? PaymentMethodId,          // how the customer pays (Send only); a payout (Receive) is always cash
    string? Notes);

public record DailyProviderSummary(
    string Provider, int SendCount, int ReceiveCount,
    decimal SentPrincipal, decimal ReceivedPrincipal, decimal FeesCollected, decimal Commission,
    decimal CashIn, decimal CashOut, decimal NetDueToProvider)
{
    public string ProviderLabel => TransferProviders.LabelOf(Provider);

    public string DueText => NetDueToProvider switch
    {
        > 0 => $"You owe {ProviderLabel}: {NetDueToProvider:N2}",
        < 0 => $"{ProviderLabel} owes you: {-NetDueToProvider:N2}",
        _ => "Nothing to settle"
    };
}

public record DailySummary(
    DateOnly Day, IReadOnlyList<DailyProviderSummary> Providers, decimal TotalCommission, decimal NetCash);

public interface ITransferService
{
    Task<List<TransferRow>> ListAsync(DateOnly day, string? provider, string? search);

    /// <summary>Totals per provider for one day: what to settle with each provider and how much commission you earned.</summary>
    Task<DailySummary> GetSummaryAsync(DateOnly day);

    /// <summary>Records the transfer and the cash that moved through the drawer.</summary>
    Task<Guid> RecordAsync(NewTransfer transfer);

    /// <summary>Cancels a transfer with a reversal line and gives the money back. Owner or Manager only.</summary>
    Task ReverseAsync(Guid transferId, string reason);
}
