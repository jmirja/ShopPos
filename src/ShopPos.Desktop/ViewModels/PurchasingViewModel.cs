using CommunityToolkit.Mvvm.ComponentModel;

namespace ShopPos.Desktop.ViewModels;

/// <summary>The "Suppliers and purchases" screen: one tab for suppliers, one for purchase orders.</summary>
public class PurchasingViewModel : ObservableObject
{
    public PurchasingViewModel(SuppliersViewModel suppliers, PurchaseOrdersViewModel orders)
    {
        Suppliers = suppliers;
        Orders = orders;
    }

    public SuppliersViewModel Suppliers { get; }
    public PurchaseOrdersViewModel Orders { get; }

    public async Task LoadAsync()
    {
        await Suppliers.LoadAsync();
        await Orders.LoadAsync();
    }

    /// <summary>After goods arrived or a payment was made: both tabs show new numbers.</summary>
    public async Task RefreshAllAsync()
    {
        await Suppliers.RefreshAsync();
        await Orders.RefreshAsync();
    }
}
