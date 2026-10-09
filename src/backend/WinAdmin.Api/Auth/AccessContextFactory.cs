using System.Security.Claims;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>Контекст доступа текущего запроса (кто и с какими итоговыми правами).</summary>
public sealed class AccessContextFactory(IAccessService access)
{
    public async Task<IAccessContext?> CreateAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var principal = PrincipalClaims.Parse(user, ApiKeyDefaults.Scheme);
        if (principal is null) return null;
        var permissions = await access.GetAsync(principal, ct);
        return new AccessContext(principal, user.Identity?.Name ?? principal.Key, permissions);
    }
}
