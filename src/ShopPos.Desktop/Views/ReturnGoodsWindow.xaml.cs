using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ReturnGoodsWindow : Window
{
    private readonly ReturnGoodsViewModel _vm;

    public ReturnGoodsWindow(ReturnGoodsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _vm.LoadAsync();
    }

    private void Serials_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ReceiveLineRow row) return;
        new SerialEntryWindow(row, "are being returned") { Owner = this }.ShowDialog();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.ConfirmAsync()) DialogResult = true;
    }
}
