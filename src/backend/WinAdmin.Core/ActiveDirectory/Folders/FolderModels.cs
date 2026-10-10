namespace WinAdmin.Core.ActiveDirectory.Folders;

public enum FolderAccess { Full, Read, Other }

public sealed record ParsedFolder(string Path, FolderAccess Access, string? AccessText);

/// <summary>Группа доступа к папке, как она прочитана из AD.</summary>
public sealed record AdFolderGroup(
    string Dn, string Name, string? Sid, string? Description, string? Info, bool IsSecurity, IReadOnlyList<string> MemberDns);

public sealed record AdMember(string Dn, string Name, string? Sam, bool IsGroup, bool Enabled);

public sealed record FolderGroupView(string Dn, string Name, string? Sid, IReadOnlyList<AdMember> Members);

public sealed record Folder(
    string Path, string? ProjectDn, string? ProjectName, FolderGroupView? Full, FolderGroupView? Read,
    IReadOnlyList<FolderGroupView> Others, IReadOnlyList<string> Warnings);

public sealed record UnparsedGroup(string Dn, string Name, string? ProjectName, string Reason);

public sealed record FolderCatalogResult(IReadOnlyList<Folder> Folders, IReadOnlyList<UnparsedGroup> Unparsed);

public sealed record UserFolderAccess(string? ProjectName, string Path, bool HasFull, bool HasRead);
