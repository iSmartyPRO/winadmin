using System.Security.AccessControl;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Расширенные права AD по DACL объекта: есть ли Allow без Deny для одного из SID.</summary>
public static class AclInspector
{
    public static readonly Guid ResetPassword = new("00299570-246d-11d0-a768-00aa006e0529");
    private const int ControlAccess = 0x100;
    private const int GenericAll = 0x10000000;
    private const int FullControl = 0x000F01FF;

    public static bool HasExtendedRight(byte[] securityDescriptor, IReadOnlyCollection<string> sids, Guid right)
    {
        var sd = new RawSecurityDescriptor(securityDescriptor, 0);
        if (sd.DiscretionaryAcl is null) return true; // нет DACL — доступ не ограничен
        var set = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
        bool allowed = false;
        foreach (GenericAce ace in sd.DiscretionaryAcl)
        {
            if ((ace.AceFlags & AceFlags.InheritOnly) != 0) continue;
            if (ace is not KnownAce known || !set.Contains(known.SecurityIdentifier.Value)) continue;
            bool full = (known.AccessMask & GenericAll) != 0 || (known.AccessMask & FullControl) == FullControl;
            bool matches = full || ((known.AccessMask & ControlAccess) != 0 && known switch
            {
                ObjectAce oa => (oa.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) == 0 || oa.ObjectAceType == right,
                _ => true,
            });
            if (!matches) continue;
            bool deny = ace.AceType is AceType.AccessDenied or AceType.AccessDeniedObject;
            if (deny) return false;
            allowed = true;
        }
        return allowed;
    }
}
