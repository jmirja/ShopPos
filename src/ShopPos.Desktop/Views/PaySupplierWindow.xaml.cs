using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class PaySupplierWindow : Window
{
    private readonly PaySupplierViewModel _vm;

    public PaySupplierWindow(PaySupplierViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await _vm.LoadAsync();
            AmountBox.Focus();
        };
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.ConfirmAsync()) DialogResult = true;
    }
}
