using System.Security.Cryptography;
using System.Text;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>AES-256-GCM: enc:v1:base64(nonce12 | ciphertext | tag16), AAD = purpose.</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Ключ шифрования должен быть 32 байта.", nameof(key));
        _key = key.ToArray();
    }

    public string Protect(string plaintext, string purpose)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));
        return ProtectedValue.Prefix + Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    public string Unprotect(string protectedValue, string purpose)
    {
        if (!ProtectedValue.IsProtected(protectedValue))
            throw new SecretUnavailableException("Значение не зашифровано.");

        byte[] data;
        try
        {
            data = Convert.FromBase64String(protectedValue[ProtectedValue.Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new SecretUnavailableException("Зашифрованное значение повреждено.", ex);
        }
        if (data.Length < NonceSize + TagSize)
            throw new SecretUnavailableException("Зашифрованное значение повреждено.");

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(data.Length - TagSize);
        var cipher = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        byte[] plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(purpose));
        }
        catch (CryptographicException ex)
        {
            throw new SecretUnavailableException(
                "Не удалось расшифровать секрет: значение зашифровано другим ключом или повреждено.", ex);
        }
        return Encoding.UTF8.GetString(plain);
    }
}
