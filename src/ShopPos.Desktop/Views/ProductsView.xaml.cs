using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ProductsView : UserControl
{
    public ProductsView() => InitializeComponent();

    private void PrintLabels_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductsViewModel vm) return;

        var lines = vm.GetLabelLines();
        if (lines.Count == 0)
        {
            MessageBox.Show(
                "Save the product first. Labels are printed for saved variants that have a barcode.",
                "Print barcode labels", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new LabelPrintWindow(new LabelPrintViewModel(lines))
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }
}
