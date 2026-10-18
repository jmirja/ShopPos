using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class FirstRunWindow : Window
{
    private readonly FirstRunViewModel _vm;

    public FirstRunWindow(FirstRunViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => FullNameBox.Focus();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = false;
        try
        {
            if (await _vm.CreateAsync(PasswordInput.Password, ConfirmInput.Password))
                DialogResult = true;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }
}
