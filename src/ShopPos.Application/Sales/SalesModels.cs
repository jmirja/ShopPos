namespace ShopPos.Application.Sales;

public record PaymentMethodDto(int Id, string Name)
{
    /// <summary>Only cash can be over-paid (the customer gets change back).</summary>
    public bool IsCash => string.Equals(Name, "Cash", StringComparison.OrdinalIgnoreCase);
}

public record SaleLineRequest(Guid VariantId, Guid? SerialItemId, decimal Qty, decimal UnitPrice);

/// <summary>For cash, Amount is what the customer handed over (it may be more than the bill).</summary>
public record PaymentRequest(int PaymentMethodId, decimal Amount, string? Reference);

public record CompleteSaleRequest(
    Guid? CustomerId,
    IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<PaymentRequest> Payments,
    string? Note);

public record CompletedSale(Guid SaleId, string InvoiceNo, decimal Total, decimal Paid, decimal Change, decimal Due);

public record PaymentInput(bool IsCash, decimal Amount);

public record PaymentSummary(
    decimal NonCash, decimal CashTendered, decimal CashApplied,
    decimal Paid, decimal Change, decimal Due, string? Error);

/// <summary>
/// The one place that decides how much was paid, how much change to give and how much is still due.
/// The payment window and the saving code both use it, so they can never disagree.
/// </summary>
public static class PaymentCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static PaymentSummary Calculate(decimal total, IEnumerable<PaymentInput> inputs)
    {
        total = Round(total);
        decimal nonCash = 0, cash = 0;

        foreach (var input in inputs)
        {
            var amount = Round(input.Amount);
            if (amount < 0)
                return Failed(total, "Amounts cannot be negative.");

            if (input.IsCash) cash += amount;
            else nonCash += amount;
        }

        if (nonCash > total)
            return Failed(total, "Card and mobile-banking payments cannot be more than the total.");

        var cashNeeded = total - nonCash;
        var cashApplied = Math.Min(cash, cashNeeded);
        var change = cash - cashApplied;
        var paid = nonCash + cashApplied;

        return new PaymentSummary(nonCash, cash, cashApplied, paid, change, total - paid, null);
    }

    private static PaymentSummary Failed(decimal total, string error) =>
        new(0, 0, 0, 0, 0, total, error);
}

public interface ISalesService
{
    Task<List<PaymentMethodDto>> GetPaymentMethodsAsync();

    /// <summary>
    /// Saves the whole sale in one go: invoice number, lines, payments, stock out, IMEIs marked sold.
    /// Either everything is saved or nothing is.
    /// </summary>
    Task<CompletedSale> CompleteSaleAsync(CompleteSaleRequest request);
}
