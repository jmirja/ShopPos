using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Customers;

namespace ShopPos.Desktop.ViewModels;

public partial class CustomerPickerViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private CancellationTokenSource? _cts;

    public CustomerPickerViewModel(ICustomerService customers) => _customers = customers;

    public ObservableCollection<CustomerDto> Results { get; } = new();

    /// <summary>The customer the cashier picked (or just added).</summary>
    public CustomerDto? Chosen { get; private set; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private CustomerDto? _selected;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newPhone = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public Task LoadAsync() => SearchNowAsync(string.Empty);

    partial void OnSearchTextChanged(string value) => _ = SearchDelayedAsync(value);

    private async Task SearchDelayedAsync(string text)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            await SearchNowAsync(text);
        }
        catch (OperationCanceledException)
        {
            // a newer search replaced this one
        }
    }

    private async Task SearchNowAsync(string text)
    {
        try
        {
            var found = await _customers.SearchAsync(text);
            Results.Clear();
            foreach (var c in found) Results.Add(c);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    /// <summary>Use the customer selected in the list. Returns false (with a message) if none is selected.</summary>
    public bool UseSelected()
    {
        ErrorMessage = null;
        if (Selected is null)
        {
            ErrorMessage = "Select a customer from the list first.";
            return false;
        }

        Chosen = Selected;
        return true;
    }

    /// <summary>Add the typed name / phone as a new customer and use it. Returns true on success.</summary>
    public async Task<bool> AddNewAsync()
    {
        ErrorMessage = null;
        try
        {
            Chosen = await _customers.QuickAddAsync(NewName, NewPhone);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }
}
