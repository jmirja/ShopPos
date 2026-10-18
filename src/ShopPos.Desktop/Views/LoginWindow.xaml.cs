using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        try
        {
            if (await _vm.LoginAsync(PasswordInput.Password))
            {
                DialogResult = true;   // closes the window and tells App.xaml.cs it worked
            }
            else
            {
                PasswordInput.Clear();
                PasswordInput.Focus();
            }
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
