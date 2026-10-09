using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;

namespace WinAdmin.Infrastructure.Modules;

public static class ModuleSettingsSchema
{
    public static IReadOnlyList<SettingsField> Build(Type settingsType)
        => settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => new SettingsField(
                JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? p.Name,
                KindOf(p)))
            .ToList();

    public static bool IsSecret(PropertyInfo p) => p.GetCustomAttribute<SecretAttribute>() is not null;

    private static string KindOf(PropertyInfo p)
    {
        if (IsSecret(p)) return "secret";
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(decimal)) return "number";
        if (t != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(t)) return "stringList";
        return "string";
    }
}
