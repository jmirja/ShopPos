using System.Windows;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

public partial class LabelPrintWindow : Window
{
    public LabelPrintWindow(LabelPrintViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.PreviewCommand.Execute(null);   // show a first preview straight away
    }
}
