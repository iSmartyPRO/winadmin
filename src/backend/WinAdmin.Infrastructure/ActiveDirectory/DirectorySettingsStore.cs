using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Настройки подключения к домену (PlatformSettings, ключ «directory»); без записи — по машине.</summary>
public sealed class DirectorySettingsStore(IServiceScopeFactory scopes, IMachineInfo machine) : IDirectorySettingsStore
{
    public const string Key = "directory";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DirectorySettings? _cached;

    public async Task<DirectorySettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached) return cached;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        var settings = row is null
            ? new DirectorySettings(machine.IsDomainJoined, machine.DomainName, null, null, false)
            : JsonSerializer.Deserialize<DirectorySettings>(row.Json, Json) ?? DirectorySettings.Disabled;
        return _cached = settings;
    }

    public async Task SaveAsync(DirectorySettings settings, CancellationToken ct = default)
    {
        var clean = new DirectorySettings(settings.Enabled, Blank(settings.Domain), Blank(settings.Server),
            Blank(settings.BaseDn), settings.UseLdaps);
        if (clean.Enabled && clean.Domain is null)
            throw new ArgumentException("Укажите домен.");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null) db.PlatformSettings.Add(row = new PlatformSettingEntity { Key = Key });
        row.Json = JsonSerializer.Serialize(clean, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        _cached = clean;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
