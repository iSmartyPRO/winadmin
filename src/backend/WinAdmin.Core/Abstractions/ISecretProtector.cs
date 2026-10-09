namespace WinAdmin.Core.Abstractions;

/// <summary>
/// Шифрование секретов (пароли служебных учёток, ключи API, пароль БД).
/// purpose — назначение секрета: зашифрованное значение нельзя подставить в другое поле.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext, string purpose);

    /// <summary>Бросает <see cref="SecretUnavailableException"/>, если расшифровать нельзя.</summary>
    string Unprotect(string protectedValue, string purpose);
}

public static class ProtectedValue
{
    public const string Prefix = "enc:v1:";

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
}

/// <summary>Секрет недоступен: нет ключа, другой ключ или значение повреждено.</summary>
public sealed class SecretUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
