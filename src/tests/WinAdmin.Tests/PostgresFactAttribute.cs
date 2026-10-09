namespace WinAdmin.Tests;

/// <summary>Тест на настоящем PostgreSQL: выполняется, только если задана WINADMIN_TEST_POSTGRES.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("WINADMIN_TEST_POSTGRES");

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Skip = "WINADMIN_TEST_POSTGRES не задана (см. src/tests/start-test-postgres.ps1).";
    }
}
