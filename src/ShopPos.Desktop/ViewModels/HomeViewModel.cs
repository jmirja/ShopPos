using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;
using ShopPos.Application.Stock;

namespace ShopPos.Desktop.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly ICurrentUser _user;
    private readonly IStockService _stock;

    public HomeViewModel(ICurrentUser user, IStockService stock)
    {
        _user = user;
        _stock = stock;
    }

    /// <summary>Set by MainViewModel so the "View low stock" button can open the stock screen.</summary>
    public Action? NavigateToLowStock { get; set; }

    public string Welcome => $"Welcome, {_user.FullName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLowStock))]
    [NotifyPropertyChangedFor(nameof(LowStockText))]
    private int _lowStockCount;

    public bool HasLowStock => LowStockCount > 0;
    public string LowStockText => $"{LowStockCount} item(s) are out of stock or at/below their reorder level.";

    public async Task LoadAsync()
    {
        // Only owners and managers care about stock alerts.
        if (!_user.IsInRole("Owner", "Manager")) return;

        try
        {
            LowStockCount = await _stock.CountLowStockAsync();
        }
        catch
        {
            LowStockCount = 0;       // the home screen must never fail because of an alert
        }
    }

    [RelayCommand]
    private void OpenLowStock() => NavigateToLowStock?.Invoke();
}
