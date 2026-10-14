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
    private readonly HttpsCertificateHolder? _holder;

    public NetworkConfigurationSource(NetworkSettingsStore store, HttpsCertificateHolder? holder = null)
    {
        _store = store;
        _holder = holder;
    }

    /// <summary>Последний созданный провайдер (для чтения LastError после старта).</summary>
    public NetworkConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => Provider = new NetworkConfigurationProvider(_store, _holder);
}

public sealed class NetworkConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly NetworkSettingsStore _store;
    private readonly HttpsCertificateHolder? _holder;
    private readonly PhysicalFileProvider? _files;
    private readonly IDisposable? _watch;

    public NetworkConfigurationProvider(NetworkSettingsStore store, HttpsCertificateHolder? holder = null)
    {
        _store = store;
        _holder = holder;
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
        // Сертификат — до OnReload: перепривязанный HTTPS-endpoint сразу получит новый.
        _holder?.Update(settings);
        Data = new Dictionary<string, string?>(NetworkEndpoints.KestrelConfiguration(settings), StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _files?.Dispose();
    }
}
