using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Hardening;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>JWT-секрет, сгенерированный один раз и хранимый зашифрованным в keys\jwt.key.</summary>
public sealed class JwtSecretStore(string keysDirectory, ISecretProtector protector)
{
    public const string FileName = "jwt.key";
    private const string Purpose = "platform:jwt";

    public string FilePath { get; } = Path.Combine(keysDirectory, FileName);

    public string GetOrCreate()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                return protector.Unprotect(File.ReadAllText(FilePath).Trim(), Purpose);
            }
            catch (SecretUnavailableException)
            {
                // Повреждён или зашифрован другим ключом — выпускаем новый: старые сессии всё равно недействительны.
            }
        }

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        Directory.CreateDirectory(keysDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, protector.Protect(secret, Purpose));
        InstallationHardening.ProtectFile(tmp, MasterKeyStore.KeyFileRules(), NullLogger.Instance);
        File.Move(tmp, FilePath, overwrite: true);
        return secret;
    }

    /// <summary>Секрет из конфигурации (WinAdmin:Jwt:Secret) имеет приоритет над сохранённым.</summary>
    public static string Resolve(string? configured, Func<string> fromStore)
        => string.IsNullOrWhiteSpace(configured) ? fromStore() : configured;
}
