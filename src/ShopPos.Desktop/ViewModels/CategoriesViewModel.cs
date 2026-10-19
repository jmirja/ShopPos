using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopPos.Application.Catalog;

namespace ShopPos.Desktop.ViewModels;

public record CategoryRow(Guid Id, string Display, CategoryDto Dto);

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICatalogService _catalog;
    private List<CategoryDto> _all = new();

    public CategoriesViewModel(ICatalogService catalog) => _catalog = catalog;

    public ObservableCollection<CategoryRow> Rows { get; } = new();
    public ObservableCollection<CategoryDto> ParentOptions { get; } = new();

    [ObservableProperty] private CategoryRow? _selectedRow;
    [ObservableProperty] private Guid _categoryId;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private CategoryDto? _selectedParent;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _formTitle = "New category";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public async Task LoadAsync(Guid? selectId = null)
    {
        try
        {
            _all = await _catalog.GetCategoriesAsync();

            var rows = _all.Select(c => new CategoryRow(c.Id, BuildPath(c), c))
                           .OrderBy(r => r.Display)
                           .ToList();
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            if (selectId is not null)
                SelectedRow = Rows.FirstOrDefault(r => r.Id == selectId);
            else
                StartNew();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    partial void OnSelectedRowChanged(CategoryRow? value)
    {
        if (value is null) return;

        CategoryId = value.Id;
        Name = value.Dto.Name;
        IsActive = value.Dto.IsActive;
        FormTitle = "Edit category";
        RebuildParentOptions(excluding: SelfAndDescendants(value.Id));
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Id == (value.Dto.ParentCategoryId ?? Guid.Empty))
                         ?? ParentOptions[0];
    }

    [RelayCommand]
    private void New() => StartNew();

    private void StartNew()
    {
        SelectedRow = null;
        CategoryId = Guid.Empty;
        Name = string.Empty;
        IsActive = true;
        FormTitle = "New category";
        RebuildParentOptions(excluding: null);
        SelectedParent = ParentOptions[0];
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            Guid? parentId = SelectedParent is { } p && p.Id != Guid.Empty ? p.Id : null;
            var id = await _catalog.SaveCategoryAsync(new CategoryDto(CategoryId, Name, parentId, IsActive));
            await LoadAsync(id);
            InfoMessage = "Category saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        if (CategoryId == Guid.Empty) return;

        try
        {
            await _catalog.DeleteCategoryAsync(CategoryId);
            await LoadAsync();
            InfoMessage = "Category deleted.";
        }
        catch (Exception ex)
        {
            ErrorMessage = UiErrors.Describe(ex);
        }
    }

    // ---- helpers ----

    private void RebuildParentOptions(HashSet<Guid>? excluding)
    {
        ParentOptions.Clear();
        ParentOptions.Add(CategoryDto.None);
        foreach (var c in _all.OrderBy(c => c.Name))
            if (excluding is null || !excluding.Contains(c.Id))
                ParentOptions.Add(c);
    }

    private HashSet<Guid> SelfAndDescendants(Guid id)
    {
        var set = new HashSet<Guid> { id };
        bool added;
        do
        {
            added = false;
            foreach (var c in _all)
                if (c.ParentCategoryId is Guid parent && set.Contains(parent) && set.Add(c.Id))
                    added = true;
        } while (added);
        return set;
    }

    private string BuildPath(CategoryDto category)
    {
        var parts = new List<string> { category.Name };
        var current = category;
        for (var i = 0; i < 10 && current.ParentCategoryId is Guid parentId; i++)
        {
            var parent = _all.FirstOrDefault(c => c.Id == parentId);
            if (parent is null) break;
            parts.Insert(0, parent.Name);
            current = parent;
        }
        var path = string.Join(" > ", parts);
        return category.IsActive ? path : path + " (inactive)";
    }
}
