using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;
using ShopPos.Application.Customers;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

public partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private readonly ISalesService _sales;
    private readonly ICurrentUser _user;

    public CustomersViewModel(ICustomerService customers, ISalesService sales, ICurrentUser user)
    {
        _customers = customers;
        _sales = sales;
        _user = user;
    }

    public ObservableCollection<CustomerRow> Rows { get; } = new();
    public ObservableCollection<CustomerSaleRow> Purchases { get; } = new();
    public ObservableCollection<CustomerPaymentRow> Payments { get; } = new();

    /// <summary>Old balances and deactivating customers are for owners and managers.</summary>
    public bool CanEditPrivileged => _user.IsInRole("Owner", "Manager");

    // ---- list
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _owingOnly;
    [ObservableProperty] private bool _showInactive;
    [ObservableProperty] private string _totalDueText = string.Empty;
    [ObservableProperty] private CustomerRow? _selectedRow;

    // ---- form
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    [NotifyPropertyChangedFor(nameof(CanCollect))]
    private Guid _customerId;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _openingDueText = "0";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _formTitle = "New customer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCollect))]
    private decimal _currentDue;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool HasCustomer => CustomerId != Guid.Empty;
    public bool CanCollect => CustomerId != Guid.Empty && CurrentDue > 0;

    // ============================================================== loading

    public async Task LoadAsync()
    {
        await ReloadAsync(null);
        StartNew();
    }

    partial void OnOwingOnlyChanged(bool value) => _ = ReloadAsync(null);
    partial void OnShowInactiveChanged(bool value) => _ = ReloadAsync(null);

    partial void OnSelectedRowChanged(CustomerRow? value)
    {
        if (value is not null) _ = LoadDetailAsync(value.Id);
    }

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync(null);

    private async Task ReloadAsync(Guid? keepSelected)
    {
        try
        {
            var rows = await _customers.ListAsync(SearchText, OwingOnly, ShowInactive);

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            var owed = rows.Where(r => r.Due > 0).Sum(r => r.Due);
            TotalDueText = $"{rows.Count} customer(s). Total owed by them: {owed:N2}";

            if (keepSelected is Guid id)
            {
                SelectedRow = Rows.FirstOrDefault(r => r.Id == id);
                if (SelectedRow is null) await LoadDetailAsync(id);
            }
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

            var detail = await _customers.GetAsync(id);
            if (detail is null) return;

            CustomerId = detail.Id;
            Name = detail.Name;
            Phone = detail.Phone;
            Address = detail.Address;
            Notes = detail.Notes;
            OpeningDueText = detail.OpeningDue.ToString("0.##", CultureInfo.InvariantCulture);
            IsActive = detail.IsActive;
            CurrentDue = detail.Due;
            FormTitle = "Customer";

            Purchases.Clear();
            foreach (var s in await _customers.GetSalesAsync(id)) Purchases.Add(s);

            Payments.Clear();
            foreach (var p in await _customers.GetPaymentsAsync(id)) Payments.Add(p);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ================================================================ edit

    [RelayCommand]
    private void New() => StartNew();

    private void StartNew()
    {
        SelectedRow = null;
        CustomerId = Guid.Empty;
        Name = string.Empty;
        Phone = null;
        Address = null;
        Notes = null;
        OpeningDueText = "0";
        IsActive = true;
        CurrentDue = 0;
        FormTitle = "New customer";
        Purchases.Clear();
        Payments.Clear();
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        if (!decimal.TryParse(OpeningDueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var opening) || opening < 0)
        {
            ErrorMessage = "Type the old balance as a number (0 if there is none).";
            return;
        }

        try
        {
            var id = await _customers.SaveAsync(
                new CustomerEdit(CustomerId, Name, Phone, Address, Notes, opening, IsActive));

            await ReloadAsync(id);
            await LoadDetailAsync(id);
            InfoMessage = "Customer saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ============================================================ collecting

    public CollectDueViewModel CreateCollectViewModel() => new(_customers, _sales, CustomerId, Name);

    public async Task AfterCollectedAsync(CollectionResult result)
    {
        var id = CustomerId;
        await ReloadAsync(id);
        await LoadDetailAsync(id);
        InfoMessage = result.RemainingDue > 0
            ? $"Collected {result.Collected:N2}. Still owed: {result.RemainingDue:N2}."
            : $"Collected {result.Collected:N2}. This customer owes nothing now.";
    }
}
