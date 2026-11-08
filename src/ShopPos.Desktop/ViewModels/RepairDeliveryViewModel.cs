using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShopPos.Desktop.ViewModels;

/// <summary>The window where the labour charge and warranty are set before a device is handed over.</summary>
public partial class RepairDeliveryViewModel : ObservableObject
{
    public RepairDeliveryViewModel(string jobNo, string device, decimal partsTotal, decimal advance, decimal estimate)
    {
        JobNo = jobNo;
        Device = device;
        PartsTotal = partsTotal;
        Advance = advance;

        // Start from the estimate: what is left after the parts is the labour.
        _labourText = Math.Max(0m, estimate - partsTotal).ToString("0.##", CultureInfo.InvariantCulture);
        Recalculate();
    }

    public string JobNo { get; }
    public string Device { get; }
    public decimal PartsTotal { get; }
    public decimal Advance { get; }

    /// <summary>Valid after the window returned true.</summary>
    public decimal Labour { get; private set; }
    public int WarrantyDays { get; private set; }

    [ObservableProperty] private string _labourText;
    [ObservableProperty] private string _warrantyText = "30";

    [ObservableProperty] private decimal _total;
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _refund;
    [ObservableProperty] private string? _errorMessage;

    partial void OnLabourTextChanged(string value) => Recalculate();

    private void Recalculate()
    {
        if (!decimal.TryParse(LabourText, NumberStyles.Number, CultureInfo.InvariantCulture, out var labour) || labour < 0)
        {
            Total = PartsTotal;
            Balance = 0;
            Refund = 0;
            return;
        }

        var total = Math.Round(labour, 2, MidpointRounding.AwayFromZero) + PartsTotal;
        var applied = Math.Min(Advance, total);

        Total = total;
        Balance = total - applied;
        Refund = Advance - applied;
    }

    /// <summary>Checks the typed values. Returns false (with a message) when something is wrong.</summary>
    public bool Validate()
    {
        ErrorMessage = null;

        if (!decimal.TryParse(LabourText, NumberStyles.Number, CultureInfo.InvariantCulture, out var labour) || labour < 0)
        {
            ErrorMessage = "Type the labour charge as a number (0 if there is none).";
            return false;
        }
        if (!int.TryParse(WarrantyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days < 0 || days > 3650)
        {
            ErrorMessage = "Type the warranty as a number of days (0 for none).";
            return false;
        }

        Labour = Math.Round(labour, 2, MidpointRounding.AwayFromZero);
        WarrantyDays = days;
        return true;
    }
}
