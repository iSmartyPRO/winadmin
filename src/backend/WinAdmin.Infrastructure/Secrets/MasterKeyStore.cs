using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Hardening;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>
/// Ключ шифрования секретов: 32 байта в keys\master.key под DPAPI (LocalMachine),
/// файл доступен только администраторам, SYSTEM и текущему пользователю процесса.
/// </summary>
public sealed class MasterKeyStore
{
    public const string FileName = "master.key";
    private static readonly byte[] Entropy = "WinAdmin.MasterKey.v1"u8.ToArray();
    private static readonly byte[] ExportMagic = "WAKEY1"u8.ToArray();
    private static readonly byte[] ExportAad = "winadmin-key-export"u8.ToArray();
    private const int KeySize = 32, SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 600_000;

    public MasterKeyStore(string keysDirectory)
    {
        KeysDirectory = keysDirectory;
        FilePath = Path.Combine(keysDirectory, FileName);
    }

    public string KeysDirectory { get; }
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);

    public byte[] LoadOrCreate()
    {
        if (Exists) return Load();
        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        // Другой процесс (служба / CLI) мог создать ключ одновременно — побеждает первый, остальные читают его.
        return Save(key, overwrite: false) ? key : Load();
    }

    /// <summary>
    /// Каталог keys — защищённый DACL (Администраторы, SYSTEM, текущий процесс), файлы наследуют только его.
    /// Вызывается при создании ключа и после исправления прав на папку приложения/данных.
    /// </summary>
    public void ProtectKeysDirectory(Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        Directory.CreateDirectory(KeysDirectory);
        InstallationHardening.HardenDirectory(KeysDirectory, KeyFileRules(), logger ?? NullLogger.Instance);
    }

    public byte[] Load()
    {
        try
        {
            byte[] key = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.LocalMachine);
            if (key.Length != KeySize) throw new CryptographicException("Неверная длина ключа.");
            return key;
        }
        catch (CryptographicException ex)
        {
            throw new SecretUnavailableException(
                $"Ключ шифрования {FilePath} не читается (повреждён или создан на другой машине). " +
                "Восстановите его командой WinAdmin.exe keys import.", ex);
        }
    }

    /// <summary>Ключ, зашифрованный паролем: WAKEY1 | salt16 | nonce12 | key32 | tag16.</summary>
    public byte[] Export(string password)
    {
        byte[] key = Load();
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[KeySize];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(DeriveKey(password, salt), TagSize))
            aes.Encrypt(nonce, key, cipher, tag, ExportAad);
        return [.. ExportMagic, .. salt, .. nonce, .. cipher, .. tag];
    }

    public void Import(byte[] exported, string password, bool overwrite)
    {
        if (Exists && !overwrite)
            throw new InvalidOperationException(
                $"Ключ {FilePath} уже существует. Замена сделает нерасшифровываемыми секреты, " +
                "зашифрованные текущим ключом. Повторите с --force, если это действительно нужно.");

        int expected = ExportMagic.Length + SaltSize + NonceSize + KeySize + TagSize;
        if (exported.Length != expected || !exported.AsSpan(0, ExportMagic.Length).SequenceEqual(ExportMagic))
            throw new InvalidOperationException("Файл не является экспортом ключа WinAdmin.");

        int o = ExportMagic.Length;
        var salt = exported.AsSpan(o, SaltSize).ToArray(); o += SaltSize;
        var nonce = exported.AsSpan(o, NonceSize); o += NonceSize;
        var cipher = exported.AsSpan(o, KeySize); o += KeySize;
        var tag = exported.AsSpan(o, TagSize);
        byte[] key = new byte[KeySize];
        try
        {
            using var aes = new AesGcm(DeriveKey(password, salt), TagSize);
            aes.Decrypt(nonce, cipher, tag, key, ExportAad);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Неверный пароль или повреждённый файл экспорта ключа.");
        }
        Save(key, overwrite: true);
    }

    public static IReadOnlyList<AclRule> KeyFileRules() =>
    [
        .. AclPlan.ForDataDirectory(),
        new AclRule(WindowsIdentity.GetCurrent().User!.Value, FileSystemRights.FullControl),
    ];

    /// <summary>Атомарная запись; без overwrite возвращает false, если ключ уже создан другим процессом.</summary>
    private bool Save(byte[] key, bool overwrite)
    {
        ProtectKeysDirectory();
        string tmp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine));
        InstallationHardening.ProtectFile(tmp, KeyFileRules(), NullLogger.Instance);
        try
        {
            File.Move(tmp, FilePath, overwrite);
            return true;
        }
        catch (IOException) when (!overwrite && File.Exists(FilePath))
        {
            File.Delete(tmp);
            return false;
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
