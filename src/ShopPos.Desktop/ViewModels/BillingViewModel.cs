using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Billing;

namespace ShopPos.Desktop.ViewModels;

/// <summary>One line of the bill that is being built.</summary>
public partial class CartLine : ObservableObject
{
    public CartLine(SellableItem item, Guid? serialItemId, string? serialNo, decimal qty, decimal? available)
    {
        VariantId = item.VariantId;
        Name = item.DisplayName;
        Sku = item.Sku;
        IsService = item.IsService;
        CostPrice = item.CostPrice;
        SerialItemId = serialItemId;
        SerialNo = serialNo;
        Available = available;
        _qty = qty;
        _unitPrice = item.SellingPrice;
    }

    public Guid VariantId { get; }
    public string Name { get; }
    public string Sku { get; }
    public bool IsService { get; }
    public decimal CostPrice { get; }
    public Guid? SerialItemId { get; }
    public string? SerialNo { get; }
    public decimal? Available { get; }          // null for services

    public string DisplayText => SerialNo is null ? Name : $"{Name}  [{SerialNo}]";

    // Services have open prices (repair charges differ). Stock prices are fixed until discounts are built.
    public bool PriceEditable => IsService;
    public bool PriceReadOnly => !IsService;

    // A phone with an IMEI is always exactly one unit.
    public bool QtyEditable => SerialItemId is null;
    public bool QtyReadOnly => SerialItemId is not null;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LineTotal))] private decimal _qty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LineTotal))] private decimal _unitPrice;

    public decimal LineTotal => Qty * UnitPrice;
}

public partial class BillingViewModel : ObservableObject
{
    private readonly IBillingService _billing;
    private CancellationTokenSource? _searchCts;
    private bool _allowNegative;
    private bool _adjusting;

    public BillingViewModel(IBillingService billing)
    {
        _billing = billing;

        Cart.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (CartLine l in e.OldItems) l.PropertyChanged -= OnLinePropertyChanged;
            if (e.NewItems is not null)
                foreach (CartLine l in e.NewItems) l.PropertyChanged += OnLinePropertyChanged;
            Recalculate();
        };

        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    public ObservableCollection<CartLine> Cart { get; } = new();
    public ObservableCollection<LookupMatch> Results { get; } = new();

    /// <summary>The view listens to this to put the cursor back into the search box.</summary>
    public event Action? FocusSearchRequested;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private CartLine? _selectedLine;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _itemCount;
    [ObservableProperty] private bool _hasItems;

    public bool HasResults => Results.Count > 0;
    public decimal Total => Subtotal;           // discounts and VAT will be added later

    public async Task LoadAsync()
    {
        try
        {
            _allowNegative = await _billing.AllowNegativeStockAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
        FocusSearchRequested?.Invoke();
    }

    // ============================================================ searching

    partial void OnSearchTextChanged(string value) => _ = LiveSearchAsync(value);

    private async Task LiveSearchAsync(string text)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();

        try
        {
            if (text.Trim().Length < 2)
            {
                Results.Clear();
                return;
            }

            await Task.Delay(250, cts.Token);                 // wait until typing pauses
            var found = await _billing.SearchAsync(text);
            if (cts.IsCancellationRequested) return;

            Results.Clear();
            foreach (var m in found) Results.Add(m);
        }
        catch (OperationCanceledException)
        {
            // a newer search replaced this one
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    /// <summary>Enter pressed in the search box (a barcode scanner sends Enter after every scan).</summary>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        var input = SearchText.Trim();
        if (input.Length == 0) return;

        _searchCts?.Cancel();
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            var result = await _billing.LookupAsync(input);

            if (result.Message is not null)
            {
                ErrorMessage = result.Message;
                SearchText = string.Empty;              // clear so the next scan does not stick to this one
            }
            else if (result.Match is not null)
            {
                await AddMatchAsync(result.Match);
                SearchText = string.Empty;
            }
            else if (result.Candidates.Count == 1)
            {
                await AddMatchAsync(result.Candidates[0]);
                SearchText = string.Empty;
            }
            else
            {
                Results.Clear();
                foreach (var m in result.Candidates) Results.Add(m);
                InfoMessage = $"{result.Candidates.Count} matches. Click the one you want.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }

        FocusSearchRequested?.Invoke();
    }

    [RelayCommand]
    private async Task PickResultAsync(LookupMatch? match)
    {
        if (match is null) return;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            await AddMatchAsync(match);
            SearchText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }

        FocusSearchRequested?.Invoke();
    }

    // ============================================================== the cart

    private async Task AddMatchAsync(LookupMatch match)
    {
        var item = match.Item;

        // A phone is sold by IMEI, so the product alone is not enough.
        if (item.TracksSerial && match.SerialItemId is null)
        {
            InfoMessage = $"'{item.DisplayName}' is tracked by IMEI. Scan the IMEI (or type its last digits) to add it.";
            return;
        }

        if (match.SerialItemId is Guid serialId)
        {
            if (Cart.Any(l => l.SerialItemId == serialId))
            {
                ErrorMessage = $"IMEI {match.SerialNo} is already on this bill.";
                return;
            }

            Cart.Add(new CartLine(item, serialId, match.SerialNo, 1, 1));
            return;
        }

        var existing = Cart.FirstOrDefault(l => l.VariantId == item.VariantId && l.SerialItemId is null);
        var wanted = (existing?.Qty ?? 0) + 1;

        decimal? available = null;
        if (!item.IsService)
        {
            available = await _billing.GetAvailableQtyAsync(item.VariantId);
            if (!_allowNegative && wanted > available)
            {
                ErrorMessage = available <= 0
                    ? $"'{item.DisplayName}' is out of stock."
                    : $"Not enough stock for '{item.DisplayName}'. Available: {available:0.###}.";
                return;
            }
        }

        if (existing is not null)
            existing.Qty = wanted;
        else
            Cart.Add(new CartLine(item, null, null, 1, available));
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CartLine line || _adjusting) return;

        _adjusting = true;
        try
        {
            if (e.PropertyName == nameof(CartLine.Qty))
            {
                if (line.Qty <= 0)
                {
                    line.Qty = 1;
                    ErrorMessage = "Quantity must be at least 1. Use Remove to delete the line.";
                }
                else if (!line.IsService && !_allowNegative && line.Available is decimal a && line.Qty > a)
                {
                    line.Qty = a;
                    ErrorMessage = $"Only {a:0.###} available in stock.";
                }
            }
            else if (e.PropertyName == nameof(CartLine.UnitPrice) && line.UnitPrice < 0)
            {
                line.UnitPrice = 0;
                ErrorMessage = "The price cannot be negative.";
            }
        }
        finally
        {
            _adjusting = false;
        }

        Recalculate();
    }

    private void Recalculate()
    {
        Subtotal = Cart.Sum(l => l.LineTotal);
        ItemCount = Cart.Sum(l => l.Qty);
        HasItems = Cart.Count > 0;
        OnPropertyChanged(nameof(Total));
    }

    [RelayCommand]
    private void RemoveLine(CartLine? line)
    {
        var target = line ?? SelectedLine;
        if (target is null) return;

        Cart.Remove(target);
        FocusSearchRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        Cart.Clear();
        Results.Clear();
        SearchText = string.Empty;
        ErrorMessage = null;
        InfoMessage = null;
        FocusSearchRequested?.Invoke();
    }
}
