namespace WinAdmin.Core.ActiveDirectory;

/// <summary>AD отклонил запись; Code — код результата LDAP, Message — текст для оператора.</summary>
public sealed class AdWriteException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
