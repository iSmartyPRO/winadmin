using System.Text.Json;
using System.Text.Json.Serialization;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Файл network.json: чтение, атомарная запись, первичное создание.</summary>
public sealed class NetworkSettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public NetworkSettingsStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        FilePath = Path.Combine(dataDirectory, WinAdminPaths.NetworkFileName);
    }

    public string DataDirectory { get; }
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);

    /// <summary>
    /// Читает настройки. Нет файла → значения по умолчанию без ошибки.
    /// Файл битый/невалидный → значения по умолчанию и текст ошибки (файл не трогаем).
    /// </summary>
    public NetworkSettings ReadOrDefault(out string? error)
    {
        error = null;
        if (!Exists) return NetworkSettings.Default;
        try
        {
            var parsed = JsonSerializer.Deserialize<NetworkSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (parsed is null)
            {
                error = "файл пуст";
                return NetworkSettings.Default;
            }
            var (settings, errors) = NetworkSettingsValidator.Normalize(parsed);
            if (errors.Count > 0)
            {
                error = string.Join(" ", errors);
                return NetworkSettings.Default;
            }
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return NetworkSettings.Default;
        }
    }

    /// <summary>Атомарная запись: временный файл + замена.</summary>
    public void Write(NetworkSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Создаёт network.json (режим Local), если его ещё нет.</summary>
    public void EnsureCreated(int? port)
    {
        if (Exists) return;
        Write(NetworkSettings.Default with { Port = port ?? NetworkEndpoints.DefaultPort });
    }
}
