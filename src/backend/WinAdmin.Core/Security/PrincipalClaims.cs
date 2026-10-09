using System.Security.Claims;

namespace WinAdmin.Core.Security;

/// <summary>Субъект в claims: wa:principal = «Тип:id», wa:group = SID группы AD.</summary>
public static class PrincipalClaims
{
    public const string Type = "wa:principal";
    public const string GroupType = "wa:group";

    public static string Format(PrincipalType type, string id) => $"{type}:{id}";

    /// <summary>
    /// Субъект запроса. Без claim wa:principal (JWT до обновления, API-ключ) — по NameIdentifier:
    /// схема API-ключа → ApiKey, иначе — локальный пользователь.
    /// </summary>
    public static PrincipalRef? Parse(ClaimsPrincipal user, string apiKeyScheme)
    {
        var groups = user.FindAll(GroupType).Select(c => c.Value).ToList();
        string? value = user.FindFirst(Type)?.Value;
        if (value is not null)
        {
            int colon = value.IndexOf(':');
            if (colon > 0 && Enum.TryParse<PrincipalType>(value[..colon], out var type) && colon < value.Length - 1)
                return new PrincipalRef(type, value[(colon + 1)..], groups);
            return null;
        }

        string? id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(id))
            return null;
        bool isKey = user.Identities.Any(i => i.AuthenticationType == apiKeyScheme);
        return new PrincipalRef(isKey ? PrincipalType.ApiKey : PrincipalType.LocalUser, id, groups);
    }
}
