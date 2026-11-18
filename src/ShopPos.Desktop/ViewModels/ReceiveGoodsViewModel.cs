using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Billing;
using ShopPos.Application.Purchasing;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

/// <summary>One item on the "goods arrived" window.</summary>
public partial class ReceiveLineRow : ObservableObject
{
    public ReceiveLineRow(Guid variantId, Guid? poLineId, string name, bool tracksSerial, bool tracksBatch,
        decimal ordered, decimal alreadyReceived, decimal qty, decimal cost)
    {
        VariantId = variantId;
        PoLineId = poLineId;
        Name = name;
        IsSerial = tracksSerial;
        IsBatch = tracksBatch && !tracksSerial;
        Ordered = ordered;
        AlreadyReceived = alreadyReceived;
        _qtyText = qty.ToString("0.###", CultureInfo.InvariantCulture);
        _costText = cost.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public Guid VariantId { get; }
    public Guid? PoLineId { get; }
    public string Name { get; }
    public bool IsSerial { get; }          // phones: IMEIs are needed
    public bool IsBatch { get; }           // cosmetics: batch number and expiry are needed
    public decimal Ordered { get; }
    public decimal AlreadyReceived { get; }

    public string OrderedText => PoLineId is null ? "" : $"{Ordered:0.###}  ({AlreadyReceived:0.###} arrived)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Qty))]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    [NotifyPropertyChangedFor(nameof(SerialButtonText))]
    private string _qtyText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cost))]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private string _costText;

    [ObservableProperty] private string _batchNo = string.Empty;
    [ObservableProperty] private DateTime? _expiryDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SerialCount))]
    [NotifyPropertyChangedFor(nameof(SerialButtonText))]
    private string _serialText = string.Empty;

    public decimal Qty => Parse(QtyText);
    public decimal Cost => Parse(CostText);
    public decimal LineTotal => Math.Round(Qty * Cost, 2, MidpointRounding.AwayFromZero);
    public bool IsCostValid => decimal.TryParse(CostText, NumberStyles.Number, CultureInfo.InvariantCulture, out var c) && c >= 0;
    public bool IsQtyValid => string.IsNullOrWhiteSpace(QtyText) ||
                              (decimal.TryParse(QtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var q) && q >= 0);

    public IReadOnlyList<string> Serials =>
        SerialText.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public int SerialCount => Serials.Count;
    public string SerialButtonText => IsSerial ? $"IMEIs ({SerialCount} of {Qty:0.###})" : "";

    private static decimal Parse(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : 0m;
}

public partial class ReceiveGoodsViewModel : ObservableObject
{
    private readonly IPurchaseService _purchases;
    private readonly IBillingService _billing;
    private readonly ISalesService _sales;
    private readonly Guid _supplierId;
    private readonly Guid? _poId;

    public ReceiveGoodsViewModel(IPurchaseService purchases, IBillingService billing, ISalesService sales,
        Guid supplierId, string supplierName, PoDetail? po)
    {
        _purchases = purchases;
        _billing = billing;
        _sales = sales;
        _supplierId = supplierId;
        _poId = po?.Id;

        Title = po is null ? $"Receive goods - {supplierName}" : $"Receive goods - {po.PoNo} ({supplierName})";
        IsDirect = po is null;

        Lines.CollectionChanged += OnLinesChanged;

        if (po is not null)
        {
            foreach (var l in po.Lines.Where(l => l.Outstanding > 0))
                Lines.Add(new ReceiveLineRow(l.VariantId, l.Id, l.Name, l.TracksSerial, l.TracksBatch,
                    l.QtyOrdered, l.QtyReceived, l.Outstanding, l.UnitCost));
        }
    }

    public string Title { get; }

    /// <summary>True when there is no purchase order: the items are added by hand.</summary>
    public bool IsDirect { get; }

    public ObservableCollection<ReceiveLineRow> Lines { get; } = new();
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();
    public ObservableCollection<LookupMatch> ItemResults { get; } = new();

    /// <summary>Filled in after <see cref="ConfirmAsync"/> succeeded.</summary>
    public ReceiptResult? Result { get; private set; }

    [ObservableProperty] private string _invoiceNo = string.Empty;
    [ObservableProperty] private DateTime? _receivedDate = DateTime.Today;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DueText))] private string _paidText = "0";
    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _reference = string.Empty;

    [ObservableProperty] private string _itemSearchText = string.Empty;
    [ObservableProperty] private string _itemQtyText = "1";
    [ObservableProperty] private string? _errorMessage;

    public decimal Total => Lines.Where(l => l.Qty > 0).Sum(l => l.LineTotal);
    public string TotalText => $"Total of this delivery:  {Total:N2}";

    public string DueText
    {
        get
        {
            var paid = decimal.TryParse(PaidText, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0m;
            return $"Still owed to the supplier:  {Math.Max(0m, Total - paid):N2}";
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            Methods.Clear();
            foreach (var m in await _sales.GetPaymentMethodsAsync()) Methods.Add(m);
            SelectedMethod = Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault();
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
        OnPropertyChanged(nameof(DueText));
    }

    // ============================================== adding items (no order)

    [RelayCommand]
    private async Task SubmitItemAsync()
    {
        var term = ItemSearchText.Trim();
        if (!IsDirect || term.Length == 0) return;
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
                AddItem(result.Match.Item);
            }
            else if (result.Candidates.Count == 1)
            {
                AddItem(result.Candidates[0].Item);
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
        if (match is not null) AddItem(match.Item);
    }

    private void AddItem(SellableItem item)
    {
        ErrorMessage = null;

        if (item.IsService)
        {
            ErrorMessage = "A service cannot be purchased.";
            return;
        }
        if (!decimal.TryParse(ItemQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
        {
            ErrorMessage = "Type the quantity as a number greater than zero.";
            return;
        }

        var existing = Lines.FirstOrDefault(l => l.VariantId == item.VariantId);
        if (existing is not null)
            existing.QtyText = (existing.Qty + qty).ToString("0.###", CultureInfo.InvariantCulture);
        else
            Lines.Add(new ReceiveLineRow(item.VariantId, null, item.DisplayName, item.TracksSerial, item.TracksBatch,
                0, 0, qty, item.CostPrice));

        ItemSearchText = string.Empty;
        ItemQtyText = "1";
        ItemResults.Clear();
    }

    [RelayCommand]
    private void RemoveLine(ReceiveLineRow? line)
    {
        if (line is not null && IsDirect) Lines.Remove(line);
    }

    // ================================================================ saving

    public async Task<bool> ConfirmAsync()
    {
        ErrorMessage = null;

        var arrived = Lines.Where(l => l.Qty > 0).ToList();
        if (arrived.Count == 0)
        {
            ErrorMessage = "Enter the quantity that arrived for at least one item.";
            return false;
        }
        if (Lines.Any(l => !l.IsQtyValid || !l.IsCostValid))
        {
            ErrorMessage = "Check the quantities and costs: they must be numbers.";
            return false;
        }
        if (!decimal.TryParse(PaidText, NumberStyles.Number, CultureInfo.InvariantCulture, out var paid) || paid < 0)
        {
            ErrorMessage = "Type the amount you pay now as a number (0 if you pay later).";
            return false;
        }

        try
        {
            var request = new ReceiveRequest(
                _supplierId, _poId, InvoiceNo, DateOnly.FromDateTime(ReceivedDate ?? DateTime.Today), Notes,
                arrived.Select(ToReceiveLine).ToList(),
                paid, paid > 0 ? SelectedMethod?.Id : null, Reference);

            Result = await _purchases.ReceiveAsync(request);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    private static ReceiveLine ToReceiveLine(ReceiveLineRow l)
    {
        DateOnly? expiry = null;
        if (l.IsBatch && l.ExpiryDate is { } date)
            expiry = DateOnly.FromDateTime(date);

        return new ReceiveLine(
            l.VariantId, l.PoLineId, l.Qty, l.Cost,
            l.IsBatch ? l.BatchNo : null, expiry,
            l.IsSerial ? l.Serials : new List<string>());
    }
}
