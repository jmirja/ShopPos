using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;
using ShopPos.Application.Customers;
using ShopPos.Application.Parcels;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

public record ParcelFilterOption(ParcelFilter Filter, string Label);
public record CourierFilterOption(int? Id, string Label);

/// <summary>Which courier payment the pay window is for.</summary>
public enum ParcelPayKind { CourierCost, CodFromCourier, CodToSender }

public partial class ParcelsViewModel : ObservableObject
{
    private readonly IParcelService _parcels;
    private readonly ICustomerService _customers;
    private readonly ISalesService _sales;
    private readonly ICurrentUser _user;

    public ParcelsViewModel(IParcelService parcels, ICustomerService customers, ISalesService sales, ICurrentUser user)
    {
        _parcels = parcels;
        _customers = customers;
        _sales = sales;
        _user = user;
        _selectedFilter = Filters[0];
        _courierFilter = new CourierFilterOption(null, "All couriers");
    }

    public ParcelFilterOption[] Filters { get; } =
    {
        new(ParcelFilter.Open, "Open parcels (not delivered yet)"),
        new(ParcelFilter.Today, "Booked today"),
        new(ParcelFilter.Last7Days, "Last 7 days"),
        new(ParcelFilter.Last30Days, "Last 30 days"),
        new(ParcelFilter.All, "All parcels")
    };

    public ObservableCollection<ParcelRow> Rows { get; } = new();
    public ObservableCollection<CourierDto> Couriers { get; } = new();
    public ObservableCollection<CourierFilterOption> CourierFilters { get; } = new();
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();
    public ObservableCollection<CourierBalance> Balances { get; } = new();

    // ------------------------------------------------------------------- list
    [ObservableProperty] private ParcelFilterOption _selectedFilter;
    [ObservableProperty] private CourierFilterOption _courierFilter;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _totalsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    [NotifyPropertyChangedFor(nameof(CanTransit))]
    [NotifyPropertyChangedFor(nameof(CanDeliver))]
    [NotifyPropertyChangedFor(nameof(CanReturn))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanPayCodToSender))]
    [NotifyPropertyChangedFor(nameof(CanEditTracking))]
    private ParcelRow? _selectedRow;

    [ObservableProperty] private string _editTrackingText = string.Empty;

    // -------------------------------------------------------------- new parcel
    [ObservableProperty] private CourierDto? _courier;
    [ObservableProperty] private string _trackingNo = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CustomerText))] private CustomerDto? _customer;
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _senderPhone = string.Empty;
    [ObservableProperty] private string _receiverName = string.Empty;
    [ObservableProperty] private string _receiverPhone = string.Empty;
    [ObservableProperty] private string _receiverAddress = string.Empty;
    [ObservableProperty] private string _weightText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MarginText))] private string _chargeText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MarginText))] private string _costText = string.Empty;
    [ObservableProperty] private string _codText = "0";
    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _notes = string.Empty;

    // ----------------------------------------------------- courier settlement
    [ObservableProperty] private CourierBalance? _selectedBalance;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    // ---- what the selected parcel allows
    public bool CanTransit => SelectedRow is { Status: ParcelStatus.Booked };
    public bool CanDeliver => SelectedRow is { Status: ParcelStatus.Booked or ParcelStatus.InTransit };
    public bool CanReturn => SelectedRow is { Status: ParcelStatus.Booked or ParcelStatus.InTransit };
    public bool CanCancel => SelectedRow is { Status: ParcelStatus.Booked } && _user.IsInRole("Owner", "Manager");
    public bool CanEditTracking => SelectedRow is { Status: ParcelStatus.Booked or ParcelStatus.InTransit };
    public bool CanPayCodToSender =>
        SelectedRow is { Status: ParcelStatus.Delivered, CodReceived: true, CodPaidToSender: false } r && r.Cod > 0;

    public bool CanAddCourier => _user.IsInRole("Owner", "Manager");

    public string CustomerText => Customer is null
        ? "No customer linked (optional)"
        : string.IsNullOrWhiteSpace(Customer.Phone) ? Customer.Name : $"{Customer.Name} ({Customer.Phone})";

    public string MarginText
    {
        get
        {
            var margin = ParseOrZero(ChargeText) - ParseOrZero(CostText);
            return $"Your margin on this parcel:  {margin:N2}";
        }
    }

    public string DetailText
    {
        get
        {
            if (SelectedRow is not { } r) return "Select a parcel in the list to see its details.";

            var text = $"{r.CourierName}   Tracking: {(string.IsNullOrWhiteSpace(r.TrackingNo) ? "(none yet)" : r.TrackingNo)}\n" +
                       $"Status: {r.StatusText}     Booked {r.BookedText} by {r.UserName}\n" +
                       $"From: {r.SenderName} {r.SenderPhone}\n" +
                       $"To: {r.ReceiverName} {r.ReceiverPhone}\n{r.ReceiverAddress}\n" +
                       $"Charge {r.Charge:N2}   Courier cost {r.CourierCost:N2}   Margin {r.Margin:N2}" +
                       (r.WeightKg is { } w ? $"   Weight {w:0.###} kg" : "");

            text += r.CostSettled ? "\nCourier paid: yes" : "\nCourier paid: not yet";
            if (r.Cod > 0) text += $"\nCOD {r.Cod:N2}: {r.CodText}";
            if (!string.IsNullOrWhiteSpace(r.Notes)) text += $"\nNotes: {r.Notes}";
            return text;
        }
    }

    // ================================================================ loading

    public async Task LoadAsync()
    {
        try
        {
            await LoadLookupsAsync();
            Courier ??= Couriers.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }

        await ReloadAsync();
    }

    private async Task LoadLookupsAsync()
    {
        var couriers = await _parcels.GetCouriersAsync();
        Couriers.Clear();
        foreach (var c in couriers) Couriers.Add(c);

        CourierFilters.Clear();
        CourierFilters.Add(new CourierFilterOption(null, "All couriers"));
        foreach (var c in couriers) CourierFilters.Add(new CourierFilterOption(c.Id, c.Name));
        CourierFilter = CourierFilters[0];

        if (Courier is null || Couriers.All(c => c.Id != Courier.Id))
            Courier = Couriers.FirstOrDefault();

        var methods = await _sales.GetPaymentMethodsAsync();
        Methods.Clear();
        foreach (var m in methods) Methods.Add(m);
        SelectedMethod ??= Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault();
    }

    partial void OnSelectedFilterChanged(ParcelFilterOption value) => _ = ReloadAsync();
    partial void OnCourierFilterChanged(CourierFilterOption value) => _ = ReloadAsync();

    partial void OnSelectedRowChanged(ParcelRow? value) => EditTrackingText = value?.TrackingNo ?? string.Empty;

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync();

    private async Task ReloadAsync(Guid? keepSelected = null)
    {
        try
        {
            var rows = await _parcels.ListAsync(SelectedFilter.Filter, CourierFilter.Id, SearchText);

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            var live = rows.Where(r => r.Status != ParcelStatus.Cancelled).ToList();
            TotalsText = $"{live.Count} parcel(s)     Charges {live.Sum(r => r.Charge):N2}     " +
                         $"Courier cost {live.Sum(r => r.CourierCost):N2}     Margin {live.Sum(r => r.Margin):N2}";

            if (keepSelected is Guid id)
                SelectedRow = Rows.FirstOrDefault(r => r.Id == id);

            await ReloadBalancesAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private async Task ReloadBalancesAsync()
    {
        var balances = await _parcels.GetCourierBalancesAsync();
        var keep = SelectedBalance?.CourierId;

        Balances.Clear();
        foreach (var b in balances) Balances.Add(b);
        SelectedBalance = Balances.FirstOrDefault(b => b.CourierId == keep);
    }

    // ============================================================== booking

    public CustomerPickerViewModel CreateCustomerPicker() => new(_customers);

    public void UseCustomer(CustomerDto customer)
    {
        Customer = customer;
        SenderName = customer.Name;
        SenderPhone = customer.Phone ?? string.Empty;
    }

    [RelayCommand]
    private void ClearForm()
    {
        TrackingNo = string.Empty;
        Customer = null;
        SenderName = string.Empty;
        SenderPhone = string.Empty;
        ReceiverName = string.Empty;
        ReceiverPhone = string.Empty;
        ReceiverAddress = string.Empty;
        WeightText = string.Empty;
        ChargeText = string.Empty;
        CostText = string.Empty;
        CodText = "0";
        Notes = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task BookAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (Courier is null)
        {
            ErrorMessage = "Choose the courier.";
            return;
        }
        if (!TryParse(ChargeText, out var charge) || !TryParse(CostText, out var cost) || !TryParse(CodText, out var cod))
        {
            ErrorMessage = "Type the charge, the courier's cost and the COD amount as numbers (0 if none).";
            return;
        }

        decimal? weight = null;
        if (!string.IsNullOrWhiteSpace(WeightText))
        {
            if (!TryParse(WeightText, out var w))
            {
                ErrorMessage = "Type the weight in kg as a number, for example 1.5.";
                return;
            }
            weight = w;
        }

        try
        {
            var id = await _parcels.BookAsync(new NewParcel(
                Courier.Id, TrackingNo, Customer?.Id, SenderName, SenderPhone,
                ReceiverName, ReceiverPhone, ReceiverAddress, weight,
                charge, cost, cod, charge > 0 ? SelectedMethod?.Id : null, Notes));

            var margin = charge - cost;
            ClearForm();
            SelectedFilter = Filters[0];
            await ReloadAsync(id);
            InfoMessage = $"Parcel booked. Margin {margin:N2}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ================================================== the selected parcel

    [RelayCommand]
    private Task MarkInTransitAsync() => ChangeStatusAsync(ParcelStatus.InTransit);

    [RelayCommand]
    private Task MarkDeliveredAsync() => ChangeStatusAsync(ParcelStatus.Delivered);

    [RelayCommand]
    private Task MarkReturnedAsync() => ChangeStatusAsync(ParcelStatus.Returned);

    private async Task ChangeStatusAsync(string newStatus)
    {
        if (SelectedRow is null) return;
        ErrorMessage = null;
        InfoMessage = null;

        var id = SelectedRow.Id;
        try
        {
            await _parcels.ChangeStatusAsync(id, newStatus);
            await ReloadAsync(id);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task SaveTrackingAsync()
    {
        if (SelectedRow is null) return;
        ErrorMessage = null;
        InfoMessage = null;

        var id = SelectedRow.Id;
        try
        {
            await _parcels.UpdateTrackingAsync(id, EditTrackingText);
            await ReloadAsync(id);
            InfoMessage = "Tracking number saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    public async Task<bool> CancelParcelAsync(string reason)
    {
        if (SelectedRow is null) return false;
        ErrorMessage = null;
        InfoMessage = null;

        var id = SelectedRow.Id;
        try
        {
            await _parcels.CancelAsync(id, reason);
            await ReloadAsync(id);
            InfoMessage = "Parcel cancelled and the charge was refunded.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    // ============================================== paying / receiving money

    public async Task AddCourierAsync(string name)
    {
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            var created = await _parcels.AddCourierAsync(name, null);
            await LoadLookupsAsync();
            Courier = Couriers.FirstOrDefault(c => c.Id == created.Id);
            InfoMessage = $"Courier '{created.Name}' added.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    /// <summary>Builds the pay window for the chosen action, or returns null (with a message) when there is nothing to do.</summary>
    public async Task<ParcelPayViewModel?> CreatePayViewModelAsync(ParcelPayKind kind)
    {
        ErrorMessage = null;
        InfoMessage = null;

        string title, message;
        decimal amount;

        switch (kind)
        {
            case ParcelPayKind.CourierCost:
                if (SelectedBalance is not { UnpaidCost: > 0 } cost)
                {
                    ErrorMessage = "Select a courier on the settlement tab that you still have to pay.";
                    return null;
                }
                title = "Pay courier";
                message = $"{cost.CourierName}: {cost.UnpaidParcels} parcel(s) not paid yet.";
                amount = cost.UnpaidCost;
                break;

            case ParcelPayKind.CodFromCourier:
                if (SelectedBalance is not { CodPending: > 0 } cod)
                {
                    ErrorMessage = "Select a courier on the settlement tab that owes you COD money.";
                    return null;
                }
                title = "COD money from courier";
                message = $"{cod.CourierName} handed over the COD for {cod.CodPendingParcels} delivered parcel(s).";
                amount = cod.CodPending;
                break;

            default:
                if (SelectedRow is not { } row || !CanPayCodToSender)
                {
                    ErrorMessage = "Select a delivered parcel whose COD money has been received from the courier.";
                    return null;
                }
                title = "Pay COD to the sender";
                message = $"Parcel for {row.ReceiverName}. Pay the COD to {(string.IsNullOrWhiteSpace(row.SenderName) ? "the sender" : row.SenderName)}.";
                amount = row.Cod;
                break;
        }

        try
        {
            var methods = await _sales.GetPaymentMethodsAsync();
            return new ParcelPayViewModel(title, message, amount, methods);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return null;
        }
    }

    public async Task<bool> ConfirmPayAsync(ParcelPayKind kind, ParcelPayViewModel pay)
    {
        if (pay.SelectedMethod is null) return false;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            var methodId = pay.SelectedMethod.Id;
            switch (kind)
            {
                case ParcelPayKind.CourierCost:
                    var paid = await _parcels.PayCourierCostAsync(SelectedBalance!.CourierId, methodId, pay.Reference);
                    InfoMessage = $"Paid {paid:N2} to the courier.";
                    break;

                case ParcelPayKind.CodFromCourier:
                    var received = await _parcels.ReceiveCodAsync(SelectedBalance!.CourierId, methodId, pay.Reference);
                    InfoMessage = $"Recorded {received:N2} of COD money from the courier.";
                    break;

                default:
                    await _parcels.PayCodToSenderAsync(SelectedRow!.Id, methodId, pay.Reference);
                    InfoMessage = $"COD of {pay.Amount:N2} paid to the sender.";
                    break;
            }

            await ReloadAsync(SelectedRow?.Id);
            return true;
        }
        catch (Exception ex)
        {
            pay.ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    // ================================================================ helpers

    private static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static decimal ParseOrZero(string text) => TryParse(text, out var v) ? v : 0m;
}
