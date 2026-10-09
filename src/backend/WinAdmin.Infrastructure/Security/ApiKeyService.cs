using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

/// <summary>Управление API-ключами: создание, проверка (по хешу), отзыв, bootstrap.</summary>
public sealed class ApiKeyService : IApiKeyService
{
    private const string KeyPrefix = "sp_";
    private readonly WinAdminDbContext _db;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(WinAdminDbContext db, ILogger<ApiKeyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default)
    {
        var keys = await _db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
        return keys.Select(ToDto).ToList();
    }

    public async Task<CreatedApiKey> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default)
    {
        string raw = GenerateKey();
        var entity = new ApiKeyEntity
        {
            Name = request.Name.Trim(),
            KeyHash = Hash(raw),
            Hint = raw[^4..],
            ExpiresAt = request.ExpiresAt,
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Создан API-ключ {Name} ({Id})", entity.Name, entity.Id);
        return new CreatedApiKey { Key = ToDto(entity), PlaintextKey = raw };
    }

    public async Task<bool> RevokeAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (entity is null) return false;
        entity.IsRevoked = true;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Отозван API-ключ {Id}", id);
        return true;
    }

    public async Task<ApiKeyPrincipal?> ValidateAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        string hash = Hash(rawKey.Trim());

        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
        if (entity is null || entity.IsRevoked) return null;
        if (entity.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow) return null;

        entity.LastUsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new ApiKeyPrincipal
        {
            Id = entity.Id,
            Name = entity.Name,
        };
    }

    public async Task<string?> EnsureBootstrapAsync(string? presetKey, CancellationToken ct = default)
    {
        if (await _db.ApiKeys.AnyAsync(ct))
            return null;

        string raw = string.IsNullOrWhiteSpace(presetKey) ? GenerateKey() : presetKey.Trim();
        var entity = new ApiKeyEntity
        {
            Name = "bootstrap-admin",
            KeyHash = Hash(raw),
            Hint = raw.Length >= 4 ? raw[^4..] : raw,
            // Маркер: при старте PlatformBootstrapper назначит роль «Администратор» и очистит поле.
            Scopes = "admin",
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Создан стартовый admin-ключ '{Name}'. Сохраните его и создайте рабочие ключи.", entity.Name);
        return raw;
    }

    // ── helpers ──────────────────────────────────────────────────
    private static string GenerateKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        string body = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return KeyPrefix + body;
    }

    private static string Hash(string raw)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static ApiKeyDto ToDto(ApiKeyEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        CreatedAt = e.CreatedAt,
        ExpiresAt = e.ExpiresAt,
        LastUsedAt = e.LastUsedAt,
        IsRevoked = e.IsRevoked,
        Hint = e.Hint,
    };
}
