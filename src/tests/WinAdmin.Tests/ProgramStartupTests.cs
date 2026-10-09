using System.Diagnostics;

namespace WinAdmin.Tests;

/// <summary>Запуск настоящего WinAdmin.dll отдельным процессом: поведение при сломанной конфигурации.</summary>
public sealed class ProgramStartupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-start-" + Guid.NewGuid().ToString("N"));

    public ProgramStartupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (int Code, string Output) Start()
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{Path.Combine(AppContext.BaseDirectory, "WinAdmin.dll")}\"")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["WinAdmin__DatabasePath"] = Path.Combine(_dir, "WinAdmin.db");
        psi.Environment["ASPNETCORE_URLS"] = "";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("WinAdmin не завершился — ожидался отказ запуска.");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    [Fact]
    public void Encrypted_database_password_without_key_stops_with_clear_message()
    {
        File.WriteAllText(Path.Combine(_dir, "database.json"),
            "{ \"provider\": \"postgresql\", \"connectionString\": \"Host=db;Username=wa;Password=enc:v1:AAAA\" }");

        var (code, output) = Start();

        Assert.Equal(1, code);
        Assert.Contains("keys import", output);
        Assert.DoesNotContain("   at ", output); // без стектрейса
        Assert.False(File.Exists(Path.Combine(_dir, "keys", "master.key"))); // новый ключ не выпущен
    }

    [Fact]
    public void Corrupt_database_settings_stop_with_clear_message()
    {
        File.WriteAllText(Path.Combine(_dir, "database.json"), "{ broken");

        var (code, output) = Start();

        Assert.Equal(1, code);
        Assert.Contains("database.json", output);
        Assert.DoesNotContain("   at ", output);
    }
}
