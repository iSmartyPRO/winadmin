using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>Используется, когда ключ шифрования недоступен: любая операция объясняет причину.</summary>
public sealed class UnavailableSecretProtector(string reason) : ISecretProtector
{
    public string Protect(string plaintext, string purpose) => throw new SecretUnavailableException(reason);
    public string Unprotect(string protectedValue, string purpose) => throw new SecretUnavailableException(reason);
}
