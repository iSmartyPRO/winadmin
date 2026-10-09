using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Настройки структуры AD в PlatformSettings; пароль учётки записи — зашифрован.</summary>
public sealed class AdStructureStore(IServiceScopeFactory scopes, ISecretProtector protector) : IAdStructureStore
{
    public const string Key = "ad-structure";
    private const string Purpose = "ad-structure.WritePassword";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Stored(
        string? RootOu, string UsersOuName, List<string> HiddenOus, AdWriteMode WriteMode, string? WriteLogin, string? WritePassword);

    private Stored? _cached;

    public async Task<AdStructureSettings> GetAsync(CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        return s is null
            ? AdStructureSettings.Default
            : new AdStructureSettings(s.RootOu, s.UsersOuName, s.HiddenOus, s.WriteMode, s.WriteLogin, s.WritePassword is not null);
    }

    public async Task SaveAsync(AdStructureSettings settings, string? newPassword, CancellationToken ct = default)
    {
        var clean = settings.Normalize();
        var previous = await LoadAsync(ct);
        string? password = newPassword switch
        {
            null => previous?.WritePassword,
            "" => null,
            _ => protector.Protect(newPassword, Purpose),
        };
        var stored = new Stored(clean.RootOu, clean.UsersOuName, [.. clean.HiddenOus], clean.WriteMode, clean.WriteLogin, password);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.FirstOrDefaultAsync(r => r.Key == Key, ct);
        if (row is null) db.PlatformSettings.Add(row = new PlatformSettingEntity { Key = Key });
        row.Json = JsonSerializer.Serialize(stored, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        _cached = stored;
    }

    public async Task<AdWriteCredential> GetWriteCredentialAsync(CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        if (s is null) return new AdWriteCredential(AdWriteMode.ServiceAccount, null, null);
        string? password = s.WritePassword is null ? null : protector.Unprotect(s.WritePassword, Purpose);
        return new AdWriteCredential(s.WriteMode, s.WriteLogin, password);
    }

    private async Task<Stored?> LoadAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Key == Key, ct);
        return _cached = row is null ? null : JsonSerializer.Deserialize<Stored>(row.Json, Json);
    }
}
