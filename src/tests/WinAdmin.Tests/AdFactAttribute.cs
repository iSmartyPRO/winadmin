namespace WinAdmin.Tests;

/// <summary>
/// Интеграция с настоящим AD (например, через SSH-туннель к 389 контроллера).
/// WINADMIN_TEST_AD_SERVER, WINADMIN_TEST_AD_DOMAIN, WINADMIN_TEST_AD_USER, WINADMIN_TEST_AD_PASSWORD.
/// </summary>
public sealed class AdFactAttribute : FactAttribute
{
    public AdFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_PASSWORD")))
            Skip = "WINADMIN_TEST_AD_* не заданы.";
    }
}
