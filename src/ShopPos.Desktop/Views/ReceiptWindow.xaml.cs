using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class ReceiptWindow : Window
{
    public ReceiptWindow(ReceiptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
