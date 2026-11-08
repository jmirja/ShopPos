using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Repairs;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

/// <summary>The small "take an advance payment" window.</summary>
public partial class AdvanceViewModel : ObservableObject
{
    private readonly Guid _jobId;

    public AdvanceViewModel(Guid jobId, string jobNo, IEnumerable<PaymentMethodDto> methods)
    {
        _jobId = jobId;
        JobNo = jobNo;
        foreach (var m in methods) Methods.Add(m);
        _selectedMethod = Methods.FirstOrDefault();
    }

    public string JobNo { get; }
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();

    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>The request to send to the service, or null (with a message) when something is missing.</summary>
    public AdvanceRequest? Build()
    {
        ErrorMessage = null;

        if (SelectedMethod is null)
        {
            ErrorMessage = "Choose how the customer pays.";
            return null;
        }
        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorMessage = "Type the amount as a number, for example 500.";
            return null;
        }

        return new AdvanceRequest(_jobId, SelectedMethod.Id, amount, Reference);
    }
}
