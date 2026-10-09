using System.Text.Json;
using WinAdmin.Core.Modules;

namespace WinAdmin.Infrastructure.Access;

/// <summary>Область в БД — JSON-массив строк; null — без области.</summary>
public static class ScopeJson
{
    public static string? Serialize(ScopeDefinition? scope)
        => scope is null ? null : JsonSerializer.Serialize(scope.Items);

    public static ScopeDefinition? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var items = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return new ScopeDefinition(items);
    }
}
