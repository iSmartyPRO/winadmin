namespace WinAdmin.Infrastructure;

/// <summary>Единый расчёт путей к БД и каталогу данных (веб-режим и CLI).</summary>
public static class WinAdminPaths
{
    public const string NetworkFileName = "network.json";

    /// <summary>
    /// Путь к SQLite: WinAdmin:DatabasePath, иначе машинная переменная WinAdmin__DatabasePath
    /// (её не видят консоли, открытые до установки, и служба до перезагрузки), иначе WinAdmin.db рядом с exe.
    /// </summary>
    public static string DatabasePath(string? configured) => DatabasePath(configured, MachineDatabasePath);

    /// <summary>Значение машинной переменной WinAdmin__DatabasePath (читается из реестра напрямую).</summary>
    public static string? MachineDatabasePath()
        => Environment.GetEnvironmentVariable("WinAdmin__DatabasePath", EnvironmentVariableTarget.Machine);

    public static string DatabasePath(string? configured, Func<string?> machineValue)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        string? machine = machineValue();
        return string.IsNullOrWhiteSpace(machine)
            ? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db")
            : machine;
    }

    /// <summary>Каталог данных — папка, где лежит БД.</summary>
    public static string DataDirectory(string databasePath)
        => Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? AppContext.BaseDirectory;
}
