using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace WinAdmin.Api.Auth;

public sealed record WindowsSignIn(string Sid, string AuthenticationType);

/// <summary>Результат Negotiate для текущего запроса; null — нужно отправить вызов (401 + WWW-Authenticate).</summary>
public interface IWindowsSignInReader
{
    Task<WindowsSignIn?> ReadAsync(HttpContext context);
}

public sealed class NegotiateWindowsSignInReader : IWindowsSignInReader
{
    public async Task<WindowsSignIn?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal?.FindFirst(ClaimTypes.PrimarySid)?.Value is not { } sid)
            return null;
        return new WindowsSignIn(sid, result.Principal.Identity?.AuthenticationType ?? "");
    }
}
