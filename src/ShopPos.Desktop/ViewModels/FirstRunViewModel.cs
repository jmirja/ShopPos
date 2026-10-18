using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Auth;

namespace ShopPos.Desktop.ViewModels;

public partial class FirstRunViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    public FirstRunViewModel(IAuthService auth) => _auth = auth;

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public async Task<bool> CreateAsync(string password, string confirm)
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(FullName)) { ErrorMessage = "Enter your full name."; return false; }
        if (Username.Trim().Length < 3) { ErrorMessage = "Username must be at least 3 characters."; return false; }
        if (password.Length < 8) { ErrorMessage = "Password must be at least 8 characters."; return false; }
        if (password != confirm) { ErrorMessage = "The two passwords do not match."; return false; }

        try
        {
            await _auth.CreateOwnerAsync(FullName, Username, password);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not create the account: " + ex.Message;
            return false;
        }
    }
}
