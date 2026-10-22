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
        _selectedReceive = ReceiveOptions[0];
        _removeReason = RemoveReasons[0];
    }

    // ---------------------------------------------------------------- lists

    public ObservableCollection<StockRow> Rows { get; } = new();
    public ObservableCollection<MovementRow> History { get; } = new();
    public ObservableCollection<SerialRow> Serials { get; } = new();
    public ObservableCollection<BatchRow> Batches { get; } = new();

    public ActionOption[] ActionOptions { get; } =
    {
        new(StockAction.Opening,  "Opening stock (what is on the shelf now)"),
        new(StockAction.Add,      "Add stock (found / returned to shelf)"),
        new(StockAction.Damaged,  "Remove: damaged"),
        new(StockAction.Lost,     "Remove: lost / stolen"),
        new(StockAction.SetCount, "Set counted quantity (stock take)")
    };

    public ActionOption[] ReceiveOptions { get; } =
    {
        new(StockAction.Opening, "Opening stock (already on the shelf)"),
        new(StockAction.Add,     "New stock added")
    };

    public string[] RemoveReasons { get; } = { "Expired", "Damaged", "Lost / stolen" };

    // ------------------------------------------------------------- filters

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _lowOnly;
    [ObservableProperty] private string _summary = string.Empty;

    // ----------------------------------------------------------- selection

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(NothingSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlainMode))]
    [NotifyPropertyChangedFor(nameof(IsSerialMode))]
    [NotifyPropertyChangedFor(nameof(IsBatchMode))]
    private StockRow? _selectedRow;

    public string SelectedTitle => SelectedRow is null
        ? "Select an item from the list."
        : $"{SelectedRow.DisplayName}  (on hand: {SelectedRow.QtyOnHand:0.###})";

    public bool NothingSelected => SelectedRow is null;
    public bool IsPlainMode => SelectedRow is { TracksSerial: false, TracksBatch: false };
    public bool IsSerialMode => SelectedRow is { TracksSerial: true };
    public bool IsBatchMode => SelectedRow is { TracksSerial: false, TracksBatch: true };

    // ----------------------------------------------- plain (quantity) panel

    [ObservableProperty] private ActionOption? _selectedAction;
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private string? _note;

    // ------------------------------------------------------- IMEI / serial

    [ObservableProperty] private ActionOption? _selectedReceive;
    [ObservableProperty] private string _serialInput = string.Empty;
    [ObservableProperty] private SerialRow? _selectedSerial;
    [ObservableProperty] private bool _showAllSerials;

    // --------------------------------------------------------------- batch

    [ObservableProperty] private string _batchNo = string.Empty;
    [ObservableProperty] private DateTime? _expiryDate;
    [ObservableProperty] private string _batchQtyText = string.Empty;
    [ObservableProperty] private BatchRow? _selectedBatch;
    [ObservableProperty] private string _removeQtyText = string.Empty;
    [ObservableProperty] private string _removeReason;

    // ------------------------------------------------------------ messages

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    // ================================================================ loading

    public async Task LoadAsync(bool lowOnly)
    {
        _lowOnly = lowOnly;                      // set the field directly so we only reload once
        OnPropertyChanged(nameof(LowOnly));
        await ReloadAsync(null);
    }

    partial void OnLowOnlyChanged(bool value) => _ = ReloadAsync(null);

    partial void OnShowAllSerialsChanged(bool value)
    {
        if (SelectedRow is { } row) _ = LoadDetailsAsync(row);
    }

    partial void OnSelectedRowChanged(StockRow? value)
    {
        if (value is null)
        {
            History.Clear();
            Serials.Clear();
            Batches.Clear();
            return;
        }

        ErrorMessage = null;
        InfoMessage = null;
        _ = LoadDetailsAsync(value);
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
                if (SelectedRow is not null) await LoadDetailsAsync(SelectedRow);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private async Task LoadDetailsAsync(StockRow row)
    {
        try
        {
            var id = row.VariantId;

            var history = await _stock.GetMovementsAsync(id);
            History.Clear();
            foreach (var m in history) History.Add(m);

            Serials.Clear();
            if (row.TracksSerial)
                foreach (var s in await _stock.GetSerialsAsync(id, ShowAllSerials)) Serials.Add(s);

            Batches.Clear();
            if (!row.TracksSerial && row.TracksBatch)
                foreach (var b in await _stock.GetBatchesAsync(id)) Batches.Add(b);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ============================================== plain quantity adjustments

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
        if (!TryParseQty(QuantityText, out var qty))
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

    // ================================================================== IMEI

    [RelayCommand]
    private async Task AddSerialsAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (SelectedRow is null || SelectedReceive is null)
        {
            ErrorMessage = "Select an item first.";
            return;
        }

        // Numbers can be separated by new lines, spaces, commas or semicolons (a scanner usually sends Enter).
        var numbers = SerialInput.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            var variantId = SelectedRow.VariantId;
            var count = await _stock.AddSerialsAsync(
                variantId, numbers, SelectedReceive.Action == StockAction.Opening, Note);

            SerialInput = string.Empty;
            Note = null;
            await ReloadAsync(variantId);
            InfoMessage = $"Added {count} number(s). Quantity on hand is now {SelectedRow?.QtyOnHand:0.###}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task RemoveSerialAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (SelectedRow is null || SelectedSerial is null)
        {
            ErrorMessage = "Select the IMEI / serial number you want to remove.";
            return;
        }

        try
        {
            var variantId = SelectedRow.VariantId;
            await _stock.RemoveSerialAsync(SelectedSerial.Id, Note);

            Note = null;
            await ReloadAsync(variantId);
            InfoMessage = "Removed from stock.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ================================================================= batch

    [RelayCommand]
    private async Task AddBatchAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (SelectedRow is null || SelectedReceive is null)
        {
            ErrorMessage = "Select an item first.";
            return;
        }
        if (!TryParseQty(BatchQtyText, out var qty))
        {
            ErrorMessage = "Enter a valid quantity (numbers only).";
            return;
        }

        DateOnly? expiry = ExpiryDate is { } d ? DateOnly.FromDateTime(d) : null;

        try
        {
            var variantId = SelectedRow.VariantId;
            await _stock.AddBatchStockAsync(variantId, BatchNo, expiry, qty,
                SelectedReceive.Action == StockAction.Opening, Note);

            BatchNo = string.Empty;
            ExpiryDate = null;
            BatchQtyText = string.Empty;
            Note = null;
            await ReloadAsync(variantId);
            InfoMessage = $"Batch saved. Quantity on hand is now {SelectedRow?.QtyOnHand:0.###}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task RemoveBatchAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (SelectedRow is null || SelectedBatch is null)
        {
            ErrorMessage = "Select the batch you want to remove stock from.";
            return;
        }
        if (!TryParseQty(RemoveQtyText, out var qty))
        {
            ErrorMessage = "Enter a valid quantity to remove.";
            return;
        }

        try
        {
            var variantId = SelectedRow.VariantId;
            await _stock.RemoveBatchStockAsync(SelectedBatch.Id, qty, RemoveReason, Note);

            RemoveQtyText = string.Empty;
            Note = null;
            await ReloadAsync(variantId);
            InfoMessage = $"Removed. Quantity on hand is now {SelectedRow?.QtyOnHand:0.###}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private static bool TryParseQty(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
