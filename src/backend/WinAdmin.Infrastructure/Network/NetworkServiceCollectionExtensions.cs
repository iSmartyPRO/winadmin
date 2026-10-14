using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Network;

public static class NetworkServiceCollectionExtensions
{
    /// <summary>Сетевые настройки панели (используется и веб-режимом, и CLI).</summary>
    public static IServiceCollection AddWinAdminNetwork(this IServiceCollection services, NetworkSettingsStore store)
    {
        services.AddSingleton(store);
        services.AddSingleton<IFirewallRunner, NetshFirewallRunner>();
        services.AddSingleton<IPortProbe, SystemPortProbe>();
        services.AddSingleton<ICertificateStore, SystemCertificateStore>();
        // Каталог ключей — keys рядом с network.json (там же master.key); пароль PFX шифруется ISecretProtector.
        services.AddSingleton<IHttpsCertificateLoader>(sp => new HttpsCertificateLoader(
            sp.GetRequiredService<ICertificateStore>(), sp.GetRequiredService<ISecretProtector>(),
            Path.Combine(store.DataDirectory, "keys")));
        services.AddSingleton<INetworkSettingsService, NetworkSettingsService>();
        return services;
    }
}
