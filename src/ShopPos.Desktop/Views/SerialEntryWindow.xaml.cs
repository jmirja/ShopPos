using System.Windows;
using System.Windows.Controls;
using ShopPos.Desktop.ViewModels;

namespace ShopPos.Desktop.Views;

/// <summary>Type or scan the IMEI / serial numbers of one delivered item, one per line.</summary>
public partial class SerialEntryWindow : Window
{
    private readonly ReceiveLineRow _row;

    public SerialEntryWindow(ReceiveLineRow row, string verb = "arrived")
    {
        InitializeComponent();
        _row = row;

        HeaderText.Text = $"{row.Name}\nScan or type the IMEI / serial numbers, one per line. " +
                          $"You said {row.Qty:0.###} {verb}.";
        SerialBox.Text = row.SerialText;

        Loaded += (_, _) =>
        {
            SerialBox.Focus();
            SerialBox.CaretIndex = SerialBox.Text.Length;
        };
    }

    private int Count() =>
        SerialBox.Text.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private void SerialBox_TextChanged(object sender, TextChangedEventArgs e) =>
        CountText.Text = $"{Count()} entered, {_row.Qty:0.###} expected";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _row.SerialText = SerialBox.Text;
        DialogResult = true;
    }
}
