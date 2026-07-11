using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookie = "wa_refresh";
    private readonly IUserService _users;
    private readonly ITokenService _tokens;

    public AuthController(IUserService users, ITokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    /// <summary>Вход по логину и паролю. Возвращает JWT и устанавливает refresh cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var principal = await _users.ValidateAsync(request.Login, request.Password, ct);
        if (principal is null)
            return Unauthorized(new { message = "Неверный логин или пароль" });

        var accessToken = _tokens.GenerateAccessToken(principal);
        var refreshRaw = await _tokens.CreateRefreshTokenAsync(principal.Id, ct);
        SetRefreshCookie(refreshRaw);
        return Ok(new TokenResponse { AccessToken = accessToken, ExpiresIn = 3600 });
    }

    /// <summary>Обновляет access token по refresh cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrWhiteSpace(raw))
            return Unauthorized(new { message = "Refresh token отсутствует" });

        var principal = await _tokens.ValidateRefreshTokenAsync(raw, ct);
        if (principal is null)
        {
            ClearRefreshCookie();
            return Unauthorized(new { message = "Refresh token недействителен или истёк" });
        }

        await _tokens.RevokeRefreshTokenAsync(raw, ct);
        var refreshRaw = await _tokens.CreateRefreshTokenAsync(principal.Id, ct);
        SetRefreshCookie(refreshRaw);
        var accessToken = _tokens.GenerateAccessToken(principal);
        return Ok(new TokenResponse { AccessToken = accessToken, ExpiresIn = 3600 });
    }

    /// <summary>Выход — отзывает refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrWhiteSpace(raw))
            await _tokens.RevokeRefreshTokenAsync(raw, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>Информация о текущем пользователе (требует Bearer token).</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Me() => Ok(new
    {
        login = User.Identity?.Name,
        scopes = User.Claims.Where(c => c.Type == "scope").Select(c => c.Value).ToArray(),
    });

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
