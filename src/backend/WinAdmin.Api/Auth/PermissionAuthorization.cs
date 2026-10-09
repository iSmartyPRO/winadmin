using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>[RequirePermission("services.manage")] — доступ по итоговым правам субъекта.</summary>
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicy.Prefix + permission)
{
    public string Permission { get; } = permission;
}

public static class PermissionPolicy
{
    public const string Prefix = "perm:";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Политики perm:* создаются на лету — не нужно регистрировать каждое право.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);
        var policy = new AuthorizationPolicyBuilder("Auto")
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

public sealed class PermissionAuthorizationHandler(IAccessService access) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var principal = PrincipalClaims.Parse(context.User, ApiKeyDefaults.Scheme);
        if (principal is null) return;
        var permissions = await access.GetAsync(principal);
        if (permissions.Has(requirement.Permission))
            context.Succeed(requirement);
    }
}
