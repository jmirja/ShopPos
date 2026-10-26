using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Shifts;

namespace ShopPos.Desktop.ViewModels;

public partial class ShiftCloseViewModel : ObservableObject
{
    private readonly IShiftService _shifts;

    public ShiftCloseViewModel(IShiftService shifts, ShiftSummary summary)
    {
        _shifts = shifts;
        Summary = summary;
    }

    /// <summary>What the system expects to find in the drawer.</summary>
    public ShiftSummary Summary { get; }

    /// <summary>The closed shift, filled in after <see cref="CloseAsync"/> succeeded.</summary>
    public ShiftSummary? Result { get; private set; }

    [ObservableProperty] private string _countedText = string.Empty;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _differenceText = "Count the cash in the drawer and type the total.";

    partial void OnCountedTextChanged(string value)
    {
        if (!TryParse(value, out var counted))
        {
            DifferenceText = string.IsNullOrWhiteSpace(value)
                ? "Count the cash in the drawer and type the total."
                : "Type the amount as a number, for example 4500.";
            return;
        }

        var difference = Math.Round(counted - Summary.ExpectedCash, 2, MidpointRounding.AwayFromZero);
        DifferenceText = difference switch
        {
            0 => "The cash matches exactly.",
            > 0 => $"Over by {difference:N2}",
            _ => $"Short by {-difference:N2}"
        };
    }

    public async Task<bool> CloseAsync()
    {
        ErrorMessage = null;

        if (!TryParse(CountedText, out var counted))
        {
            ErrorMessage = "Type the counted cash as a number, for example 4500.";
            return false;
        }

        try
        {
            Result = await _shifts.CloseShiftAsync(counted, Notes);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    private static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0;
}
