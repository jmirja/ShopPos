using ShopPos.Application.Catalog;

namespace ShopPos.Desktop.ViewModels;

public static class UiErrors
{
    /// <summary>Friendly rule messages are shown as they are; anything else gets a generic prefix.</summary>
    public static string Describe(Exception ex) =>
        ex is BusinessRuleException
            ? ex.Message
            : "Something went wrong: " + (ex.InnerException?.Message ?? ex.Message);
}
