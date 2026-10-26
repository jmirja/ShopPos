using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Customers;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

public partial class CollectDueViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private readonly ISalesService _sales;
    private readonly Guid _customerId;

    public CollectDueViewModel(ICustomerService customers, ISalesService sales, Guid customerId, string customerName)
    {
        _customers = customers;
        _sales = sales;
        _customerId = customerId;
        CustomerName = customerName;
    }

    public string CustomerName { get; }
    public ObservableCollection<OpenSaleRow> OpenSales { get; } = new();
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();

    /// <summary>Filled in after <see cref="ConfirmAsync"/> succeeded.</summary>
    public CollectionResult? Result { get; private set; }

    [ObservableProperty] private decimal _totalDue;
    [ObservableProperty] private decimal _openingRemaining;
    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public async Task LoadAsync()
    {
        try
        {
            var summary = await _customers.GetDueSummaryAsync(_customerId);
            TotalDue = summary.TotalDue;
            OpeningRemaining = summary.OpeningRemaining;

            OpenSales.Clear();
            foreach (var s in summary.OpenSales) OpenSales.Add(s);

            Methods.Clear();
            foreach (var m in await _sales.GetPaymentMethodsAsync()) Methods.Add(m);
            SelectedMethod = Methods.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private void FillFull() => AmountText = TotalDue.ToString("0.##", CultureInfo.InvariantCulture);

    public async Task<bool> ConfirmAsync()
    {
        ErrorMessage = null;

        if (SelectedMethod is null)
        {
            ErrorMessage = "Choose how the customer pays.";
            return false;
        }
        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorMessage = "Type the amount as a number, for example 500.";
            return false;
        }

        try
        {
            Result = await _customers.CollectDueAsync(new CollectDueRequest(_customerId, SelectedMethod.Id, amount, Reference));
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }
}
