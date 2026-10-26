using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

public partial class VoidSaleViewModel : ObservableObject
{
    private readonly ISaleVoidService _service;

    public VoidSaleViewModel(ISaleVoidService service) => _service = service;

    [ObservableProperty] private string _invoiceText = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanVoidNow))]
    private SaleToVoid? _found;

    [ObservableProperty] private string _reasonText = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool CanVoidNow => Found?.CanVoid == true;

    public async Task FindAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        Found = null;

        if (string.IsNullOrWhiteSpace(InvoiceText))
        {
            ErrorMessage = "Type the invoice number, for example INV-000012.";
            return;
        }

        try
        {
            var sale = await _service.FindAsync(InvoiceText);
            if (sale is null)
            {
                ErrorMessage = $"No sale with invoice number '{InvoiceText.Trim()}' was found.";
                return;
            }

            Found = sale;
            if (!sale.CanVoid)
                ErrorMessage = $"This sale cannot be voided: it is already {sale.Status.ToLowerInvariant()}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    public async Task<bool> VoidAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (Found is null) return false;

        try
        {
            await _service.VoidAsync(Found.SaleId, ReasonText);
            InfoMessage = $"Sale {Found.InvoiceNo} was voided. The items are back in stock and the payments are marked as refunded.";
            Found = null;
            ReasonText = string.Empty;
            InvoiceText = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }
}
