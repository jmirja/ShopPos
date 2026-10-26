using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;
using ShopPos.Application.Billing;
using ShopPos.Application.Customers;
using ShopPos.Application.Sales;
using ShopPos.Application.Shifts;
using ShopPos.Desktop.Receipts;

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
    private readonly ISalesService _sales;
    private readonly ICustomerService _customers;
    private readonly IShiftService _shifts;
    private readonly IHeldBillService _held;
    private readonly ISaleVoidService _voids;
    private readonly ICurrentUser _currentUser;
    private CancellationTokenSource? _searchCts;
    private bool _allowNegative;
    private bool _adjusting;

    public BillingViewModel(IBillingService billing, ISalesService sales, ICustomerService customers,
        ReceiptCoordinator receipts, IShiftService shifts, IHeldBillService held, ISaleVoidService voids,
        ICurrentUser currentUser)
    {
        Receipts = receipts;
        _shifts = shifts;
        _held = held;
        _voids = voids;
        _currentUser = currentUser;
        _billing = billing;
        _sales = sales;
        _customers = customers;

        Cart.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (CartLine l in e.OldItems) l.PropertyChanged -= OnLinePropertyChanged;
            if (e.NewItems is not null)
                foreach (CartLine l in e.NewItems) l.PropertyChanged += OnLinePropertyChanged;

            // starting a new bill: the "sale saved" message of the previous one is no longer needed
            if (e.Action == NotifyCollectionChangedAction.Add) LastSaleText = null;

            Recalculate();
        };

        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>Prints or previews receipts (used by the billing view after a sale).</summary>
    public ReceiptCoordinator Receipts { get; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomerText))]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    private CustomerDto? _customer;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLastSale))] private string? _lastSaleText;

    public bool HasResults => Results.Count > 0;
    public bool HasLastSale => LastSaleText is not null;
    public bool HasCustomer => Customer is not null;
    public decimal Total => Subtotal;           // discounts and VAT will be added later

    public string CustomerText => Customer is null
        ? "Walk-in customer"
        : string.IsNullOrWhiteSpace(Customer.Phone) ? Customer.Name : $"{Customer.Name} ({Customer.Phone})";

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

        await RefreshShiftAsync();
        await RefreshHeldCountAsync();
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
        ResetBill();
        ErrorMessage = null;
        InfoMessage = null;
        FocusSearchRequested?.Invoke();
    }

    private void ResetBill()
    {
        Cart.Clear();
        Results.Clear();
        SearchText = string.Empty;
        Customer = null;
    }

    // ============================================================== customer

    public CustomerPickerViewModel CreateCustomerPicker() => new(_customers);

    [RelayCommand]
    private void RemoveCustomer() => Customer = null;

    // ============================================================== paying

    /// <summary>Checks the bill and returns the view-model for the payment window (null if the bill cannot be paid yet).</summary>
    public async Task<PaymentViewModel?> PreparePaymentAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (Cart.Count == 0)
        {
            ErrorMessage = "The bill is empty.";
            return null;
        }
        if (Total <= 0)
        {
            ErrorMessage = "The bill total must be greater than zero.";
            return null;
        }

        try
        {
            var methods = await _sales.GetPaymentMethodsAsync();
            if (methods.Count == 0)
            {
                ErrorMessage = "No payment methods are set up.";
                return null;
            }

            return new PaymentViewModel(Total, methods, HasCustomer);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    /// <summary>Saves the sale after the payment window was confirmed.</summary>
    public async Task<CompletedSale?> CompleteSaleAsync(PaymentViewModel payment)
    {
        CompletedSale? saved = null;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            var request = new CompleteSaleRequest(
                Customer?.Id,
                Cart.Select(l => new SaleLineRequest(l.VariantId, l.SerialItemId, l.Qty, l.UnitPrice)).ToList(),
                payment.BuildPayments(),
                null);

            var done = await _sales.CompleteSaleAsync(request);
            saved = done;

            Cart.Clear();
            Results.Clear();
            SearchText = string.Empty;
            Customer = null;

            var text = $"Sale {done.InvoiceNo} saved.";
            if (done.Change > 0) text += $"\nChange to give: {done.Change:N2}";
            if (done.Due > 0) text += $"\nDue added to the customer: {done.Due:N2}";
            LastSaleText = text;
        }
        catch (Exception ex)
        {
            // the bill is kept so the cashier can fix the problem and try again
            ErrorMessage = UiErrors.Describe(ex);
        }

        FocusSearchRequested?.Invoke();
        return saved;
    }

    // ================================================================== shift

    private ShiftInfo? _openShift;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoOpenShift))] private bool _hasOpenShift;
    [ObservableProperty] private string _shiftText = "Shift: checking...";

    public bool HasNoOpenShift => !HasOpenShift;

    public async Task RefreshShiftAsync()
    {
        try
        {
            _openShift = await _shifts.GetOpenShiftAsync();
            HasOpenShift = _openShift is not null;
            ShiftText = _openShift is null
                ? "Shift: not open"
                : $"Shift open since {_openShift.OpenedAtText}\nOpening cash: {_openShift.OpeningCash:N2}";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    /// <summary>True when payments are allowed: either shifts are not required, or this cashier has an open shift.</summary>
    public async Task<bool> IsShiftOkAsync()
    {
        try
        {
            if (!await _shifts.IsShiftRequiredAsync()) return true;
            return await _shifts.GetOpenShiftAsync() is not null;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    public async Task<bool> OpenShiftAsync(decimal openingCash)
    {
        ErrorMessage = null;
        try
        {
            await _shifts.OpenShiftAsync(openingCash);
            await RefreshShiftAsync();
            InfoMessage = "Shift opened.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    public async Task<ShiftSummary?> GetShiftSummaryAsync()
    {
        ErrorMessage = null;
        try
        {
            var summary = await _shifts.GetOpenShiftSummaryAsync();
            if (summary is null) ErrorMessage = "You do not have an open shift.";
            return summary;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    public ShiftCloseViewModel CreateShiftCloseViewModel(ShiftSummary summary) => new(_shifts, summary);

    public async Task AfterShiftClosedAsync(ShiftSummary closed)
    {
        var difference = closed.Difference ?? 0;
        var verdict = difference == 0 ? "no difference" : difference > 0 ? $"over by {difference:N2}" : $"short by {-difference:N2}";
        InfoMessage = $"Shift closed. Expected {closed.ExpectedCash:N2}, counted {closed.CountedCash:N2}: {verdict}.";
        await RefreshShiftAsync();
    }

    // ============================================================ held bills

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HeldButtonText))] private int _heldCount;

    public string HeldButtonText => HeldCount > 0 ? $"Held bills ({HeldCount})" : "Held bills";

    public async Task RefreshHeldCountAsync()
    {
        try
        {
            HeldCount = await _held.CountAsync();
        }
        catch
        {
            // the counter is only a convenience; never let it break the screen
        }
    }

    private List<SaleLineRequest> BuildLineRequests() =>
        Cart.Select(l => new SaleLineRequest(l.VariantId, l.SerialItemId, l.Qty, l.UnitPrice)).ToList();

    /// <summary>Puts the current bill on one side so the next customer can be served.</summary>
    public async Task<bool> HoldAsync(string? note)
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (Cart.Count == 0)
        {
            ErrorMessage = "There is nothing to hold: the bill is empty.";
            return false;
        }

        try
        {
            var text = string.IsNullOrWhiteSpace(note) ? Customer?.Name : note;
            await _held.HoldAsync(new HoldRequest(Customer?.Id, text, BuildLineRequests()));

            ResetBill();
            await RefreshHeldCountAsync();
            InfoMessage = "Bill put on hold. You can bring it back with Held bills.";
            FocusSearchRequested?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    public HeldBillsViewModel CreateHeldBillsViewModel() => new(_held);

    /// <summary>Loads a held bill back into the cart. Items that cannot be sold any more are left out and reported.</summary>
    public async Task ResumeAsync(HeldBill bill)
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (Cart.Count > 0)
        {
            ErrorMessage = "Finish or hold the current bill first, then resume the other one.";
            return;
        }

        try
        {
            var warnings = new List<string>();

            foreach (var l in bill.Lines)
            {
                var item = await _billing.GetSellableItemAsync(l.VariantId);
                if (item is null)
                {
                    warnings.Add("An item is no longer available and was left out.");
                    continue;
                }

                CartLine line;
                if (l.SerialItemId is Guid serialId)
                {
                    // the sale itself checks again that this IMEI is still in stock
                    line = new CartLine(item, serialId, l.SerialNo, 1, 1);
                }
                else if (item.IsService)
                {
                    line = new CartLine(item, null, null, l.Qty, null);
                }
                else
                {
                    var available = await _billing.GetAvailableQtyAsync(item.VariantId);
                    var qty = l.Qty;
                    if (!_allowNegative && qty > available) qty = Math.Max(available, 0m);

                    if (qty <= 0)
                    {
                        warnings.Add($"'{item.DisplayName}' is out of stock and was left out.");
                        continue;
                    }
                    if (qty < l.Qty)
                        warnings.Add($"Only {qty:0.###} of '{item.DisplayName}' are in stock now (the bill had {l.Qty:0.###}).");

                    line = new CartLine(item, null, null, qty, available);
                }

                line.UnitPrice = l.UnitPrice;              // keep the price the customer was quoted
                Cart.Add(line);
            }

            Customer = bill.Customer;
            await _held.DeleteAsync(bill.Id);
            await RefreshHeldCountAsync();

            if (warnings.Count > 0)
                InfoMessage = "Bill resumed, but please check: " + string.Join(" ", warnings.Distinct());
            else
                InfoMessage = "Bill resumed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }

        FocusSearchRequested?.Invoke();
    }

    // ================================================================== voids

    /// <summary>Only owners and managers see the "Void a sale" button.</summary>
    public bool CanVoid => _currentUser.IsInRole("Owner", "Manager");

    public VoidSaleViewModel CreateVoidViewModel() => new(_voids);
}
