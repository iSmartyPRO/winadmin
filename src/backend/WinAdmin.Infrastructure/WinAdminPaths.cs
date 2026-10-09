namespace WinAdmin.Infrastructure;

/// <summary>Единый расчёт путей к БД и каталогу данных (веб-режим и CLI).</summary>
public static class WinAdminPaths
{
    public const string NetworkFileName = "network.json";

    /// <summary>Путь к SQLite: WinAdmin:DatabasePath либо WinAdmin.db рядом с exe.</summary>
    public static string DatabasePath(string? configured)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db")
            : configured;

    /// <summary>Каталог данных — папка, где лежит БД.</summary>
    public static string DataDirectory(string databasePath)
        => Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? AppContext.BaseDirectory;
}
