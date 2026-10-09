using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>JWT пользователя AD: группы — из кэша (5/15 минут) в claims wa:group; нет групп → 401.</summary>
public static class DirectorySessionValidator
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        if (context.Principal is not { } user) return;
        var principal = PrincipalClaims.Parse(user, ApiKeyDefaults.Scheme);
        if (principal is not { Type: PrincipalType.AdUser }) return;

        var cache = context.HttpContext.RequestServices.GetRequiredService<IAdGroupCache>();
        var groups = await cache.GetGroupsAsync(principal.Id, context.HttpContext.RequestAborted);
        if (groups is null)
        {
            context.Fail("Сеанс учётной записи домена недействителен");
            return;
        }
        if (user.Identity is ClaimsIdentity identity)
            foreach (var sid in groups)
                identity.AddClaim(new Claim(PrincipalClaims.GroupType, sid));
    }
}
