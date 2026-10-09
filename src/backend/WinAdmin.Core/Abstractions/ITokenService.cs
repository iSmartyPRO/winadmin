using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

public interface ITokenService
{
    /// <summary>Генерирует JWT access token для пользователя.</summary>
    string GenerateAccessToken(UserPrincipal user);

    /// <summary>Создаёт refresh token в БД, возвращает raw token для cookie.</summary>
    Task<string> CreateRefreshTokenAsync(string userId, CancellationToken ct = default);

    /// <summary>Проверяет raw refresh token. Возвращает UserPrincipal или null.</summary>
    Task<UserPrincipal?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default);

    /// <summary>JWT для пользователя AD: sub = SID, wa:principal = AdUser:SID; группы не кладутся.</summary>
    string GenerateDirectoryAccessToken(DirectoryObject account);

    Task<string> CreateDirectoryRefreshTokenAsync(string sid, CancellationToken ct = default);

    /// <summary>SID владельца действующего refresh-токена AD или null.</summary>
    Task<string?> ValidateDirectoryRefreshTokenAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Отзывает refresh token (logout) — локальный или AD.</summary>
    Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default);
}
