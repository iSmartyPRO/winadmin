using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

public sealed class TokenService : ITokenService
{
    private readonly WinAdminDbContext _db;
    private readonly JwtOptions _opts;

    public TokenService(WinAdminDbContext db, JwtOptions opts)
    {
        _db = db;
        _opts = opts;
    }

    public string GenerateAccessToken(UserPrincipal user)
        => Write(user.Id, user.Login, PrincipalClaims.Format(PrincipalType.LocalUser, user.Id));

    public string GenerateDirectoryAccessToken(DirectoryObject account)
        => Write(account.Sid, account.LoginName, PrincipalClaims.Format(PrincipalType.AdUser, account.Sid));

    private string Write(string id, string name, string principal)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Name, name),
            new(PrincipalClaims.Type, principal),
        };
        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string> CreateDirectoryRefreshTokenAsync(string sid, CancellationToken ct = default)
    {
        var raw = GenerateRaw();
        _db.DirectoryRefreshTokens.Add(new DirectoryRefreshTokenEntity
        {
            Sid = sid,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_opts.RefreshTokenDays),
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<string?> ValidateDirectoryRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var entity = await _db.DirectoryRefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (entity is null || entity.RevokedAt is not null || entity.ExpiresAt < DateTimeOffset.UtcNow) return null;
        return entity.Sid;
    }

    public async Task<string> CreateRefreshTokenAsync(string userId, CancellationToken ct = default)
    {
        var raw = GenerateRaw();
        var entity = new RefreshTokenEntity
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_opts.RefreshTokenDays),
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<UserPrincipal?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var entity = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (entity is null || entity.RevokedAt is not null) return null;
        if (entity.ExpiresAt < DateTimeOffset.UtcNow) return null;
        if (!entity.User.IsActive) return null;

        return new UserPrincipal
        {
            Id = entity.User.Id,
            Login = entity.User.Login,
        };
    }

    public async Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var local = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (local is not null) local.RevokedAt = DateTimeOffset.UtcNow;
        var directory = await _db.DirectoryRefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (directory is not null) directory.RevokedAt = DateTimeOffset.UtcNow;
        if (local is not null || directory is not null) await _db.SaveChangesAsync(ct);
    }

    private static string GenerateRaw()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
