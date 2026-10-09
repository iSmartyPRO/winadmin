namespace WinAdmin.Api;

/// <summary>Подсказки в журнал при старте, когда управлять WinAdmin некому.</summary>
public static class StartupAdvice
{
    public static IReadOnlyList<string> For(bool anyUsers, int activeAdministrators)
    {
        if (!anyUsers)
            return
            [
                "Пользователи не созданы. Создайте первого администратора:",
                "WinAdmin.exe user add --login admin --password <пароль> --role Администратор",
            ];
        if (activeAdministrators == 0)
            return
            [
                "Нет ни одного активного администратора. Назначьте роль существующему пользователю:",
                "WinAdmin.exe role assign --role Администратор --local <логин>",
            ];
        return [];
    }
}
