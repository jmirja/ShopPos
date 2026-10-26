using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    private async void Collect_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CustomersViewModel vm) return;

        var collectVm = vm.CreateCollectViewModel();
        var window = new CollectDueWindow(collectVm) { Owner = Window.GetWindow(this) };

        if (window.ShowDialog() == true && collectVm.Result is not null)
            await vm.AfterCollectedAsync(collectVm.Result);
    }
}
