using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Stock;

namespace ShopPos.Desktop.ViewModels;

public record ActionOption(StockAction Action, string Label);

public partial class StockViewModel : ObservableObject
{
    private readonly IStockService _stock;

    public StockViewModel(IStockService stock)
    {
        _stock = stock;
        _selectedAction = ActionOptions[0];
    }

    public ObservableCollection<StockRow> Rows { get; } = new();
    public ObservableCollection<MovementRow> History { get; } = new();

    public ActionOption[] ActionOptions { get; } =
    {
        new(StockAction.Opening,  "Opening stock (what is on the shelf now)"),
        new(StockAction.Add,      "Add stock (found / returned to shelf)"),
        new(StockAction.Damaged,  "Remove: damaged"),
        new(StockAction.Lost,     "Remove: lost / stolen"),
        new(StockAction.SetCount, "Set counted quantity (stock take)")
    };

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _lowOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(CanAdjust))]
    [NotifyPropertyChangedFor(nameof(BlockedMessage))]
    private StockRow? _selectedRow;

    [ObservableProperty] private ActionOption? _selectedAction;
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public string SelectedTitle => SelectedRow is null
        ? "Select an item from the list."
        : $"{SelectedRow.DisplayName}  (on hand: {SelectedRow.QtyOnHand:0.###})";

    public bool CanAdjust => SelectedRow is not null && !SelectedRow.NeedsSpecialTracking;

    public string? BlockedMessage => SelectedRow is { NeedsSpecialTracking: true }
        ? "This item tracks IMEI / serial numbers or batches, so its stock is added by entering them. That comes in the next step."
        : null;

    public async Task LoadAsync(bool lowOnly)
    {
        _lowOnly = lowOnly;                      // set the field directly so we only reload once
        OnPropertyChanged(nameof(LowOnly));
        await ReloadAsync(null);
    }

    partial void OnLowOnlyChanged(bool value) => _ = ReloadAsync(null);

    partial void OnSelectedRowChanged(StockRow? value)
    {
        if (value is null) History.Clear();
        else _ = LoadHistoryAsync(value.VariantId);
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync(null);

    private async Task ReloadAsync(Guid? keepSelected)
    {
        try
        {
            var rows = await _stock.GetStockAsync(SearchText, LowOnly);

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            var low = rows.Count(r => r.IsLow);
            Summary = low > 0 ? $"{low} item(s) are out of stock or at/below their reorder level." : string.Empty;

            if (keepSelected is Guid id)
            {
                SelectedRow = Rows.FirstOrDefault(r => r.VariantId == id);
                if (SelectedRow is not null) await LoadHistoryAsync(id);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private async Task LoadHistoryAsync(Guid variantId)
    {
        try
        {
            var list = await _stock.GetMovementsAsync(variantId);
            History.Clear();
            foreach (var m in list) History.Add(m);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (SelectedRow is null || SelectedAction is null)
        {
            ErrorMessage = "Select an item first.";
            return;
        }
        if (!decimal.TryParse(QuantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
        {
            ErrorMessage = "Enter a valid quantity (numbers only, for example 5 or 2.5).";
            return;
        }

        try
        {
            var variantId = SelectedRow.VariantId;
            var newQty = await _stock.AdjustStockAsync(
                new StockAdjustmentRequest(variantId, SelectedAction.Action, qty, Note));

            QuantityText = string.Empty;
            Note = null;
            await ReloadAsync(variantId);
            InfoMessage = $"Done. Quantity on hand is now {newQty:0.###}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }
}
