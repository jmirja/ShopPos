using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

/// <summary>A small window that asks how a courier payment was made. The amount is fixed; only method and reference are chosen.</summary>
public partial class ParcelPayViewModel : ObservableObject
{
    public ParcelPayViewModel(string title, string message, decimal amount, IEnumerable<PaymentMethodDto> methods)
    {
        Title = title;
        Message = message;
        Amount = amount;
        foreach (var m in methods) Methods.Add(m);
        _selectedMethod = Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault();
    }

    public string Title { get; }
    public string Message { get; }
    public decimal Amount { get; }
    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();

    [ObservableProperty] private PaymentMethodDto? _selectedMethod;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public bool Validate()
    {
        ErrorMessage = null;
        if (SelectedMethod is null)
        {
            ErrorMessage = "Choose a payment method.";
            return false;
        }
        return true;
    }
}
