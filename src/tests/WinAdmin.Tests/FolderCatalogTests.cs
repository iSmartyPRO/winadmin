using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class FolderDescriptionParserTests
{
    [Theory]
    [InlineData(@"A:\ТЕХНО-ЦЕНТР\01. Общие ресурсы;Full Access", null, "sg_x", @"A:\ТЕХНО-ЦЕНТР\01. Общие ресурсы", FolderAccess.Full)]
    [InlineData(@"A:\Проект\Docs; Read Only", null, "sg_x", @"A:\Проект\Docs", FolderAccess.Read)]
    [InlineData(@"A:\Проект\Docs;ro", null, "sg_x", @"A:\Проект\Docs", FolderAccess.Read)]
    [InlineData(@"A:\Проект\Docs;F", null, "sg_x", @"A:\Проект\Docs", FolderAccess.Full)]
    [InlineData(@"A:\Проект\Docs", null, "sg_a_docs_full", @"A:\Проект\Docs", FolderAccess.Full)]
    [InlineData(@"A:\Проект\Docs", null, "sg_a_docs_read_only", @"A:\Проект\Docs", FolderAccess.Read)]
    [InlineData(null, @"\\fs01\share\Docs;Full", "sg_x", @"\\fs01\share\Docs", FolderAccess.Full)]
    [InlineData(@"примечание;A:\Docs;;Read", null, "sg_x", @"A:\Docs", FolderAccess.Read)]
    public void Parser_handles_access_forms(string? description, string? info, string name, string path, FolderAccess access)
    {
        var parsed = FolderDescriptionParser.Parse(description, info, name)!;
        Assert.Equal(path, parsed.Path);
        Assert.Equal(access, parsed.Access);
    }

    [Fact]
    public void Unknown_access_is_other_and_no_path_is_null()
    {
        var other = FolderDescriptionParser.Parse(@"A:\Docs;Аудит", null, "sg_x")!;
        Assert.Equal(FolderAccess.Other, other.Access);
        Assert.Equal("Аудит", other.AccessText);
        Assert.Null(FolderDescriptionParser.Parse("просто текст", null, "sg_x_full"));
        Assert.Null(FolderDescriptionParser.Parse(null, null, "sg_x_full"));
    }

    [Fact]
    public void Normalized_key_ignores_case_and_trailing_slash()
        => Assert.Equal(FolderDescriptionParser.NormalizeKey(@"A:\Проект\Docs\"), FolderDescriptionParser.NormalizeKey(@"a:\проект\docs"));
}

public sealed class FolderCatalogTests
{
    private const string Root = "OU=Accounts,DC=pcs";
    private const string A = "OU=A," + Root;

    private static AdFolderGroup G(string name, string? description, params string[] members)
        => new($"CN={name},{A}", name, "S-1-5-21-1-" + Math.Abs(name.GetHashCode()), description, null, true, members);

    private static readonly Dictionary<string, AdMember> Members = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CN=ivan,OU=Users," + A] = new("CN=ivan,OU=Users," + A, "Иван", "ivan", false, true),
        ["CN=petr,OU=Users," + A] = new("CN=petr,OU=Users," + A, "Пётр", "petr", false, true),
    };

    [Fact]
    public void Catalog_merges_by_normalized_path()
    {
        var result = FolderCatalog.Build([
            G("sg_a_docs_full", @"A:\Проект\Docs;Full Access", "CN=ivan,OU=Users," + A),
            G("sg_a_docs_read", @"a:\проект\docs\;Read Only", "CN=petr,OU=Users," + A),
        ], Members, Root, ['A']);

        var folder = Assert.Single(result.Folders);
        Assert.Equal("A", folder.ProjectName);
        Assert.Equal("ivan", Assert.Single(folder.Full!.Members).Sam);
        Assert.Equal("petr", Assert.Single(folder.Read!.Members).Sam);
        Assert.Empty(folder.Warnings);
    }

    [Fact]
    public void Warnings_for_duplicates_missing_pair_and_unmapped_drive()
    {
        var result = FolderCatalog.Build([
            G("sg_a_x_full", @"B:\X;Full Access"),
            G("sg_a_x2_full", @"B:\X;Full Access"),
            G("sg_a_bad", "без пути"),
        ], Members, Root, ['A']);

        var folder = Assert.Single(result.Folders);
        Assert.Contains("две группы Full", folder.Warnings);
        Assert.Contains("нет группы Read", folder.Warnings);
        Assert.Contains("буква диска B: не сопоставлена", folder.Warnings);
        Assert.Equal("sg_a_bad", Assert.Single(result.Unparsed).Name);
    }

    [Fact]
    public void Unknown_members_are_shown_by_dn_name()
    {
        var folder = Assert.Single(FolderCatalog.Build([G("sg_a_y_full", @"A:\Y;Full", "CN=Гость,OU=Other,DC=pcs")], Members, Root, ['A']).Folders);
        Assert.Equal("Гость", Assert.Single(folder.Full!.Members).Name);
    }

    [Fact]
    public void Search_by_path_or_words()
    {
        var folders = FolderCatalog.Build([
            G("sg_a_docs_full", @"A:\Проект\Документы;Full"),
            G("sg_a_photo_full", @"A:\Проект\Фото;Full"),
        ], Members, Root, ['A']).Folders;
        Assert.Equal(@"A:\Проект\Документы", Assert.Single(FolderCatalog.Search(folders, @"a:\проект\документы")).Path);
        Assert.Equal(2, FolderCatalog.Search(folders, @"A:\Проект").Count);
        Assert.Equal(@"A:\Проект\Фото", Assert.Single(FolderCatalog.Search(folders, "фото проект")).Path);
        Assert.Equal(2, FolderCatalog.Search(folders, " ").Count);
    }

    [Fact]
    public void User_access_lists_full_and_read()
    {
        var folders = FolderCatalog.Build([
            G("sg_a_docs_full", @"A:\Docs;Full", "CN=ivan,OU=Users," + A),
            G("sg_a_docs_read", @"A:\Docs;Read", "CN=ivan,OU=Users," + A),
            G("sg_a_x_read", @"A:\X;Read", "CN=petr,OU=Users," + A),
        ], Members, Root, ['A']).Folders;
        var access = Assert.Single(FolderCatalog.UserAccess(folders, "CN=ivan,OU=Users," + A));
        Assert.True(access.HasFull);
        Assert.True(access.HasRead);
    }
}

public sealed class FolderNamingTests
{
    private static readonly IReadOnlyDictionary<char, string> Map = FolderNaming.ParseMappings([@"A=\\fs01\Projects", @"b:=\\fs02\B\"]);

    [Fact]
    public void Maps_drive_paths_and_keeps_unc()
    {
        Assert.Equal(@"\\fs01\Projects\Проект\Docs", FolderNaming.ToUnc(@"A:\Проект\Docs", Map));
        Assert.Equal(@"\\fs02\B\X", FolderNaming.ToUnc(@"B:\X\", Map));
        Assert.Equal(@"\\fs03\s\X", FolderNaming.ToUnc(@"\\fs03\s\X", Map));
    }

    [Theory]
    [InlineData(@"C:\X")]
    [InlineData(@"A:\X\..\Windows")]
    [InlineData("просто текст")]
    [InlineData(@"\\server")]
    public void Mapping_rejects_unmapped_and_dot_segments(string path)
        => Assert.Throws<ArgumentException>(() => FolderNaming.ToUnc(path, Map));

    [Theory]
    [InlineData("A")]
    [InlineData(@"AB=\\fs\x")]
    [InlineData("A=C:\\local")]
    public void Bad_mapping_lines_are_rejected(string line)
        => Assert.Throws<ArgumentException>(() => FolderNaming.ParseMappings([line]));

    [Fact]
    public void Org_code_from_existing_groups_or_project_name()
    {
        Assert.Equal("co", FolderNaming.DefaultOrgCode("Сервисный центр", ["sg_co_a_full", "sg_co_b_read", "sg_x_c_full", "other"], "sg_"));
        Assert.Equal("amur", FolderNaming.DefaultOrgCode("Amur", [], "sg_"));
        Assert.Null(FolderNaming.DefaultOrgCode("Амур", [], "sg_"));
    }

    [Fact]
    public void Group_names_are_validated()
    {
        Assert.Equal(("sg_co_docs_full", "sg_co_docs_read"), FolderNaming.GroupNames("sg_", "co", "docs"));
        Assert.Throws<ArgumentException>(() => FolderNaming.GroupNames("sg_", "co", "доки"));
        Assert.Throws<ArgumentException>(() => FolderNaming.GroupNames("sg_", "co", new string('x', 41)));
        Assert.Throws<ArgumentException>(() => FolderNaming.GroupNames("sg_", "co x", "docs"));
    }
}

public sealed class AdFoldersModuleTests
{
    [Fact]
    public void Module_declares_scoped_permissions_and_domain_requirement()
    {
        var module = Assert.Single(BuiltInModules.All, m => m.Id == AdFoldersModule.ModuleId);
        Assert.Equal(ModuleRequirements.DomainJoined, module.Requirements);
        Assert.All(module.Permissions, p => Assert.True(p.Scopable));
        Assert.True(module.Permissions.Single(p => p.Id == PermissionIds.AdFoldersCreate).Dangerous);
        Assert.Equal("sg_", new AdFoldersSettings().GroupPrefix);
    }
}
