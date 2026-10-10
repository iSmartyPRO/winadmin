using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Operations;

public enum StepStatus { Ok, Skipped, Failed }

public sealed record ScenarioStep(string Name, StepStatus Status, string? Message);

public sealed record StepResult(StepStatus Status, string? Message)
{
    public static StepResult Done(string? message = null) => new(StepStatus.Ok, message);
    public static StepResult Skip(string message) => new(StepStatus.Skipped, message);
}

/// <summary>
/// Сценарий из шагов: ошибка останавливает выполнение, следующие шаги помечаются «не выполнялся».
/// Ожидаемые ошибки (AD, доступ, проверки) показываются как есть; прочие — общим текстом и в лог.
/// </summary>
public sealed class ScenarioRunner(Action<Exception>? log = null)
{
    private readonly List<ScenarioStep> _steps = [];

    public IReadOnlyList<ScenarioStep> Steps => _steps;
    public bool Failed { get; private set; }

    public async Task<bool> RunAsync(string name, Func<Task<StepResult>> action)
    {
        if (Failed)
        {
            _steps.Add(new(name, StepStatus.Skipped, "Не выполнялся: предыдущий шаг завершился ошибкой"));
            return false;
        }
        try
        {
            var result = await action();
            _steps.Add(new(name, result.Status, result.Message));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed = true;
            // Ошибки файлового сервера (нет доступа, путь не найден) — тоже понятны оператору.
            bool expected = ex is AdWriteException or DirectoryUnavailableException or AccessDeniedException
                or ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException;
            if (!expected) log?.Invoke(ex);
            _steps.Add(new(name, StepStatus.Failed, expected ? ex.Message : "Внутренняя ошибка — подробности в журнале службы"));
            return false;
        }
    }
}
