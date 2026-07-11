using Microsoft.AspNetCore.Authorization;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>Требование наличия конкретного scope (или scope "admin").</summary>
public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public string Scope { get; }
    public ScopeRequirement(string scope) => Scope = scope;
}

/// <summary>Проверяет, что у ключа есть требуемый scope; "admin" проходит везде.</summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        bool hasScope = context.User.HasClaim(ApiKeyDefaults.ScopeClaimType, requirement.Scope)
                        || context.User.HasClaim(ApiKeyDefaults.ScopeClaimType, Scopes.Admin);
        if (hasScope)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class ScopePolicy
{
    /// <summary>Имя policy для scope, например "scope:power.manage".</summary>
    public static string Name(string scope) => $"scope:{scope}";
}
