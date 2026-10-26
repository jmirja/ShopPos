using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShopPos.Application.Sales;

namespace ShopPos.Desktop.ViewModels;

public partial class HeldBillsViewModel : ObservableObject
{
    private readonly IHeldBillService _held;

    public HeldBillsViewModel(IHeldBillService held) => _held = held;

    public ObservableCollection<HeldBillInfo> Items { get; } = new();

    /// <summary>The bill the cashier chose to continue (read by the billing screen after the window closes).</summary>
    public HeldBill? Resumed { get; private set; }

    [ObservableProperty] private HeldBillInfo? _selected;
    [ObservableProperty] private string? _errorMessage;

    public async Task LoadAsync()
    {
        try
        {
            var list = await _held.ListAsync();
            Items.Clear();
            foreach (var h in list) Items.Add(h);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    public async Task<bool> ResumeSelectedAsync()
    {
        ErrorMessage = null;
        if (Selected is null)
        {
            ErrorMessage = "Select a bill first.";
            return false;
        }

        try
        {
            var bill = await _held.GetAsync(Selected.Id);
            if (bill is null)
            {
                ErrorMessage = "This bill is no longer there (someone else may have resumed it).";
                await LoadAsync();
                return false;
            }

            Resumed = bill;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
            return false;
        }
    }

    public async Task DeleteSelectedAsync()
    {
        ErrorMessage = null;
        if (Selected is null)
        {
            ErrorMessage = "Select a bill first.";
            return;
        }

        try
        {
            await _held.DeleteAsync(Selected.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }
}
