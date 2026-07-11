using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Api.Auth;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-API-Key";

    /// <summary>Тип claim для каждого scope ключа.</summary>
    public const string ScopeClaimType = "scope";
}

/// <summary>
/// Аутентификация по заголовку <c>X-API-Key</c>. Ключ проверяется по хешу,
/// личность наполняется scope-claims для последующей авторизации.
/// UI может также передать ключ в cookie "sp_key" (для удобства SPA).
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyService _apiKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeys)
        : base(options, logger, encoder)
    {
        _apiKeys = apiKeys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? rawKey = ExtractKey();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.NoResult();

        var principal = await _apiKeys.ValidateAsync(rawKey, Context.RequestAborted);
        if (principal is null)
            return AuthenticateResult.Fail("Недействительный API-ключ");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.Id),
            new(ClaimTypes.Name, principal.Name),
        };
        claims.AddRange(principal.Scopes.Select(s => new Claim(ApiKeyDefaults.ScopeClaimType, s)));

        var identity = new ClaimsIdentity(claims, ApiKeyDefaults.Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    private string? ExtractKey()
    {
        if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header) && header.Count > 0)
            return header[0];
        if (Request.Cookies.TryGetValue("sp_key", out var cookie))
            return cookie;
        return null;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
