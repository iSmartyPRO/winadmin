using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IUserService users, ITokenService tokens, IDirectorySignIn directory,
    IDirectorySettingsStore directorySettings, IAuditService audit) : ControllerBase
{
    private const string RefreshCookie = "wa_refresh";

    /// <summary>
    /// Вход по логину и паролю: локальный пользователь WinAdmin, иначе учётка домена
    /// (только по HTTPS или с этого компьютера). Возвращает JWT и устанавливает refresh cookie.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        string login = (request.Login ?? "").Trim();
        if (login.Length == 0 || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new { message = "Неверный логин или пароль" });

        // 1. Есть локальный пользователь с таким логином — только его пароль, в домен не идём.
        if (await users.ExistsAsync(login, ct))
        {
            var local = await users.ValidateAsync(login, request.Password, ct);
            if (local is null) return await FailedAsync(login, "local", ct);
            await AuditAsync("auth.login", login, true, "local", ct);
            return await IssueLocalAsync(local, ct);
        }

        // 2. Подключение к домену выключено — обычный отказ (время ответа выравнивает заглушка в ValidateAsync).
        if (!(await directorySettings.GetAsync(ct)).Enabled)
        {
            await users.ValidateAsync(login, request.Password, ct);
            return await FailedAsync(login, "unknown", ct);
        }

        // 3. Пароль домена — только HTTPS или loopback.
        if (!LoginTransport.PasswordAllowed(HttpContext))
            return BadRequest(new { message = LoginTransport.RefusedMessage });

        var signIn = await directory.PasswordAsync(login, request.Password, ct);
        return await DirectoryOutcomeAsync(signIn, login, "auth.login", ct);
    }

    /// <summary>Обновляет access token по refresh cookie (локальный пользователь или учётка домена).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrWhiteSpace(raw))
            return Unauthorized(new { message = "Refresh token отсутствует" });

        var local = await tokens.ValidateRefreshTokenAsync(raw, ct);
        if (local is not null)
        {
            await tokens.RevokeRefreshTokenAsync(raw, ct);
            return await IssueLocalAsync(local, ct);
        }

        // Учётка домена перепроверяется в каталоге: отключённая или без назначений — не обновляем.
        var sid = await tokens.ValidateDirectoryRefreshTokenAsync(raw, ct);
        if (sid is not null && await directory.CompleteAsync(sid, ct) is { Status: DirectorySignInStatus.Ok, Account: { } account })
        {
            await tokens.RevokeRefreshTokenAsync(raw, ct);
            SetRefreshCookie(await tokens.CreateDirectoryRefreshTokenAsync(sid, ct));
            return Ok(new TokenResponse { AccessToken = tokens.GenerateDirectoryAccessToken(account), ExpiresIn = 3600 });
        }

        ClearRefreshCookie();
        return Unauthorized(new { message = "Refresh token недействителен или истёк" });
    }

    /// <summary>Выход — отзывает refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrWhiteSpace(raw))
            await tokens.RevokeRefreshTokenAsync(raw, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    private async Task<IActionResult> DirectoryOutcomeAsync(DirectorySignInResult result, string login, string action, CancellationToken ct)
    {
        switch (result.Status)
        {
            case DirectorySignInStatus.Ok:
                await AuditAsync(action, result.Account!.LoginName, true, "ad", ct);
                SetRefreshCookie(await tokens.CreateDirectoryRefreshTokenAsync(result.Account.Sid, ct));
                return Ok(new TokenResponse { AccessToken = tokens.GenerateDirectoryAccessToken(result.Account), ExpiresIn = 3600 });
            case DirectorySignInStatus.NoAccess:
                await AuditAsync(action + ".failed", login, false, "нет назначений", ct);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Нет доступа к WinAdmin" });
            case DirectorySignInStatus.Unavailable:
                await AuditAsync(action + ".failed", login, false, "контроллер домена недоступен", ct);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Контроллер домена недоступен" });
            default:
                return await FailedAsync(login, "ad", ct, action + ".failed");
        }
    }

    private async Task<IActionResult> FailedAsync(string login, string source, CancellationToken ct, string action = "auth.login.failed")
    {
        await AuditAsync(action, login, false, source, ct);
        return Unauthorized(new { message = "Неверный логин или пароль" });
    }

    private Task AuditAsync(string action, string actor, bool success, string details, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor, Action = action, Success = success, Details = details,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        }, ct);

    private async Task<IActionResult> IssueLocalAsync(UserPrincipal user, CancellationToken ct)
    {
        SetRefreshCookie(await tokens.CreateRefreshTokenAsync(user.Id, ct));
        return Ok(new TokenResponse { AccessToken = tokens.GenerateAccessToken(user), ExpiresIn = 3600 });
    }

    private void SetRefreshCookie(string raw) =>
        Response.Cookies.Append(RefreshCookie, raw, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            MaxAge = TimeSpan.FromDays(30),
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie);
}
