using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Auth;

namespace ShopPos.Desktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;

    public LoginViewModel(IAuthService auth, ICurrentUser currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    // The password is passed in from the PasswordBox (it cannot be data-bound safely).
    public async Task<bool> LoginAsync(string password)
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Enter your username and password.";
            return false;
        }

        try
        {
            var result = await _auth.LoginAsync(Username, password);
            if (!result.Success)
            {
                ErrorMessage = result.Error;
                return false;
            }

            _currentUser.SignIn(result);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not sign in: " + ex.Message;
            return false;
        }
    }
}
