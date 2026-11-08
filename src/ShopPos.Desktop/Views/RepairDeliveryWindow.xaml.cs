using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class RepairDeliveryWindow : Window
{
    private readonly RepairDeliveryViewModel _vm;

    public RepairDeliveryWindow(RepairDeliveryViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            LabourBox.Focus();
            LabourBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Validate()) DialogResult = true;
    }
}
