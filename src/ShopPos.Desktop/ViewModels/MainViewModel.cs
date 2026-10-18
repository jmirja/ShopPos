using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Auth;

namespace ShopPos.Desktop.ViewModels;

/// <summary>Temporary screen shown for modules we have not built yet.</summary>
public record PlaceholderViewModel(string Title, string Message);

public partial class MainViewModel : ObservableObject
{
    public MainViewModel(ICurrentUser user)
    {
        User = user;
        _currentPage = new PlaceholderViewModel(
            "Home", $"Welcome, {user.FullName}. Choose a module from the menu on the left.");
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
    private void Navigate(string? page)
    {
        var title = page ?? "Home";
        CurrentPage = new PlaceholderViewModel(title, $"The {title} screen will be built in a later step.");
    }

    [RelayCommand]
    private void Logout()
    {
        LoggedOut = true;
        LogoutRequested?.Invoke();
    }
}
