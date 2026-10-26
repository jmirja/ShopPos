namespace ShopPos.Application.Shifts;

public record ShiftInfo(Guid Id, DateTime OpenedAtUtc, decimal OpeningCash)
{
    public string OpenedAtText =>
        DateTime.SpecifyKind(OpenedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");
}

public record MethodTotal(string Method, decimal In, decimal Out)
{
    public decimal Net => In - Out;
}

public record ShiftSummary(
    Guid SessionId, DateTime OpenedAtUtc, DateTime? ClosedAtUtc, decimal OpeningCash,
    int SalesCount, decimal SalesTotal,
    IReadOnlyList<MethodTotal> Methods,
    decimal CashIn, decimal CashOut, decimal ExpectedCash,
    decimal? CountedCash, decimal? Difference)
{
    public string OpenedAtText =>
        DateTime.SpecifyKind(OpenedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy HH:mm");
}

/// <summary>
/// A shift is the time between "open the cash drawer" and "close it" for one cashier.
/// Expected cash = opening cash + cash received - cash refunded during the shift.
/// </summary>
public interface IShiftService
{
    /// <summary>The setting shift.required (default: true). When true, payments need an open shift.</summary>
    Task<bool> IsShiftRequiredAsync();

    Task<ShiftInfo?> GetOpenShiftAsync();
    Task<ShiftInfo> OpenShiftAsync(decimal openingCash);

    Task<ShiftSummary?> GetOpenShiftSummaryAsync();
    Task<ShiftSummary> CloseShiftAsync(decimal countedCash, string? notes);
}
