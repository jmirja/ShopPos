using System.Windows;
using System.Windows.Input;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class CustomerPickerWindow : Window
{
    private readonly CustomerPickerViewModel _vm;

    public CustomerPickerWindow(CustomerPickerViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await _vm.LoadAsync();
            SearchBox.Focus();
        };
    }

    private void UseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.UseSelected()) DialogResult = true;
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Selected is not null && _vm.UseSelected()) DialogResult = true;
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.AddNewAsync()) DialogResult = true;
    }
}
