using System.Text.RegularExpressions;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Modules;

/// <summary>Все права: ядро + модули. Ошибки описания модулей — ошибка разработчика, служба не стартует.</summary>
public sealed class PermissionCatalog
{
    private static readonly Regex ModuleIdPattern = new("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, PermissionDefinition> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IWinAdminModule> _moduleByPermission = new(StringComparer.Ordinal);
    private readonly List<PermissionDefinition> _all = [];

    public PermissionCatalog(IEnumerable<IWinAdminModule> modules)
    {
        Modules = modules.ToList();
        foreach (var p in PermissionIds.Platform)
            Add(p, null);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Modules)
        {
            if (!ModuleIdPattern.IsMatch(m.Id) || m.Id == "platform")
                throw new InvalidOperationException($"Недопустимый id модуля «{m.Id}».");
            if (!ids.Add(m.Id))
                throw new InvalidOperationException($"Модуль «{m.Id}» зарегистрирован дважды.");
            foreach (var p in m.Permissions)
            {
                if (!p.Id.StartsWith(m.Id + ".", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Право «{p.Id}» модуля «{m.Id}» должно начинаться с «{m.Id}.».");
                Add(p, m);
            }
        }
    }

    public IReadOnlyList<IWinAdminModule> Modules { get; }
    public IReadOnlyList<PermissionDefinition> All => _all;

    public PermissionDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Модуль права; null — право ядра или неизвестное.</summary>
    public IWinAdminModule? ModuleOf(string permissionId) => _moduleByPermission.GetValueOrDefault(permissionId);

    public IWinAdminModule? FindModule(string moduleId) => Modules.FirstOrDefault(m => m.Id == moduleId);

    private void Add(PermissionDefinition p, IWinAdminModule? module)
    {
        if (!_byId.TryAdd(p.Id, p))
            throw new InvalidOperationException($"Право «{p.Id}» объявлено дважды.");
        _all.Add(p);
        if (module is not null)
            _moduleByPermission[p.Id] = module;
    }
}
