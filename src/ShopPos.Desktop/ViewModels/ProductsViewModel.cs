using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Catalog;

namespace ShopPos.Desktop.ViewModels;

/// <summary>One editable row in the variants grid.</summary>
public partial class VariantRow : ObservableObject
{
    public Guid Id { get; set; }                 // Guid.Empty = not saved yet

    [ObservableProperty] private string? _variantName;
    [ObservableProperty] private string? _sku;
    [ObservableProperty] private string? _barcode;
    [ObservableProperty] private decimal _costPrice;
    [ObservableProperty] private decimal _sellingPrice;
    [ObservableProperty] private decimal _reorderLevel;
    [ObservableProperty] private bool _isActive = true;
}

public partial class ProductsViewModel : ObservableObject
{
    private readonly ICatalogService _catalog;

    public ProductsViewModel(ICatalogService catalog) => _catalog = catalog;

    public ObservableCollection<ProductListItem> Products { get; } = new();
    public ObservableCollection<CategoryDto> CategoryOptions { get; } = new();
    public ObservableCollection<VariantRow> Variants { get; } = new();
    public string[] ProductTypes { get; } = { "Stock", "Service" };

    public bool IsStock => ProductType == "Stock";

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ProductListItem? _selectedProduct;

    [ObservableProperty] private Guid _productId;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private CategoryDto? _selectedCategory;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsStock))] private string _productType = "Stock";
    [ObservableProperty] private string? _brand;
    [ObservableProperty] private bool _tracksSerial;
    [ObservableProperty] private bool _tracksBatch;
    [ObservableProperty] private bool _isActive = true;

    [ObservableProperty] private string _formTitle = "New product";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public async Task LoadAsync()
    {
        try
        {
            var categories = await _catalog.GetCategoriesAsync();
            CategoryOptions.Clear();
            CategoryOptions.Add(CategoryDto.None);
            foreach (var c in categories) CategoryOptions.Add(c);

            await SearchAsync();
            StartNewProduct();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    partial void OnProductTypeChanged(string value)
    {
        if (value == "Service")
        {
            TracksSerial = false;
            TracksBatch = false;
        }
    }

    partial void OnSelectedProductChanged(ProductListItem? value)
    {
        if (value is not null) _ = LoadProductAsync(value.Id);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            var items = await _catalog.SearchProductsAsync(SearchText);
            Products.Clear();
            foreach (var p in items) Products.Add(p);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    private async Task LoadProductAsync(Guid id)
    {
        try
        {
            ErrorMessage = null;
            var d = await _catalog.GetProductAsync(id);
            if (d is null) return;

            ProductId = d.Id;
            Name = d.Name;
            Brand = d.Brand;
            ProductType = d.ProductType;
            TracksSerial = d.TracksSerial;
            TracksBatch = d.TracksBatch;
            IsActive = d.IsActive;
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Id == (d.CategoryId ?? Guid.Empty))
                               ?? CategoryOptions.FirstOrDefault();

            Variants.Clear();
            foreach (var v in d.Variants)
            {
                Variants.Add(new VariantRow
                {
                    Id = v.Id,
                    VariantName = v.VariantName,
                    Sku = v.Sku,
                    Barcode = v.Barcode,
                    CostPrice = v.CostPrice,
                    SellingPrice = v.SellingPrice,
                    ReorderLevel = v.ReorderLevel,
                    IsActive = v.IsActive
                });
            }
            FormTitle = "Edit product";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private void NewProduct() => StartNewProduct();

    private void StartNewProduct()
    {
        SelectedProduct = null;
        ProductId = Guid.Empty;
        Name = string.Empty;
        Brand = null;
        ProductType = "Stock";
        TracksSerial = false;
        TracksBatch = false;
        IsActive = true;
        SelectedCategory = CategoryOptions.FirstOrDefault();
        Variants.Clear();
        Variants.Add(new VariantRow());
        FormTitle = "New product";
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand]
    private void AddVariant()
    {
        var last = Variants.LastOrDefault();
        Variants.Add(new VariantRow
        {
            CostPrice = last?.CostPrice ?? 0,
            SellingPrice = last?.SellingPrice ?? 0,
            ReorderLevel = last?.ReorderLevel ?? 0
        });
    }

    [RelayCommand]
    private void RemoveVariant(VariantRow? row)
    {
        if (row is null) return;
        if (row.Id != Guid.Empty)
        {
            InfoMessage = "A saved variant cannot be deleted (it may be used in sales). Untick 'Active' instead.";
            return;
        }
        Variants.Remove(row);
    }

    [RelayCommand]
    private async Task GenerateBarcodeAsync(VariantRow? row)
    {
        if (row is null) return;
        try
        {
            row.Barcode = await _catalog.GenerateBarcodeAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            Guid? categoryId = SelectedCategory is { } c && c.Id != Guid.Empty ? c.Id : null;

            var detail = new ProductDetail(
                ProductId, Name, categoryId, ProductType, Brand, TracksSerial, TracksBatch, IsActive,
                Variants.Select(v => new VariantDto(v.Id, v.Sku, v.Barcode, v.VariantName,
                    v.CostPrice, v.SellingPrice, v.ReorderLevel, v.IsActive)).ToList());

            var id = await _catalog.SaveProductAsync(detail);

            await SearchAsync();
            SelectedProduct = Products.FirstOrDefault(p => p.Id == id);
            await LoadProductAsync(id);          // shows generated SKUs / barcodes
            InfoMessage = "Product saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }
}
