using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;
using ShopPos.Application.Billing;
using ShopPos.Application.Purchasing;
using ShopPos.Application.Sales;
using ShopPos.Application.Suppliers;

namespace ShopPos.Desktop.ViewModels;

public partial class SuppliersViewModel : ObservableObject
{
    private readonly ISupplierService _suppliers;
    private readonly IPurchaseService _purchases;
    private readonly IBillingService _billing;
    private readonly ISalesService _sales;
    private readonly ICurrentUser _user;

    public SuppliersViewModel(ISupplierService suppliers, IPurchaseService purchases, IBillingService billing,
        ISalesService sales, ICurrentUser user)
    {
        _suppliers = suppliers;
        _purchases = purchases;
        _billing = billing;
        _sales = sales;
        _user = user;
    }

    public ObservableCollection<SupplierRow> Rows { get; } = new();
    public ObservableCollection<SupplierPurchaseRow> Purchases { get; } = new();
    public ObservableCollection<SupplierPaymentRow> Payments { get; } = new();

    public bool CanEditPrivileged => _user.IsInRole("Owner", "Manager");

    // ---- list
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _owingOnly;
    [ObservableProperty] private bool _showInactive;
    [ObservableProperty] private string _totalDueText = string.Empty;
    [ObservableProperty] private SupplierRow? _selectedRow;

    // ---- form
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSupplier))]
    [NotifyPropertyChangedFor(nameof(CanPay))]
    private Guid _supplierId;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _contactPerson;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string _openingDueText = "0";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _formTitle = "New supplier";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPay))] private decimal _currentDue;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool HasSupplier => SupplierId != Guid.Empty;
    public bool CanPay => SupplierId != Guid.Empty && CurrentDue > 0;

    // ============================================================== loading

    public async Task LoadAsync()
    {
        await ReloadAsync(null);
        StartNew();
    }

    public async Task RefreshAsync()
    {
        var id = SupplierId;
        await ReloadAsync(id == Guid.Empty ? null : id);
        if (id != Guid.Empty) await LoadDetailAsync(id);
    }

    partial void OnOwingOnlyChanged(bool value) => _ = ReloadAsync(null);
    partial void OnShowInactiveChanged(bool value) => _ = ReloadAsync(null);

    partial void OnSelectedRowChanged(SupplierRow? value)
    {
        if (value is not null) _ = LoadDetailAsync(value.Id);
    }

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync(null);

    private async Task ReloadAsync(Guid? keepSelected)
    {
        try
        {
            var rows = await _suppliers.ListAsync(SearchText, OwingOnly, ShowInactive);

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            var owed = rows.Where(r => r.Due > 0).Sum(r => r.Due);
            TotalDueText = $"{rows.Count} supplier(s). Total you owe them: {owed:N2}";

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

            var d = await _suppliers.GetAsync(id);
            if (d is null) return;

            SupplierId = d.Id;
            Name = d.Name;
            ContactPerson = d.ContactPerson;
            Phone = d.Phone;
            Address = d.Address;
            OpeningDueText = d.OpeningDue.ToString("0.##", CultureInfo.InvariantCulture);
            IsActive = d.IsActive;
            CurrentDue = d.Due;
            FormTitle = "Supplier";

            Purchases.Clear();
            foreach (var p in await _suppliers.GetPurchasesAsync(id)) Purchases.Add(p);

            Payments.Clear();
            foreach (var p in await _suppliers.GetPaymentsAsync(id)) Payments.Add(p);
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
        SupplierId = Guid.Empty;
        Name = string.Empty;
        ContactPerson = null;
        Phone = null;
        Address = null;
        OpeningDueText = "0";
        IsActive = true;
        CurrentDue = 0;
        FormTitle = "New supplier";
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
            var id = await _suppliers.SaveAsync(new SupplierEdit(SupplierId, Name, ContactPerson, Phone, Address, opening, IsActive));
            await ReloadAsync(id);
            await LoadDetailAsync(id);
            InfoMessage = "Supplier saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ============================================================== paying

    public PaySupplierViewModel CreatePayViewModel() => new(_suppliers, _sales, SupplierId, Name);

    public async Task AfterPaidAsync(SupplierPayResult result)
    {
        await RefreshAsync();
        InfoMessage = result.RemainingDue > 0
            ? $"Paid {result.Paid:N2}. You still owe {result.RemainingDue:N2}."
            : $"Paid {result.Paid:N2}. You owe this supplier nothing now.";
    }

    // ============================================== goods without an order

    public ReceiveGoodsViewModel? CreateDirectReceiveViewModel()
    {
        if (SupplierId == Guid.Empty || !IsActive)
        {
            ErrorMessage = "Select an active supplier first.";
            return null;
        }
        return new ReceiveGoodsViewModel(_purchases, _billing, _sales, SupplierId, Name, null);
    }
}
