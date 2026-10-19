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
    private void Logout()
    {
        LoggedOut = true;
        LogoutRequested?.Invoke();
    }
}
