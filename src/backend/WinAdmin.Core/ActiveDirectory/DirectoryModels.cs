namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Подключение к домену. Server пусто — поиск контроллера через DNS; BaseDn пусто — defaultNamingContext.</summary>
public sealed record DirectorySettings(bool Enabled, string? Domain, string? Server, string? BaseDn, bool UseLdaps)
{
    public static DirectorySettings Disabled { get; } = new(false, null, null, null, false);

    /// <summary>Пробелы обрезаются, пустые строки → null; включено без домена — ArgumentException.</summary>
    public DirectorySettings Normalize()
    {
        var clean = new DirectorySettings(Enabled, Blank(Domain), Blank(Server), Blank(BaseDn), UseLdaps);
        if (clean.Enabled && clean.Domain is null)
            throw new ArgumentException("Укажите домен.");
        return clean;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum DirectoryObjectKind { User, Group }

/// <summary>Пользователь или группа AD.</summary>
public sealed record DirectoryObject(
    string Sid, DirectoryObjectKind Kind, string SamAccountName, string? DisplayName,
    string? Upn, string? DistinguishedName, bool Enabled)
{
    public string LoginName => Upn ?? SamAccountName;
}

/// <summary>Шаг проверки подключения (для UI).</summary>
public sealed record DirectoryTestStep(string Name, bool Ok, string Message);

/// <summary>Контроллер домена недоступен или подключение выключено.</summary>
public sealed class DirectoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
