using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Sales;
using ShopPos.Application.Suppliers;

namespace ShopPos.Desktop.ViewModels;

public partial class PaySupplierViewModel : ObservableObject
{
    private readonly ISupplierService _suppliers;
    private readonly ISalesService _sales;
    private readonly Guid _supplierId;

    public PaySupplierViewModel(ISupplierService suppliers, ISalesService sales, Guid supplierId, string supplierName)
    {
        _suppliers = suppliers;
        _sales = sales;
        _supplierId = supplierId;
        SupplierName = supplierName;
    }

    public string SupplierName { get; }
    public ObservableCollection<OpenReceiptRow> OpenReceipts { get; } = new();
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();

    /// <summary>Filled in after <see cref="ConfirmAsync"/> succeeded.</summary>
    public SupplierPayResult? Result { get; private set; }

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
            var summary = await _suppliers.GetDueSummaryAsync(_supplierId);
            TotalDue = summary.TotalDue;
            OpeningRemaining = summary.OpeningRemaining;

            OpenReceipts.Clear();
            foreach (var r in summary.OpenReceipts) OpenReceipts.Add(r);

            Methods.Clear();
            foreach (var m in await _sales.GetPaymentMethodsAsync()) Methods.Add(m);
            SelectedMethod = Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault();
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
            ErrorMessage = "Choose how you pay.";
            return false;
        }
        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorMessage = "Type the amount as a number, for example 5000.";
            return false;
        }

        try
        {
            Result = await _suppliers.PayAsync(new PaySupplierRequest(_supplierId, SelectedMethod.Id, amount, Reference));
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }
}
