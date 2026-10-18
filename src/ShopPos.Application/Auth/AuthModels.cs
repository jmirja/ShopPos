namespace ShopPos.Application.Auth;

/// <summary>Result of a login attempt.</summary>
public record AuthResult(
    bool Success,
    string? Error = null,
    Guid UserId = default,
    string FullName = "",
    string Role = "",
    Guid BranchId = default)
{
    public static AuthResult Fail(string error) => new(false, error);
}

/// <summary>Login and first-run account creation. Implemented in Infrastructure.</summary>
public interface IAuthService
{
    Task<bool> HasAnyUserAsync();
    Task CreateOwnerAsync(string fullName, string username, string password);
    Task<AuthResult> LoginAsync(string username, string password);
}

/// <summary>The user who is signed in right now. Used by every screen and service.</summary>
public interface ICurrentUser
{
    bool IsLoggedIn { get; }
    Guid UserId { get; }
    Guid BranchId { get; }
    string FullName { get; }
    string Role { get; }
    bool IsInRole(params string[] roles);
    void SignIn(AuthResult result);
    void SignOut();
}

public class CurrentUser : ICurrentUser
{
    public bool IsLoggedIn { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;

    public bool IsInRole(params string[] roles) =>
        IsLoggedIn && roles.Contains(Role, StringComparer.OrdinalIgnoreCase);

    public void SignIn(AuthResult result)
    {
        IsLoggedIn = true;
        UserId = result.UserId;
        BranchId = result.BranchId;
        FullName = result.FullName;
        Role = result.Role;
    }

    public void SignOut()
    {
        IsLoggedIn = false;
        UserId = default;
        BranchId = default;
        FullName = string.Empty;
        Role = string.Empty;
    }
}
