using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ReceiveGoodsWindow : Window
{
    private readonly ReceiveGoodsViewModel _vm;

    public ReceiveGoodsWindow(ReceiveGoodsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _vm.LoadAsync();
    }

    private void Serials_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ReceiveLineRow row) return;
        new SerialEntryWindow(row) { Owner = this }.ShowDialog();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (await _vm.ConfirmAsync()) DialogResult = true;
    }
}
