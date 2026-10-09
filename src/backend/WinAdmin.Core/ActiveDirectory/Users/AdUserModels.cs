namespace WinAdmin.Core.ActiveDirectory.Users;

/// <summary>Пользователь AD; Attributes — запрошенные атрибуты (ключи — имена LDAP без учёта регистра).</summary>
public sealed record AdUser(
    string Sam, string Dn, string Sid, string? DisplayName, bool Enabled,
    DateTimeOffset? LastLogon, DateTimeOffset? WhenCreated, bool HasPhoto, int PrimaryGroupId,
    IReadOnlyDictionary<string, string?> Attributes)
{
    public string? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;
}

public sealed record AdGroupRef(string Dn, string Name, string? Sid, bool IsPrimary);

public enum AdUserStatus { Active, Disabled, Terminated, All }

/// <summary>Пользователь для UI: проект (или «уволен»), атрибуты.</summary>
public sealed record AdUserView(
    string Sam, string Dn, string? DisplayName, bool Enabled, DateTimeOffset? LastLogon, DateTimeOffset? WhenCreated,
    bool HasPhoto, string? ProjectDn, string? ProjectName, bool Terminated, IReadOnlyDictionary<string, string?> Attributes);

public sealed record AdUserCard(AdUserView User, IReadOnlyList<AdGroupRef> Groups);

public sealed record ActivationResult(IReadOnlyList<Operations.ScenarioStep> Steps, string? Password);
