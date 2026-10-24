namespace ShopPos.Application.Customers;

public record CustomerDto(Guid Id, string Name, string? Phone);

public interface ICustomerService
{
    /// <summary>Active customers whose name or phone contains the text (everyone, newest names first, when empty).</summary>
    Task<List<CustomerDto>> SearchAsync(string? term);

    /// <summary>Adds a customer with just a name and an optional phone number (the full customer screen comes later).</summary>
    Task<CustomerDto> QuickAddAsync(string name, string? phone);
}
