using System.Windows;
using System.Windows.Input;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class VoidSaleWindow : Window
{
    private readonly VoidSaleViewModel _vm;

    public VoidSaleWindow(VoidSaleViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => InvoiceBox.Focus();
    }

    private async void Find_Click(object sender, RoutedEventArgs e) => await _vm.FindAsync();

    private async void InvoiceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _vm.FindAsync();
        }
    }

    private async void Void_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Found is null) return;

        var answer = MessageBox.Show(
            $"Void sale {_vm.Found.InvoiceNo} for {_vm.Found.Total:N2}?\n\n" +
            "The items go back into stock and every payment is recorded as refunded. This cannot be undone.",
            "Void sale", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
            await _vm.VoidAsync();
    }
}
