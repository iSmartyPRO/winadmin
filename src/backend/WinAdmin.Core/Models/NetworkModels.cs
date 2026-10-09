namespace WinAdmin.Core.Models;

/// <summary>Режим доступа к панели.</summary>
public enum NetworkMode
{
    /// <summary>Только этот компьютер (127.0.0.1).</summary>
    Local,
    /// <summary>Сеть: все интерфейсы + правило брандмауэра для перечисленных подсетей.</summary>
    Network,
}

/// <summary>Сетевые настройки панели (содержимое network.json).</summary>
public sealed record NetworkSettings(NetworkMode Mode, int Port, IReadOnlyList<string> Allow)
{
    public static NetworkSettings Default { get; } = new(NetworkMode.Local, 8080, []);

    /// <summary>Сравнение по содержимому (record сравнивает списки по ссылке).</summary>
    public bool IsEquivalentTo(NetworkSettings other)
        => Mode == other.Mode && Port == other.Port && (Allow ?? []).SequenceEqual(other.Allow ?? []);
}

/// <summary>Текущие сетевые настройки для UI.</summary>
public sealed record NetworkSettingsDto(NetworkMode Mode, int Port, IReadOnlyList<string> Allow, string Url, bool FirewallRule);

/// <summary>Запрос на изменение сетевых настроек.</summary>
public sealed record UpdateNetworkSettingsRequest(NetworkMode Mode, int Port, List<string>? Allow);

/// <summary>Результат изменения: новый адрес панели.</summary>
public sealed record NetworkUpdateResult(string Url);
