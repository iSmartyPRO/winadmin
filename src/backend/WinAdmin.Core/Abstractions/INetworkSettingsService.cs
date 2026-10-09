using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

/// <summary>Результат проверки новых сетевых настроек.</summary>
public sealed record NetworkCheckResult(NetworkSettings Normalized, IReadOnlyList<string> Errors, bool PortBusy)
{
    public bool Ok => Errors.Count == 0 && !PortBusy;
}

/// <summary>Сетевые настройки панели: чтение, проверка, применение (брандмауэр + network.json).</summary>
public interface INetworkSettingsService
{
    /// <summary>Действующие настройки (битый файл → значения по умолчанию).</summary>
    NetworkSettings Current { get; }

    /// <summary>Нормализует и проверяет; порт проверяется на занятость, только если он меняется.</summary>
    NetworkCheckResult Check(NetworkSettings next);

    /// <summary>Применяет брандмауэр, затем пишет network.json. Ошибка брандмауэра → файл не меняется.</summary>
    void Apply(NetworkSettings next, NetworkSettings previous);

    /// <summary>Приводит правило брандмауэра к настройкам и удаляет устаревшие правила для legacyPorts.</summary>
    void ApplyFirewall(NetworkSettings settings, IEnumerable<int> legacyPorts);
}
