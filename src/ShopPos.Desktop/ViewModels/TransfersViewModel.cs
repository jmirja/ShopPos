using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;
using ShopPos.Application.Customers;
using ShopPos.Application.Sales;
using ShopPos.Application.Transfers;

namespace ShopPos.Desktop.ViewModels;

public record ProviderOption(string? Key, string Label);
public record DirectionOption(string Key, string Label);

public partial class TransfersViewModel : ObservableObject
{
    private readonly ITransferService _transfers;
    private readonly ICustomerService _customers;
    private readonly ISalesService _sales;
    private readonly ICurrentUser _user;

    public TransfersViewModel(ITransferService transfers, ICustomerService customers, ISalesService sales, ICurrentUser user)
    {
        _transfers = transfers;
        _customers = customers;
        _sales = sales;
        _user = user;

        FilterProviders = new[] { new ProviderOption(null, "All providers") }
            .Concat(TransferProviders.All.Select(p => new ProviderOption(p.Key, p.Label))).ToArray();
        FormProviders = TransferProviders.All.Select(p => new ProviderOption(p.Key, p.Label)).ToArray();

        _filterProvider = FilterProviders[0];
        _provider = FormProviders[0];
        _direction = Directions[0];
    }

    public ProviderOption[] FilterProviders { get; }
    public ProviderOption[] FormProviders { get; }

    public DirectionOption[] Directions { get; } =
    {
        new(TransferDirection.Send, "Send  (customer sends money)"),
        new(TransferDirection.Receive, "Receive  (customer collects money)")
    };

    public ObservableCollection<TransferRow> Rows { get; } = new();
    public ObservableCollection<DailyProviderSummary> SummaryRows { get; } = new();
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();

    // ------------------------------------------------------------ the day's list
    [ObservableProperty] private DateTime? _selectedDay = DateTime.Today;
    [ObservableProperty] private ProviderOption _filterProvider;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _totalsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    [NotifyPropertyChangedFor(nameof(CanReverse))]
    private TransferRow? _selectedRow;

    // ------------------------------------------------------------ the new transfer
    [ObservableProperty] private ProviderOption _provider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSend))]
    [NotifyPropertyChangedFor(nameof(AmountSummary))]
    private DirectionOption _direction;

    [ObservableProperty] private string _referenceNo = string.Empty;
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _senderPhone = string.Empty;
    [ObservableProperty] private string _receiverName = string.Empty;
    [ObservableProperty] private string _receiverPhone = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AmountSummary))] private string _principalText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AmountSummary))] private string _feeText = "0";
    [ObservableProperty] private string _commissionText = "0";
    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CustomerText))] private CustomerDto? _customer;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool IsSend => Direction.Key == TransferDirection.Send;
    public bool CanReverse => SelectedRow is { IsReversal: false, IsCancelled: false } && _user.IsInRole("Owner", "Manager");

    public string CustomerText => Customer is null
        ? "No customer linked (optional)"
        : string.IsNullOrWhiteSpace(Customer.Phone) ? Customer.Name : $"{Customer.Name} ({Customer.Phone})";

    /// <summary>What the cashier has to collect or pay out.</summary>
    public string AmountSummary
    {
        get
        {
            var principal = ParseOrZero(PrincipalText);
            return IsSend
                ? $"Customer pays:  {principal + ParseOrZero(FeeText):N2}"
                : $"Pay out to the customer:  {principal:N2}";
        }
    }

    public string DetailText
    {
        get
        {
            if (SelectedRow is not { } r) return "Select a line in the list to see its details.";

            var text = $"{r.ProviderLabel}  -  {r.Direction}  -  {r.ReferenceNo}\n" +
                       $"Sender: {r.SenderName} {r.SenderPhone}\n" +
                       $"Receiver: {r.ReceiverName} {r.ReceiverPhone}\n" +
                       $"Amount {r.Principal:N2}   Fee {r.Fee:N2}   Your commission {r.Commission:N2}\n" +
                       $"Recorded by {r.UserName} at {r.TimeText}";
            if (!string.IsNullOrWhiteSpace(r.Notes)) text += $"\nNotes: {r.Notes}";
            if (r.StatusText.Length > 0) text += $"\nStatus: {r.StatusText}";
            return text;
        }
    }

    // ================================================================ loading

    public async Task LoadAsync()
    {
        try
        {
            var methods = await _sales.GetPaymentMethodsAsync();
            Methods.Clear();
            foreach (var m in methods) Methods.Add(m);
            SelectedMethod = Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }

        await ReloadAsync();
    }

    partial void OnSelectedDayChanged(DateTime? value) => _ = ReloadAsync();
    partial void OnFilterProviderChanged(ProviderOption value) => _ = ReloadAsync();

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            var day = DateOnly.FromDateTime(SelectedDay ?? DateTime.Today);

            var rows = await _transfers.ListAsync(day, FilterProvider.Key, SearchText);
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            var summary = await _transfers.GetSummaryAsync(day);
            SummaryRows.Clear();
            foreach (var s in summary.Providers) SummaryRows.Add(s);

            TotalsText = $"Your commission on this day: {summary.TotalCommission:N2}     " +
                         $"Net cash through the drawer: {summary.NetCash:N2}";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // =============================================================== recording

    public CustomerPickerViewModel CreateCustomerPicker() => new(_customers);

    /// <summary>Links a customer and fills in their name and phone as the sender (Send) or receiver (Receive).</summary>
    public void UseCustomer(CustomerDto customer)
    {
        Customer = customer;
        if (IsSend)
        {
            SenderName = customer.Name;
            SenderPhone = customer.Phone ?? string.Empty;
        }
        else
        {
            ReceiverName = customer.Name;
            ReceiverPhone = customer.Phone ?? string.Empty;
        }
    }

    [RelayCommand]
    private void ClearForm()
    {
        ReferenceNo = string.Empty;
        SenderName = string.Empty;
        SenderPhone = string.Empty;
        ReceiverName = string.Empty;
        ReceiverPhone = string.Empty;
        PrincipalText = string.Empty;
        FeeText = "0";
        CommissionText = "0";
        Notes = string.Empty;
        Customer = null;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task RecordAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (!TryParse(PrincipalText, out var principal) || principal <= 0)
        {
            ErrorMessage = "Type the transfer amount as a number, for example 25000.";
            return;
        }
        if (!TryParse(FeeText, out var fee) || !TryParse(CommissionText, out var commission))
        {
            ErrorMessage = "Type the fee and your commission as numbers (0 if there is none).";
            return;
        }

        try
        {
            await _transfers.RecordAsync(new NewTransfer(
                Provider.Key!, Direction.Key, ReferenceNo, Customer?.Id,
                SenderName, SenderPhone, ReceiverName, ReceiverPhone,
                principal, fee, commission,
                IsSend ? SelectedMethod?.Id : null,
                Notes));

            var recorded = AmountSummary;
            ClearForm();

            // show the day the transfer belongs to
            if (SelectedDay?.Date != DateTime.Today) SelectedDay = DateTime.Today;
            else await ReloadAsync();

            InfoMessage = $"Transfer recorded.   {recorded}";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ================================================================ reversal

    public async Task<bool> ReverseSelectedAsync(string reason)
    {
        if (SelectedRow is null) return false;
        ErrorMessage = null;
        InfoMessage = null;

        try
        {
            await _transfers.ReverseAsync(SelectedRow.Id, reason);
            await ReloadAsync();
            InfoMessage = "Transfer cancelled. The money was given back and the ledger shows a reversal line.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    // ================================================================ helpers

    private static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static decimal ParseOrZero(string text) => TryParse(text, out var v) ? v : 0m;
}
