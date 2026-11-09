using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class TransfersView : UserControl
{
    public TransfersView() => InitializeComponent();

    private void ChooseCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TransfersViewModel vm) return;

        var picker = vm.CreateCustomerPicker();
        var window = new CustomerPickerWindow(picker) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true && picker.Chosen is not null)
            vm.UseCustomer(picker.Chosen);
    }

    private async void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TransfersViewModel vm || vm.SelectedRow is null) return;

        var input = new InputWindow("Cancel transfer",
            $"Why is transfer {vm.SelectedRow.ReferenceNo} being cancelled? The money will be given back.")
        {
            Owner = Window.GetWindow(this)
        };
        if (input.ShowDialog() != true) return;

        await vm.ReverseSelectedAsync(input.Result);
    }
}
