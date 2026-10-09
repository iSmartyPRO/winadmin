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
        services.AddSingleton<INetworkSettingsService, NetworkSettingsService>();
        return services;
    }
}
