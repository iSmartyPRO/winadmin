using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>
/// database.json в каталоге данных: провайдер и строка подключения.
/// Пароль PostgreSQL хранится зашифрованным (Password=enc:v1:…).
/// </summary>
public sealed class DatabaseSettingsStore(string dataDirectory, ISecretProtector protector)
{
    public const string FileName = "database.json";
    private const string PasswordPurpose = "database:password";

    public string FilePath { get; } = Path.Combine(dataDirectory, FileName);
    public bool Exists => File.Exists(FilePath);

    public static string ProviderName(DatabaseProvider p) => p == DatabaseProvider.PostgreSql ? "postgresql" : "sqlite";

    public static DatabaseProvider ParseProvider(string value) => value.Trim().ToLowerInvariant() switch
    {
        "sqlite" => DatabaseProvider.Sqlite,
        "postgresql" or "postgres" => DatabaseProvider.PostgreSql,
        _ => throw new ArgumentException($"Неизвестный провайдер «{value}». Допустимо: sqlite, postgresql."),
    };

    /// <summary>Настройки как хранятся (пароль может быть зашифрован). Нет файла → SQLite по умолчанию.</summary>
    public DatabaseSettings Read(string defaultSqlitePath)
    {
        if (!Exists)
            return new DatabaseSettings(DatabaseProvider.Sqlite, $"Data Source={defaultSqlitePath}");

        string? provider, connection;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(FilePath));
            provider = node?["provider"]?.GetValue<string>();
            connection = node?["connectionString"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"{FilePath}: файл повреждён ({ex.Message}).", ex);
        }
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException($"{FilePath}: нужны поля provider и connectionString.");
        try
        {
            return new DatabaseSettings(ParseProvider(provider), connection);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{FilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>Сохраняет настройки, шифруя пароль PostgreSQL.</summary>
    public void Write(DatabaseSettings settings)
    {
        string connection = settings.ConnectionString;
        if (settings.Provider == DatabaseProvider.PostgreSql)
        {
            var cs = new NpgsqlConnectionStringBuilder(connection);
            if (!string.IsNullOrEmpty(cs.Password) && !ProtectedValue.IsProtected(cs.Password))
                cs.Password = protector.Protect(cs.Password, PasswordPurpose);
            connection = cs.ConnectionString;
        }

        var json = new JsonObject
        {
            ["provider"] = ProviderName(settings.Provider),
            ["connectionString"] = connection,
        };
        Directory.CreateDirectory(dataDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Строка подключения для использования: пароль расшифрован.</summary>
    public DatabaseSettings ResolveForUse(DatabaseSettings stored)
    {
        if (stored.Provider != DatabaseProvider.PostgreSql)
            return stored;
        var cs = new NpgsqlConnectionStringBuilder(stored.ConnectionString);
        if (ProtectedValue.IsProtected(cs.Password))
        {
            try
            {
                cs.Password = protector.Unprotect(cs.Password!, PasswordPurpose);
            }
            catch (SecretUnavailableException ex)
            {
                throw new SecretUnavailableException(
                    $"Пароль базы данных в {FilePath} не расшифровывается: {ex.Message} " +
                    "Восстановите ключ (WinAdmin.exe keys import) или задайте подключение заново (WinAdmin.exe db set).", ex);
            }
        }
        return stored with { ConnectionString = cs.ConnectionString };
    }

    /// <summary>Описание для вывода — без пароля.</summary>
    public static string Describe(DatabaseSettings settings)
    {
        if (settings.Provider == DatabaseProvider.Sqlite)
            return $"sqlite: {settings.ConnectionString}";
        var cs = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Password = null };
        return $"postgresql: {cs.ConnectionString}";
    }
}
