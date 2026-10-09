using System.Text.Json.Nodes;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Abstractions;

/// <summary>Состояние и настройки модулей (KeyNotFoundException — нет модуля, ArgumentException — неверные настройки).</summary>
public interface IModuleRegistry
{
    IReadOnlyList<IWinAdminModule> Modules { get; }
    ModuleState GetState(string id);
    Task SetEnabledAsync(string id, bool enabled, string actor, CancellationToken ct = default);
    IReadOnlyList<SettingsField> GetSettingsSchema(string id);

    /// <summary>Настройки для UI: секреты — { "isSet": bool }.</summary>
    Task<JsonObject> GetSettingsViewAsync(string id, CancellationToken ct = default);

    /// <summary>Пустой или отсутствующий секрет — оставить прежний.</summary>
    Task SaveSettingsAsync(string id, JsonObject settings, string actor, CancellationToken ct = default);

    /// <summary>Настройки с расшифрованными секретами (недоступный секрет — пустая строка).</summary>
    Task<T> GetSettingsAsync<T>(string id, CancellationToken ct = default) where T : class, new();
}
