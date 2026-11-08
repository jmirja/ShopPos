using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Billing;
using ShopPos.Application.Customers;
using ShopPos.Application.Repairs;
using ShopPos.Application.Sales;
using ShopPos.Desktop.Receipts;

namespace ShopPos.Desktop.ViewModels;

public record FilterOption(RepairFilter Filter, string Label);

public partial class RepairsViewModel : ObservableObject
{
    private readonly IRepairService _repairs;
    private readonly IBillingService _billing;
    private readonly ICustomerService _customers;
    private readonly ISalesService _sales;

    public RepairsViewModel(IRepairService repairs, IBillingService billing, ICustomerService customers,
        ISalesService sales, ReceiptCoordinator receipts)
    {
        _repairs = repairs;
        _billing = billing;
        _customers = customers;
        _sales = sales;
        Receipts = receipts;
        _selectedFilter = Filters[0];
    }

    public ReceiptCoordinator Receipts { get; }

    public FilterOption[] Filters { get; } =
    {
        new(RepairFilter.Open, "Open jobs"),
        new(RepairFilter.All, "All jobs"),
        new(RepairFilter.Delivered, "Delivered"),
        new(RepairFilter.Cancelled, "Cancelled")
    };

    public ObservableCollection<RepairListItem> Rows { get; } = new();
    public ObservableCollection<RepairPartRow> Parts { get; } = new();
    public ObservableCollection<RepairHistoryRow> History { get; } = new();
    public ObservableCollection<LookupMatch> PartResults { get; } = new();

    // ------------------------------------------------------------------ list
    [ObservableProperty] private FilterOption _selectedFilter;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private RepairListItem? _selectedRow;

    // ------------------------------------------------------------ the open job
    // Detail is null while a NEW job is being typed in.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(JobTitle))]
    [NotifyPropertyChangedFor(nameof(CustomerText))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanMarkReady))]
    [NotifyPropertyChangedFor(nameof(CanDeliver))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanTakeAdvance))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private RepairDetail? _detail;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CustomerText))] private CustomerDto? _newCustomer;

    [ObservableProperty] private string _brand = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _imei = string.Empty;
    [ObservableProperty] private string _problem = string.Empty;
    [ObservableProperty] private string _estimateText = "0";
    [ObservableProperty] private string _notes = string.Empty;

    // ------------------------------------------------------------------ parts
    [ObservableProperty] private string _partSearchText = string.Empty;
    [ObservableProperty] private string _partQtyText = "1";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool IsNew => Detail is null;
    public bool CanEdit => Detail is null || Detail.IsOpen;
    public bool CanStart => Detail is { Status: RepairStatus.Received };
    public bool CanMarkReady => Detail is { Status: RepairStatus.Repairing };
    public bool CanDeliver => Detail is { Status: RepairStatus.Ready };
    public bool CanCancel => Detail is { IsOpen: true };
    public bool CanTakeAdvance => Detail is { IsOpen: true };

    public string JobTitle => Detail is null ? "New repair job" : $"{Detail.JobNo}  -  {Detail.Status}";

    public string CustomerText
    {
        get
        {
            if (Detail is not null)
                return string.IsNullOrWhiteSpace(Detail.CustomerPhone) ? Detail.CustomerName : $"{Detail.CustomerName} ({Detail.CustomerPhone})";
            if (NewCustomer is null) return "(no customer chosen yet)";
            return string.IsNullOrWhiteSpace(NewCustomer.Phone) ? NewCustomer.Name : $"{NewCustomer.Name} ({NewCustomer.Phone})";
        }
    }

    public string SummaryText
    {
        get
        {
            if (Detail is null) return "";
            var text = $"Parts: {Detail.PartsTotal:N2}     Advance paid: {Detail.Advance:N2}";
            if (Detail.WarrantyText.Length > 0) text += "\n" + Detail.WarrantyText;
            return text;
        }
    }

    // ================================================================ loading

    public async Task LoadAsync()
    {
        await ReloadAsync(null);
        StartNew();
    }

    partial void OnSelectedFilterChanged(FilterOption value) => _ = ReloadAsync(null);

    partial void OnSelectedRowChanged(RepairListItem? value)
    {
        if (value is not null) _ = LoadDetailAsync(value.Id);
    }

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync(null);

    private async Task ReloadAsync(Guid? keepSelected)
    {
        try
        {
            var rows = await _repairs.ListAsync(SearchText, SelectedFilter.Filter);

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

            var d = await _repairs.GetAsync(id);
            if (d is null) return;

            Brand = d.Brand ?? string.Empty;
            Model = d.Model ?? string.Empty;
            Imei = d.Imei ?? string.Empty;
            Problem = d.Problem;
            EstimateText = d.Estimate.ToString("0.##", CultureInfo.InvariantCulture);
            Notes = d.Notes ?? string.Empty;

            Parts.Clear();
            foreach (var p in d.Parts) Parts.Add(p);

            History.Clear();
            foreach (var h in d.History.Reverse()) History.Add(h);

            PartResults.Clear();
            Detail = d;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    /// <summary>Reloads the open job and the list after a change.</summary>
    private async Task RefreshAfterChangeAsync()
    {
        if (Detail is null) return;
        var id = Detail.Id;
        await ReloadAsync(id);
        await LoadDetailAsync(id);
    }

    // ============================================================== new / save

    [RelayCommand]
    private void NewJob() => StartNew();

    private void StartNew()
    {
        SelectedRow = null;
        Detail = null;
        NewCustomer = null;
        Brand = string.Empty;
        Model = string.Empty;
        Imei = string.Empty;
        Problem = string.Empty;
        Notes = string.Empty;
        EstimateText = "0";
        PartSearchText = string.Empty;
        PartQtyText = "1";
        Parts.Clear();
        History.Clear();
        PartResults.Clear();
        ErrorMessage = null;
        InfoMessage = null;
    }

    /// <summary>Saves the form. Returns the id of the job when a NEW job was created (so its slip can be printed).</summary>
    public async Task<Guid?> SaveJobAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (!decimal.TryParse(EstimateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var estimate) || estimate < 0)
        {
            ErrorMessage = "Type the estimated cost as a number (0 if you do not know yet).";
            return null;
        }

        try
        {
            if (Detail is null)
            {
                if (NewCustomer is null)
                {
                    ErrorMessage = "Choose the customer first.";
                    return null;
                }

                var id = await _repairs.CreateAsync(new RepairIntake(
                    NewCustomer.Id, Brand, Model, Imei, Problem, estimate, Notes));

                await ReloadAsync(id);
                await LoadDetailAsync(id);
                InfoMessage = "Job created. Take an advance payment with the button below if the customer pays one.";
                return id;
            }

            await _repairs.UpdateAsync(new RepairEdit(Detail.Id, Brand, Model, Imei, Problem, estimate, Notes));
            await RefreshAfterChangeAsync();
            InfoMessage = "Job saved.";
            return null;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    public CustomerPickerViewModel CreateCustomerPicker() => new(_customers);

    // ================================================================ status

    [RelayCommand]
    private Task StartRepairAsync() => ChangeStatusAsync(RepairStatus.Repairing);

    [RelayCommand]
    private Task MarkReadyAsync() => ChangeStatusAsync(RepairStatus.Ready);

    private async Task ChangeStatusAsync(string newStatus)
    {
        if (Detail is null) return;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            await _repairs.ChangeStatusAsync(Detail.Id, newStatus, null);
            await RefreshAfterChangeAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    public async Task<bool> CancelJobAsync(string reason)
    {
        if (Detail is null) return false;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            await _repairs.CancelAsync(Detail.Id, reason);
            await RefreshAfterChangeAsync();
            InfoMessage = "Job cancelled.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    // ================================================================= parts

    [RelayCommand]
    private async Task SubmitPartAsync()
    {
        var term = PartSearchText.Trim();
        if (Detail is null || term.Length == 0) return;

        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            var result = await _billing.LookupAsync(term);

            if (result.Message is not null)
            {
                ErrorMessage = result.Message;
                PartSearchText = string.Empty;
            }
            else if (result.Match is not null)
            {
                await AddPartAsync(result.Match.Item);
            }
            else if (result.Candidates.Count == 1)
            {
                await AddPartAsync(result.Candidates[0].Item);
            }
            else
            {
                PartResults.Clear();
                foreach (var m in result.Candidates) PartResults.Add(m);
                InfoMessage = $"{result.Candidates.Count} matches. Click the part you want.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task PickPartAsync(LookupMatch? match)
    {
        if (match is null) return;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            await AddPartAsync(match.Item);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private async Task AddPartAsync(SellableItem item)
    {
        if (Detail is null) return;

        if (item.IsService)
        {
            ErrorMessage = "A service cannot be used as a spare part.";
            return;
        }
        if (item.TracksSerial || item.TracksBatch)
        {
            ErrorMessage = "Parts that are tracked by IMEI or batch cannot be added to a job yet.";
            return;
        }
        if (!decimal.TryParse(PartQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
        {
            ErrorMessage = "Type the quantity as a number greater than zero.";
            return;
        }

        await _repairs.AddPartAsync(Detail.Id, item.VariantId, qty);

        PartSearchText = string.Empty;
        PartQtyText = "1";
        PartResults.Clear();
        await RefreshAfterChangeAsync();
        InfoMessage = $"{item.DisplayName} added to the job.";
    }

    [RelayCommand]
    private async Task RemovePartAsync(RepairPartRow? part)
    {
        if (part is null) return;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            await _repairs.RemovePartAsync(part.Id);
            await RefreshAfterChangeAsync();
            InfoMessage = "Part removed and put back into stock.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ============================================================== advance

    public async Task<AdvanceViewModel?> CreateAdvanceViewModelAsync()
    {
        if (Detail is null) return null;
        ErrorMessage = null;

        try
        {
            var methods = await _sales.GetPaymentMethodsAsync();
            return new AdvanceViewModel(Detail.Id, Detail.JobNo, methods);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    public async Task<bool> TakeAdvanceAsync(AdvanceViewModel advance)
    {
        var request = advance.Build();
        if (request is null) return false;

        ErrorMessage = null;
        try
        {
            await _repairs.TakeAdvanceAsync(request);
            await RefreshAfterChangeAsync();
            InfoMessage = $"Advance payment of {request.Amount:N2} recorded.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    // ============================================================== delivery

    public RepairDeliveryViewModel? CreateDeliveryViewModel() =>
        Detail is null
            ? null
            : new RepairDeliveryViewModel(Detail.JobNo, Detail.Device, Detail.PartsTotal, Detail.Advance, Detail.Estimate);

    /// <summary>The payment window for what is still unpaid (after the advance).</summary>
    public async Task<PaymentViewModel?> PrepareDeliveryPaymentAsync(decimal balance)
    {
        ErrorMessage = null;
        try
        {
            var methods = await _sales.GetPaymentMethodsAsync();
            return new PaymentViewModel(balance, methods, hasCustomer: true);   // a job always has a customer
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    public async Task<CompletedSale?> DeliverAsync(RepairDeliveryViewModel delivery, PaymentViewModel? payment)
    {
        if (Detail is null) return null;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            var done = await _repairs.DeliverAsync(new DeliverRequest(
                Detail.Id, delivery.Labour, delivery.WarrantyDays,
                payment?.BuildPayments() ?? new List<PaymentRequest>()));

            await RefreshAfterChangeAsync();
            var text = $"Device handed over. Bill {done.InvoiceNo} saved.";
            if (done.Change > 0) text += $" Change to give: {done.Change:N2}.";
            if (done.Due > 0) text += $" Added to the customer's due: {done.Due:N2}.";
            InfoMessage = text;
            return done;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }
}
