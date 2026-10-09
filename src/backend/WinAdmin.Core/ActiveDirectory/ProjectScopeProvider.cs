using WinAdmin.Core.Modules;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Область «Проекты (OU)»: элемент — DN OU; право на OU распространяется на всё внутри неё.</summary>
public sealed class ProjectScopeProvider : IScopeProvider
{
    public string Title => "Проекты (OU)";

    public ScopeDefinition Normalize(ScopeDefinition scope)
    {
        var items = scope.Items.Select(i => (i ?? "").Trim()).Where(i => i.Length > 0).Select(i =>
        {
            var parts = DnUtils.Split(i);
            if (parts.Count == 0 || !parts[0].StartsWith("OU=", StringComparison.OrdinalIgnoreCase) || parts.Any(p => !p.Contains('=')))
                throw new ArgumentException($"«{i}» — не DN подразделения (OU=…).");
            return DnUtils.Normalize(i);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) throw new ArgumentException("Укажите хотя бы один проект.");
        return new ScopeDefinition(items);
    }

    public bool IsSubsetOf(ScopeDefinition candidate, ScopeDefinition container)
        => candidate.Items.All(c => container.Items.Any(p => DnUtils.IsUnderOrSame(c, p)));
}
