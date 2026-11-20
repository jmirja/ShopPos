using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Billing;
using ShopPos.Application.Purchasing;
using ShopPos.Application.Sales;
using ShopPos.Application.Suppliers;

namespace ShopPos.Desktop.ViewModels;

/// <summary>Goods that are sent back to a supplier. It reuses the line row of the "goods arrived" window.</summary>
public partial class ReturnGoodsViewModel : ObservableObject
{
    private readonly IPurchaseService _purchases;
    private readonly IBillingService _billing;
    private readonly ISalesService _sales;
    private readonly ISupplierService _suppliers;
    private readonly Guid _supplierId;

    public ReturnGoodsViewModel(IPurchaseService purchases, IBillingService billing, ISalesService sales,
        ISupplierService suppliers, Guid supplierId, string supplierName)
    {
        _purchases = purchases;
        _billing = billing;
        _sales = sales;
        _suppliers = suppliers;
        _supplierId = supplierId;

        Title = $"Return goods - {supplierName}";
        Lines.CollectionChanged += OnLinesChanged;
    }

    public string Title { get; }

    public ObservableCollection<ReceiveLineRow> Lines { get; } = new();
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();
    public ObservableCollection<LookupMatch> ItemResults { get; } = new();

    /// <summary>Filled in after <see cref="ConfirmAsync"/> succeeded.</summary>
    public ReturnResult? Result { get; private set; }

    [ObservableProperty] private string _creditNoteNo = string.Empty;
    [ObservableProperty] private DateTime? _returnDate = DateTime.Today;
    [ObservableProperty] private string _reason = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SplitText))] private string _refundText = "0";
    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _reference = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SplitText))] private decimal _owed;

    [ObservableProperty] private string _itemSearchText = string.Empty;
    [ObservableProperty] private string _itemQtyText = "1";
    [ObservableProperty] private string? _errorMessage;

    public decimal Total => Lines.Where(l => l.Qty > 0).Sum(l => l.LineTotal);
    public string TotalText => $"Value of the return:  {Total:N2}";

    /// <summary>How the value is settled: first against what you owe, the rest as a refund.</summary>
    public string SplitText
    {
        get
        {
            var refund = decimal.TryParse(RefundText, NumberStyles.Number, CultureInfo.InvariantCulture, out var r) ? r : 0m;
            var credit = Math.Max(0m, Total - refund);
            var text = $"Taken off what you owe: {credit:N2}   (you owe {Owed:N2} on unpaid purchases)";
            if (credit > Owed)
                text += $"\nMore than you owe: the supplier must refund at least {Total - Owed:N2}.";
            return text;
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            Methods.Clear();
            foreach (var m in await _sales.GetPaymentMethodsAsync()) Methods.Add(m);
            SelectedMethod = Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault();

            var summary = await _suppliers.GetDueSummaryAsync(_supplierId);
            Owed = summary.OpenReceipts.Sum(o => o.Due);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ReceiveLineRow l in e.OldItems) l.PropertyChanged -= OnLineChanged;
        if (e.NewItems is not null)
            foreach (ReceiveLineRow l in e.NewItems) l.PropertyChanged += OnLineChanged;
        RefreshTotals();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e) => RefreshTotals();

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(SplitText));
    }

    // ================================================================ items

    [RelayCommand]
    private async Task SubmitItemAsync()
    {
        var term = ItemSearchText.Trim();
        if (term.Length == 0) return;
        ErrorMessage = null;

        try
        {
            var result = await _billing.LookupAsync(term);

            if (result.Message is not null)
            {
                ErrorMessage = result.Message;
                ItemSearchText = string.Empty;
            }
            else if (result.Match is not null)
            {
                AddMatch(result.Match);
            }
            else if (result.Candidates.Count == 1)
            {
                AddMatch(result.Candidates[0]);
            }
            else
            {
                ItemResults.Clear();
                foreach (var m in result.Candidates) ItemResults.Add(m);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private void PickItem(LookupMatch? match)
    {
        if (match is not null) AddMatch(match);
    }

    private void AddMatch(LookupMatch match)
    {
        ErrorMessage = null;
        var item = match.Item;

        if (item.IsService)
        {
            ErrorMessage = "A service cannot be returned.";
            return;
        }

        // Scanning the IMEI of a phone adds that phone to the return.
        if (item.TracksSerial && match.SerialNo is string imei)
        {
            var row = Lines.FirstOrDefault(l => l.VariantId == item.VariantId) ?? AddRow(item, 0);

            if (row.Serials.Any(s => string.Equals(s, imei, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorMessage = $"IMEI {imei} is already on this return.";
                return;
            }

            row.SerialText = string.IsNullOrWhiteSpace(row.SerialText) ? imei : row.SerialText.TrimEnd() + "\n" + imei;
            row.QtyText = row.SerialCount.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            if (!decimal.TryParse(ItemQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                ErrorMessage = "Type the quantity as a number greater than zero.";
                return;
            }

            var existing = Lines.FirstOrDefault(l => l.VariantId == item.VariantId);
            if (existing is not null)
                existing.QtyText = (existing.Qty + qty).ToString("0.###", CultureInfo.InvariantCulture);
            else
                AddRow(item, qty);
        }

        ItemSearchText = string.Empty;
        ItemQtyText = "1";
        ItemResults.Clear();
    }

    private ReceiveLineRow AddRow(SellableItem item, decimal qty)
    {
        var row = new ReceiveLineRow(item.VariantId, null, item.DisplayName, item.TracksSerial, item.TracksBatch,
            0, 0, qty, item.CostPrice);
        Lines.Add(row);
        return row;
    }

    [RelayCommand]
    private void RemoveLine(ReceiveLineRow? line)
    {
        if (line is not null) Lines.Remove(line);
    }

    // ================================================================ saving

    public async Task<bool> ConfirmAsync()
    {
        ErrorMessage = null;

        var returning = Lines.Where(l => l.Qty > 0).ToList();
        if (returning.Count == 0)
        {
            ErrorMessage = "Add at least one item with a quantity to send back.";
            return false;
        }
        if (Lines.Any(l => !l.IsQtyValid || !l.IsCostValid))
        {
            ErrorMessage = "Check the quantities and credit amounts: they must be numbers.";
            return false;
        }
        if (!decimal.TryParse(RefundText, NumberStyles.Number, CultureInfo.InvariantCulture, out var refund) || refund < 0)
        {
            ErrorMessage = "Type the refund you receive now as a number (0 if there is none).";
            return false;
        }

        try
        {
            var request = new ReturnRequest(
                _supplierId, CreditNoteNo, DateOnly.FromDateTime(ReturnDate ?? DateTime.Today), Reason,
                returning.Select(l => new ReceiveLine(
                    l.VariantId, null, l.Qty, l.Cost,
                    l.IsBatch ? l.BatchNo : null, null,
                    l.IsSerial ? l.Serials : new List<string>())).ToList(),
                refund, refund > 0 ? SelectedMethod?.Id : null, Reference);

            Result = await _purchases.ReturnAsync(request);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }
}
