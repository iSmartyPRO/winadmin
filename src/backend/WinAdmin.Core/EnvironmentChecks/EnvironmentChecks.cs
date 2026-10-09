namespace WinAdmin.Core.EnvironmentChecks;

public enum CheckStatus { Ok, Warning, Failed, Skipped }

public enum CheckDepth { Quick, Full }

/// <summary>Результат проверки: Fix — что сделать администратору.</summary>
public sealed record CheckResult(string Code, string Title, CheckStatus Status, string Message, string? Fix = null)
{
    public static CheckResult Ok(string code, string title, string message) => new(code, title, CheckStatus.Ok, message);
    public static CheckResult Warn(string code, string title, string message, string? fix = null) => new(code, title, CheckStatus.Warning, message, fix);
    public static CheckResult Fail(string code, string title, string message, string? fix = null) => new(code, title, CheckStatus.Failed, message, fix);
    public static CheckResult Skip(string code, string title, string reason) => new(code, title, CheckStatus.Skipped, reason);
}

/// <summary>Проверки окружения модуля («platform» — ядро).</summary>
public interface IEnvironmentCheck
{
    string ModuleId { get; }
    Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct);
}

public sealed record EnvironmentReport(string ModuleId, DateTimeOffset At, CheckStatus Overall, IReadOnlyList<CheckResult> Results)
{
    public static CheckStatus Worst(IEnumerable<CheckResult> results)
    {
        var statuses = results.Select(r => r.Status).Where(s => s != CheckStatus.Skipped).ToList();
        if (statuses.Contains(CheckStatus.Failed)) return CheckStatus.Failed;
        if (statuses.Contains(CheckStatus.Warning)) return CheckStatus.Warning;
        return CheckStatus.Ok;
    }
}
