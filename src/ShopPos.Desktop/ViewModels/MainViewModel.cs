using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ShopPos.Application.Auth;

namespace ShopPos.Desktop.ViewModels;

/// <summary>Temporary screen shown for modules we have not built yet.</summary>
public record PlaceholderViewModel(string Title, string Message);

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private BillingViewModel? _billing;     // one per login, so the bill is not lost when you open another screen

    public MainViewModel(ICurrentUser user, IServiceProvider services)
    {
        User = user;
        _services = services;
    }

    public ICurrentUser User { get; }

    public bool LoggedOut { get; private set; }
    public event Action? LogoutRequested;

    // Menu visibility by role
    public bool CanManageStock => User.IsInRole("Owner", "Manager");
    public bool CanSeeAccounting => User.IsInRole("Owner", "Manager");
    public bool CanSeeSettings => User.IsInRole("Owner");

    [ObservableProperty] private object? _currentPage;

    [RelayCommand]
    private async Task NavigateAsync(string? page)
    {
        switch (page)
        {
            case "Home":
            case null:
            {
                var vm = _services.GetRequiredService<HomeViewModel>();
                vm.NavigateToLowStock = () => _ = NavigateAsync("LowStock");
                CurrentPage = vm;
                await vm.LoadAsync();
                break;
            }
            case "Billing":
            {
                _billing ??= _services.GetRequiredService<BillingViewModel>();
                CurrentPage = _billing;
                await _billing.LoadAsync();
                break;
            }
            case "Customers":
            {
                var vm = _services.GetRequiredService<CustomersViewModel>();
                CurrentPage = vm;
                await vm.LoadAsync();
                break;
            }
            case "Products":
            {
                var vm = _services.GetRequiredService<ProductsViewModel>();
                CurrentPage = vm;
                await vm.LoadAsync();
                break;
            }
            case "Categories":
            {
                var vm = _services.GetRequiredService<CategoriesViewModel>();
                CurrentPage = vm;
                await vm.LoadAsync();
                break;
            }
            case "Stock":
            case "LowStock":
            {
                var vm = _services.GetRequiredService<StockViewModel>();
                CurrentPage = vm;
                await vm.LoadAsync(lowOnly: page == "LowStock");
                break;
            }
            default:
            {
                CurrentPage = new PlaceholderViewModel(page, $"The {page} screen will be built in a later step.");
                break;
            }
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // A bill that is still open is put on hold automatically so it is not lost.
        if (_billing is { HasItems: true })
        {
            var held = await _billing.HoldAsync("Held automatically when logging out");
            if (!held)
            {
                await NavigateAsync("Billing");          // show the problem to the cashier
                return;
            }
        }

        LoggedOut = true;
        LogoutRequested?.Invoke();
    }
}
