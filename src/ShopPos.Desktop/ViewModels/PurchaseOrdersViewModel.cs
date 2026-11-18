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

public record PoFilterOption(PoFilter Filter, string Label);

/// <summary>One editable line of a purchase order.</summary>
public partial class PoLineEdit : ObservableObject
{
    public PoLineEdit(Guid variantId, string name, decimal qty, decimal cost, decimal received)
    {
        VariantId = variantId;
        Name = name;
        QtyReceived = received;
        _qtyText = qty.ToString("0.###", CultureInfo.InvariantCulture);
        _costText = cost.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public Guid VariantId { get; }
    public string Name { get; }
    public decimal QtyReceived { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Qty))]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private string _qtyText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cost))]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private string _costText;

    public decimal Qty => Parse(QtyText);
    public decimal Cost => Parse(CostText);
    public decimal LineTotal => Math.Round(Qty * Cost, 2, MidpointRounding.AwayFromZero);

    public bool IsValid =>
        decimal.TryParse(QtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var q) && q > 0 &&
        decimal.TryParse(CostText, NumberStyles.Number, CultureInfo.InvariantCulture, out var c) && c >= 0;

    private static decimal Parse(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : 0m;
}

public partial class PurchaseOrdersViewModel : ObservableObject
{
    private readonly IPurchaseService _purchases;
    private readonly ISupplierService _suppliers;
    private readonly IBillingService _billing;
    private readonly ISalesService _sales;

    public PurchaseOrdersViewModel(IPurchaseService purchases, ISupplierService suppliers,
        IBillingService billing, ISalesService sales)
    {
        _purchases = purchases;
        _suppliers = suppliers;
        _billing = billing;
        _sales = sales;
        _selectedFilter = Filters[0];

        Lines.CollectionChanged += OnLinesChanged;
    }

    public PoFilterOption[] Filters { get; } =
    {
        new(PoFilter.Open, "Open orders"),
        new(PoFilter.All, "All orders"),
        new(PoFilter.Received, "Fully received"),
        new(PoFilter.Cancelled, "Cancelled")
    };

    public ObservableCollection<PoRow> Rows { get; } = new();
    public ObservableCollection<SupplierOption> SupplierOptions { get; } = new();
    public ObservableCollection<PoLineEdit> Lines { get; } = new();
    public ObservableCollection<LookupMatch> ItemResults { get; } = new();

    // ------------------------------------------------------------------ list
    [ObservableProperty] private PoFilterOption _selectedFilter;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private PoRow? _selectedRow;

    // -------------------------------------------------------------- the order
    // Detail is null while a NEW order is being typed in.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanMarkOrdered))]
    [NotifyPropertyChangedFor(nameof(CanReceive))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(OrderTitle))]
    private PoDetail? _detail;

    [ObservableProperty] private SupplierOption? _selectedSupplier;
    [ObservableProperty] private DateTime? _orderDate = DateTime.Today;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private string _itemSearchText = string.Empty;
    [ObservableProperty] private string _itemQtyText = "1";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool IsNew => Detail is null;
    public bool CanEdit => Detail is null || Detail.IsDraft;
    public bool CanMarkOrdered => Detail is { IsDraft: true };
    public bool CanReceive => Detail is { CanReceive: true };
    public bool CanCancel => Detail is { Status: PoStatus.Draft or PoStatus.Ordered };

    public string OrderTitle => Detail is null ? "New purchase order" : $"{Detail.PoNo}  -  {PoStatus.Label(Detail.Status)}";
    public string TotalText => $"Order total:  {Lines.Sum(l => l.LineTotal):N2}";

    // ================================================================ loading

    public async Task LoadAsync()
    {
        await LoadSuppliersAsync();
        await ReloadAsync(null);
        StartNew();
    }

    /// <summary>Reloads the list and the open order (used after goods were received).</summary>
    public async Task RefreshAsync()
    {
        var id = Detail?.Id;
        await LoadSuppliersAsync();
        await ReloadAsync(id);
        if (id is Guid g) await LoadDetailAsync(g);
    }

    private async Task LoadSuppliersAsync()
    {
        try
        {
            var options = await _suppliers.GetOptionsAsync();
            var keep = SelectedSupplier?.Id;

            SupplierOptions.Clear();
            foreach (var o in options) SupplierOptions.Add(o);
            SelectedSupplier = SupplierOptions.FirstOrDefault(o => o.Id == keep);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    partial void OnSelectedFilterChanged(PoFilterOption value) => _ = ReloadAsync(null);

    partial void OnSelectedRowChanged(PoRow? value)
    {
        if (value is not null) _ = LoadDetailAsync(value.Id);
    }

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync(null);

    private async Task ReloadAsync(Guid? keepSelected)
    {
        try
        {
            var rows = await _purchases.ListOrdersAsync(SelectedFilter.Filter, SearchText);

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            if (keepSelected is Guid id)
                SelectedRow = Rows.FirstOrDefault(r => r.Id == id);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private async Task LoadDetailAsync(Guid id)
    {
        try
        {
            ErrorMessage = null;

            var d = await _purchases.GetOrderAsync(id);
            if (d is null) return;

            var supplierOption = SupplierOptions.FirstOrDefault(o => o.Id == d.SupplierId);
            if (supplierOption is null)                      // an inactive supplier is not in the list
            {
                supplierOption = new SupplierOption(d.SupplierId, d.SupplierName);
                SupplierOptions.Add(supplierOption);
            }
            SelectedSupplier = supplierOption;
            OrderDate = d.OrderDate.ToDateTime(TimeOnly.MinValue);
            Notes = d.Notes ?? string.Empty;

            Lines.Clear();
            foreach (var l in d.Lines)
                Lines.Add(new PoLineEdit(l.VariantId, l.Name, l.QtyOrdered, l.UnitCost, l.QtyReceived));

            ItemResults.Clear();
            Detail = d;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PoLineEdit l in e.OldItems) l.PropertyChanged -= OnLineChanged;
        if (e.NewItems is not null)
            foreach (PoLineEdit l in e.NewItems) l.PropertyChanged += OnLineChanged;
        OnPropertyChanged(nameof(TotalText));
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(TotalText));

    // ================================================================= editing

    [RelayCommand]
    private void NewOrder() => StartNew();

    private void StartNew()
    {
        SelectedRow = null;
        Detail = null;
        SelectedSupplier = null;
        OrderDate = DateTime.Today;
        Notes = string.Empty;
        Lines.Clear();
        ItemSearchText = string.Empty;
        ItemQtyText = "1";
        ItemResults.Clear();
        ErrorMessage = null;
        InfoMessage = null;
    }

    /// <summary>Saves the order as a draft. Returns its id, or null (with a message) when something is wrong.</summary>
    private async Task<Guid?> SaveCoreAsync()
    {
        ErrorMessage = null;

        if (SelectedSupplier is null)
        {
            ErrorMessage = "Choose the supplier.";
            return null;
        }
        if (Lines.Count == 0)
        {
            ErrorMessage = "Add at least one item to the order.";
            return null;
        }
        if (Lines.Any(l => !l.IsValid))
        {
            ErrorMessage = "Check the quantities and costs: the quantity must be above zero and both must be numbers.";
            return null;
        }

        try
        {
            var id = await _purchases.SaveOrderAsync(new PoSave(
                Detail?.Id ?? Guid.Empty,
                SelectedSupplier.Id,
                DateOnly.FromDateTime(OrderDate ?? DateTime.Today),
                Notes,
                Lines.Select(l => new PoLineInput(l.VariantId, l.Qty, l.Cost)).ToList()));

            await ReloadAsync(id);
            await LoadDetailAsync(id);
            return id;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        InfoMessage = null;
        if (await SaveCoreAsync() is not null)
            InfoMessage = "Order saved as a draft.";
    }

    [RelayCommand]
    private async Task MarkOrderedAsync()
    {
        InfoMessage = null;
        var id = await SaveCoreAsync();
        if (id is null) return;

        try
        {
            await _purchases.MarkOrderedAsync(id.Value);
            await ReloadAsync(id);
            await LoadDetailAsync(id.Value);
            InfoMessage = "Order marked as ordered. It can no longer be changed; receive the goods when they arrive.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    public async Task<bool> CancelOrderAsync(string reason)
    {
        if (Detail is null) return false;
        ErrorMessage = null;
        InfoMessage = null;

        var id = Detail.Id;
        try
        {
            await _purchases.CancelOrderAsync(id, reason);
            await ReloadAsync(id);
            await LoadDetailAsync(id);
            InfoMessage = "Order cancelled.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    // ============================================================ order items

    [RelayCommand]
    private async Task SubmitItemAsync()
    {
        var term = ItemSearchText.Trim();
        if (!CanEdit || term.Length == 0) return;

        ErrorMessage = null;
        InfoMessage = null;

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
                InfoMessage = $"{result.Candidates.Count} matches. Click the item you want.";
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
        if (match is null || !CanEdit) return;
        ErrorMessage = null;
        InfoMessage = null;
        AddItem(match.Item);
    }

    private void AddItem(SellableItem item)
    {
        if (item.IsService)
        {
            ErrorMessage = "A service cannot be ordered.";
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
            Lines.Add(new PoLineEdit(item.VariantId, item.DisplayName, qty, item.CostPrice, 0));

        ItemSearchText = string.Empty;
        ItemQtyText = "1";
        ItemResults.Clear();
    }

    [RelayCommand]
    private void RemoveLine(PoLineEdit? line)
    {
        if (line is not null && CanEdit) Lines.Remove(line);
    }

    // ============================================================= receiving

    public ReceiveGoodsViewModel? CreateReceiveViewModel() =>
        Detail is { CanReceive: true } d
            ? new ReceiveGoodsViewModel(_purchases, _billing, _sales, d.SupplierId, d.SupplierName, d)
            : null;
}
