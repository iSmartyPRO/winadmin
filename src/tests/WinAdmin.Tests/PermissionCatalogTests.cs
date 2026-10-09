using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class PermissionCatalogTests
{
    private sealed class TestModule(string id, params PermissionDefinition[] permissions) : IWinAdminModule
    {
        public string Id => id;
        public string Title => id;
        public string? Description => null;
        public ModuleRequirements Requirements => ModuleRequirements.None;
        public bool EnabledByDefault => true;
        public IReadOnlyList<PermissionDefinition> Permissions => permissions;
        public Type? SettingsType => null;
        public IScopeProvider? Scope => null;
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }

    [Fact]
    public void Built_in_modules_form_a_valid_catalog_with_platform_permissions()
    {
        var catalog = new PermissionCatalog(BuiltInModules.All);

        Assert.Contains(catalog.All, p => p.Id == PermissionIds.PlatformRolesManage);
        Assert.Contains(catalog.All, p => p.Id == PermissionIds.ServicesManage);
        Assert.Equal("services", catalog.ModuleOf(PermissionIds.ServicesManage)!.Id);
        Assert.Null(catalog.ModuleOf(PermissionIds.PlatformRolesManage));
        Assert.Null(catalog.Find("disks.read")); // объединено с system.read
        Assert.Equal(catalog.All.Count, catalog.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("Services")]
    [InlineData("platform")]
    [InlineData("1abc")]
    public void Rejects_invalid_module_ids(string id)
        => Assert.Throws<InvalidOperationException>(() => new PermissionCatalog([new TestModule(id)]));

    [Fact]
    public void Rejects_permission_outside_module_prefix()
        => Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new TestModule("ad", new PermissionDefinition("users.read", "x"))]));

    [Fact]
    public void Rejects_duplicate_modules_and_permissions()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new TestModule("ad"), new TestModule("ad")]));
        Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new TestModule("ad", new("ad.x", "1"), new("ad.x", "2"))]));
    }
}
