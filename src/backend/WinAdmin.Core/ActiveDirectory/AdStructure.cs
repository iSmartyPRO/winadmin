namespace WinAdmin.Core.ActiveDirectory;

public enum AdWriteMode { ServiceAccount, ProcessAccount }

/// <summary>Структура каталога для модулей AD. Пароль учётки записи сюда не попадает — только флаг.</summary>
public sealed record AdStructureSettings(
    string? RootOu, string UsersOuName, IReadOnlyList<string> HiddenOus,
    AdWriteMode WriteMode, string? WriteLogin, bool HasWritePassword = false)
{
    public static AdStructureSettings Default { get; } = new(null, "Users", [], AdWriteMode.ServiceAccount, null);

    public AdStructureSettings Normalize()
    {
        string? root = string.IsNullOrWhiteSpace(RootOu) ? null : DnUtils.Normalize(RootOu);
        if (root is not null)
        {
            var parts = DnUtils.Split(root);
            if (!parts[0].StartsWith("OU=", StringComparison.OrdinalIgnoreCase)
                || !parts.Any(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                || parts.Any(p => !p.Contains('=')))
                throw new ArgumentException("Корневая OU — DN вида OU=…,DC=…,DC=….");
        }
        return this with
        {
            RootOu = root,
            UsersOuName = string.IsNullOrWhiteSpace(UsersOuName) ? "Users" : UsersOuName.Trim(),
            HiddenOus = (HiddenOus ?? []).Select(h => (h ?? "").Trim()).Where(h => h.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WriteLogin = string.IsNullOrWhiteSpace(WriteLogin) ? null : WriteLogin.Trim(),
        };
    }
}

/// <summary>Учётка записи: служебная (логин/пароль) или процесса службы.</summary>
public sealed record AdWriteCredential(AdWriteMode Mode, string? Login, string? Password)
{
    public override string ToString() => Mode == AdWriteMode.ProcessAccount ? "учётка службы" : Login ?? "(не задана)";
}

public sealed record AdProject(string Dn, string Name);

/// <summary>Что учётке записи разрешено на объекте (allowedAttributesEffective / allowedChildClassesEffective).</summary>
public sealed record EffectiveRights(IReadOnlySet<string> Attributes, IReadOnlySet<string> ChildClasses);

/// <summary>Состояние учётки записи для проверки окружения.</summary>
public sealed record WriterStatus(
    string Account, bool Bound, string? Error, bool? Enabled, bool? Locked, DateTimeOffset? PasswordExpires, bool Encrypted);
