using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

/// <summary>One payment method in the payment window (Cash, Card, bKash ...).</summary>
public partial class PaymentRow : ObservableObject
{
    public PaymentRow(PaymentMethodDto method) => Method = method;

    public PaymentMethodDto Method { get; }
    public string Name => Method.Name;
    public bool IsCash => Method.IsCash;
    public bool ShowReference => !Method.IsCash;

    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _referenceText = string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(AmountText);

    public bool IsInvalid =>
        !IsEmpty && (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v < 0);

    public decimal Amount =>
        decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0m;
}

public partial class PaymentViewModel : ObservableObject
{
    public PaymentViewModel(decimal total, IEnumerable<PaymentMethodDto> methods, bool hasCustomer)
    {
        Total = PaymentCalculator.Round(total);
        HasCustomer = hasCustomer;

        foreach (var method in methods)
        {
            var row = new PaymentRow(method);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        Recalculate();
    }

    public decimal Total { get; }
    public bool HasCustomer { get; }
    public ObservableCollection<PaymentRow> Rows { get; } = new();

    [ObservableProperty] private decimal _paid;
    [ObservableProperty] private decimal _change;
    [ObservableProperty] private decimal _due;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _canConfirm;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentRow.AmountText))
            Recalculate();
    }

    private void Recalculate()
    {
        var bad = Rows.FirstOrDefault(r => r.IsInvalid);
        if (bad is not null)
        {
            Set(0, 0, Total, $"Check the amount for {bad.Name}.", false);
            return;
        }

        var s = PaymentCalculator.Calculate(Total, Rows.Select(r => new PaymentInput(r.IsCash, r.Amount)));
        if (s.Error is not null)
        {
            Set(0, 0, Total, s.Error, false);
            return;
        }

        if (s.Due > 0 && !HasCustomer)
        {
            Set(s.Paid, s.Change, s.Due,
                "Part of the bill is unpaid. Collect the full amount, or cancel and choose a customer to sell on credit.",
                false);
            return;
        }

        Set(s.Paid, s.Change, s.Due, null, true);
    }

    private void Set(decimal paid, decimal change, decimal due, string? error, bool canConfirm)
    {
        Paid = paid;
        Change = change;
        Due = due;
        Error = error;
        CanConfirm = canConfirm;
    }

    /// <summary>Puts the amount that is still missing into this row.</summary>
    [RelayCommand]
    private void FillRemaining(PaymentRow? row)
    {
        if (row is null) return;

        var others = Rows.Where(r => r != row).Sum(r => r.Amount);
        var remaining = Math.Max(0m, Total - others);
        row.AmountText = remaining.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public List<PaymentRequest> BuildPayments() =>
        Rows.Where(r => r.Amount > 0)
            .Select(r => new PaymentRequest(
                r.Method.Id,
                r.Amount,
                string.IsNullOrWhiteSpace(r.ReferenceText) ? null : r.ReferenceText.Trim()))
            .ToList();
}
