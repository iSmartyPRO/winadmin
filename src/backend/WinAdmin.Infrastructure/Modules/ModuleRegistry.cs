using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Modules;

/// <summary>Singleton: состояние модулей кэшируется в памяти и перечитывается после изменений.</summary>
public sealed class ModuleRegistry(
    PermissionCatalog catalog, IMachineInfo machine, IServiceScopeFactory scopes,
    ISecretProtector protector, IAccessService access) : IModuleRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private Dictionary<string, ModuleStateEntity>? _rows;

    public IReadOnlyList<IWinAdminModule> Modules => catalog.Modules;

    public ModuleState GetState(string id)
    {
        var module = Find(id);
        var (available, reason) = Check(module);
        bool enabled = available && (Rows().TryGetValue(id, out var row) ? row.Enabled : module.EnabledByDefault);
        return new ModuleState(id, enabled, available, reason);
    }

    public async Task SetEnabledAsync(string id, bool enabled, string actor, CancellationToken ct = default)
    {
        var module = Find(id);
        var (available, reason) = Check(module);
        if (enabled && !available)
            throw new InvalidOperationException($"Модуль «{module.Title}» недоступен на этой машине: {reason}");

        bool firstEnable = enabled && !Rows().ContainsKey(id);
        await UpsertAsync(id, row => row.Enabled = enabled, actor, ct);
        await AuditAsync(actor, enabled ? "module.enable" : "module.disable", id, null, ct);

        if (firstEnable && module is IModuleLifecycle lifecycle)
        {
            using var scope = scopes.CreateScope();
            await lifecycle.OnFirstEnabledAsync(scope.ServiceProvider, ct);
        }
    }

    public IReadOnlyList<SettingsField> GetSettingsSchema(string id)
        => Find(id).SettingsType is { } t ? ModuleSettingsSchema.Build(t) : [];

    public Task<JsonObject> GetSettingsViewAsync(string id, CancellationToken ct = default)
    {
        var module = Find(id);
        var stored = StoredSettings(id);
        var view = new JsonObject();
        if (module.SettingsType is null) return Task.FromResult(view);
        // Несохранённые поля показываются со значениями по умолчанию из класса настроек.
        var defaults = JsonSerializer.SerializeToNode(Activator.CreateInstance(module.SettingsType), module.SettingsType, Json) as JsonObject ?? [];
        foreach (var p in Properties(module.SettingsType))
        {
            string name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (ModuleSettingsSchema.IsSecret(p))
                view[name] = new JsonObject { ["isSet"] = stored[name] is JsonValue v && v.GetValue<string>().Length > 0 };
            else
                view[name] = (stored[name] ?? defaults[name])?.DeepClone();
        }
        return Task.FromResult(view);
    }

    public async Task SaveSettingsAsync(string id, JsonObject settings, string actor, CancellationToken ct = default)
    {
        var module = Find(id);
        if (module.SettingsType is null)
            throw new ArgumentException($"У модуля «{module.Title}» нет настроек.");

        var merged = StoredSettings(id);
        var changed = new List<string>();
        foreach (var p in Properties(module.SettingsType))
        {
            string name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!settings.TryGetPropertyValue(name, out var incoming))
                continue;
            if (ModuleSettingsSchema.IsSecret(p))
            {
                if (incoming is JsonValue sv && sv.TryGetValue<string>(out var secret) && secret.Length > 0)
                {
                    merged[name] = protector.Protect(secret, Purpose(id, name));
                    changed.Add(name);
                }
                continue;
            }
            // Пустое значение нестрокового поля (очищенное число, список) — вернуть значение по умолчанию.
            bool empty = incoming is null || (incoming is JsonValue ev && ev.TryGetValue<string>(out var es) && es.Length == 0);
            if (empty && p.PropertyType != typeof(string))
                merged.Remove(name);
            else
                merged[name] = incoming?.DeepClone();
            changed.Add(name);
        }

        // Проверка типов: секреты подставляем пустыми строками.
        var probe = (JsonObject)merged.DeepClone();
        foreach (var p in Properties(module.SettingsType).Where(ModuleSettingsSchema.IsSecret))
            probe[JsonNamingPolicy.CamelCase.ConvertName(p.Name)] = "";
        try
        {
            probe.Deserialize(module.SettingsType, Json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Неверные настройки модуля «{module.Title}»: {ex.Message}");
        }

        await UpsertAsync(id, row => row.SettingsJson = merged.ToJsonString(), actor, ct);
        await AuditAsync(actor, "module.settings.update", id, string.Join(", ", changed), ct);
    }

    public Task<T> GetSettingsAsync<T>(string id, CancellationToken ct = default) where T : class, new()
    {
        var stored = StoredSettings(id);
        foreach (var p in Properties(typeof(T)).Where(ModuleSettingsSchema.IsSecret))
        {
            string name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            string? value = stored[name] is JsonValue v ? v.GetValue<string>() : null;
            try
            {
                stored[name] = string.IsNullOrEmpty(value) ? "" : protector.Unprotect(value, Purpose(id, name));
            }
            catch (SecretUnavailableException)
            {
                stored[name] = "";
            }
        }
        return Task.FromResult(stored.Deserialize<T>(Json) ?? new T());
    }

    // ── helpers ──────────────────────────────────────────────────

    private IWinAdminModule Find(string id)
        => catalog.FindModule(id) ?? throw new KeyNotFoundException($"Модуль «{id}» не найден.");

    private (bool Available, string? Reason) Check(IWinAdminModule module)
    {
        if (module.Requirements.HasFlag(ModuleRequirements.WindowsServer) && !machine.IsWindowsServer)
            return (false, "нужен Windows Server");
        if (module.Requirements.HasFlag(ModuleRequirements.DomainJoined) && !machine.IsDomainJoined)
            return (false, "компьютер не входит в домен");
        return (true, null);
    }

    private static string Purpose(string moduleId, string field) => $"module:{moduleId}:{field}";

    private static IEnumerable<PropertyInfo> Properties(Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

    private JsonObject StoredSettings(string id)
        => Rows().TryGetValue(id, out var row) && !string.IsNullOrWhiteSpace(row.SettingsJson)
            ? JsonNode.Parse(row.SettingsJson) as JsonObject ?? new JsonObject()
            : new JsonObject();

    private Dictionary<string, ModuleStateEntity> Rows()
    {
        lock (_lock)
        {
            if (_rows is not null) return _rows;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            return _rows = db.ModuleStates.AsNoTracking().ToDictionary(r => r.Id, StringComparer.Ordinal);
        }
    }

    private async Task UpsertAsync(string id, Action<ModuleStateEntity> change, string actor, CancellationToken ct)
    {
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            var row = await db.ModuleStates.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (row is null)
            {
                row = new ModuleStateEntity { Id = id, Enabled = Find(id).EnabledByDefault };
                db.ModuleStates.Add(row);
            }
            change(row);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = actor;
            await db.SaveChangesAsync(ct);
        }
        lock (_lock) _rows = null;
        access.Invalidate();
    }

    private async Task AuditAsync(string actor, string action, string target, string? details, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(
            new AuditEntryDto { Actor = actor, Action = action, Target = target, Success = true, Details = details }, ct);
    }
}
