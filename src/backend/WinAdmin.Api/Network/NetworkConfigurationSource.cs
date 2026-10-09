using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Network;

/// <summary>
/// Источник конфигурации Kestrel:Endpoints из network.json. Kestrel загружает секцию
/// «Kestrel» с reloadOnChange, поэтому изменение файла перепривязывает endpoint без
/// перезапуска процесса.
/// </summary>
public sealed class NetworkConfigurationSource : IConfigurationSource
{
    private readonly NetworkSettingsStore _store;

    public NetworkConfigurationSource(NetworkSettingsStore store) => _store = store;

    /// <summary>Последний созданный провайдер (для чтения LastError после старта).</summary>
    public NetworkConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => Provider = new NetworkConfigurationProvider(_store);
}

public sealed class NetworkConfigurationProvider : ConfigurationProvider, IDisposable
{
    private const string EndpointKey = "Kestrel:Endpoints:Http:Url";

    private readonly NetworkSettingsStore _store;
    private readonly PhysicalFileProvider? _files;
    private readonly IDisposable? _watch;

    public NetworkConfigurationProvider(NetworkSettingsStore store)
    {
        _store = store;
        if (Directory.Exists(store.DataDirectory))
        {
            _files = new PhysicalFileProvider(store.DataDirectory);
            _watch = ChangeToken.OnChange(
                () => _files.Watch(WinAdminPaths.NetworkFileName),
                () =>
                {
                    // Даём писателю закончить (как JsonConfigurationProvider: ReloadDelay 250 мс).
                    Thread.Sleep(250);
                    Load();
                    OnReload();
                });
        }
    }

    /// <summary>Ошибка чтения network.json (null — файл в порядке или отсутствует).</summary>
    public string? LastError { get; private set; }

    public override void Load()
    {
        var settings = _store.ReadOrDefault(out var error);
        LastError = error;
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EndpointKey] = NetworkEndpoints.ListenUrl(settings),
        };
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _files?.Dispose();
    }
}
