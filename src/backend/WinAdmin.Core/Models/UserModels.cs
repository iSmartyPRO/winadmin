namespace WinAdmin.Core.Models;

public sealed record UserDto
{
    public required string Id { get; init; }
    public required string Login { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsActive { get; init; }
}

public sealed record CreateUserRequest
{
    public required string Login { get; init; }
    public required string Password { get; init; }
    public List<string> Scopes { get; init; } = [];
}

public sealed record UpdateScopesRequest
{
    public List<string> Scopes { get; init; } = [];
}

public sealed record ChangePasswordRequest
{
    public required string NewPassword { get; init; }
}

public sealed record SetActiveRequest
{
    public bool IsActive { get; init; }
}

public sealed record LoginRequest
{
    public required string Login { get; init; }
    public required string Password { get; init; }
}

public sealed record TokenResponse
{
    public required string AccessToken { get; init; }
    public int ExpiresIn { get; init; }
}

public sealed record UserPrincipal
{
    public required string Id { get; init; }
    public required string Login { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public sealed class JwtOptions
{
    public string Secret { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public string Issuer { get; set; } = "WinAdmin";
    public string Audience { get; set; } = "WinAdmin";
}
