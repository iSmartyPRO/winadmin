using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>
/// Единая точка получения ключа для службы и CLI. Новый ключ создаётся только на чистой
/// установке: если зашифрованные данные уже есть, а ключа нет, — молча выпущенный ключ
/// навсегда «потерял» бы их и помешал бы восстановлению через keys import.
/// </summary>
public static class SecretProtectorFactory
{
    public static ISecretProtector Open(MasterKeyStore keys, string dataDirectory, out string? problem)
    {
        problem = null;
        try
        {
            if (keys.Exists)
                return new AesGcmSecretProtector(keys.Load());
            if (ProtectedDataExists(keys, dataDirectory))
            {
                problem = $"Ключ шифрования {keys.FilePath} не найден, а зашифрованные данные уже есть. " +
                          "Восстановите ключ командой WinAdmin.exe keys import.";
                return new UnavailableSecretProtector(problem);
            }
            return new AesGcmSecretProtector(keys.LoadOrCreate());
        }
        catch (Exception ex) when (ex is SecretUnavailableException or IOException or UnauthorizedAccessException)
        {
            problem = ex is SecretUnavailableException
                ? ex.Message
                : $"Ключ шифрования {keys.FilePath} недоступен: {ex.Message} Восстановите ключ командой WinAdmin.exe keys import.";
            return new UnavailableSecretProtector(problem);
        }
    }

    /// <summary>Признаки того, что ключ когда-то уже был: jwt.key или зашифрованный пароль в database.json.</summary>
    public static bool ProtectedDataExists(MasterKeyStore keys, string dataDirectory)
        => File.Exists(Path.Combine(keys.KeysDirectory, JwtSecretStore.FileName))
           || DatabaseSettingsStore.FileHasProtectedValues(dataDirectory);
}

/// <summary>Открывает ключ при первом использовании (CLI: keys import / network не трогают ключ).</summary>
public sealed class LazySecretProtector(Func<ISecretProtector> open) : ISecretProtector
{
    private readonly Lazy<ISecretProtector> _inner = new(open);

    public string Protect(string plaintext, string purpose) => _inner.Value.Protect(plaintext, purpose);
    public string Unprotect(string protectedValue, string purpose) => _inner.Value.Unprotect(protectedValue, purpose);
}
