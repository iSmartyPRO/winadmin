using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default);
    Task<CreatedApiKey> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default);
    Task<bool> RevokeAsync(string id, CancellationToken ct = default);

    /// <summary>Проверяет сырой ключ и возвращает личность, либо null.</summary>
    Task<ApiKeyPrincipal?> ValidateAsync(string rawKey, CancellationToken ct = default);

    /// <summary>Создаёт стартовый admin-ключ при первом запуске, если ключей ещё нет.
    /// Возвращает сырой ключ, если он был создан.</summary>
    Task<string?> EnsureBootstrapAsync(string? presetKey, CancellationToken ct = default);
}

public interface IAuditService
{
    Task WriteAsync(AuditEntryDto entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntryDto>> QueryAsync(int limit = 200, string? actor = null, string? target = null, CancellationToken ct = default);
}
