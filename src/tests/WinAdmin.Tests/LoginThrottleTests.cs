using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Tests;

public sealed class LoginThrottleTests
{
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);

    [Fact]
    public void Sixth_failure_for_same_login_is_blocked_for_the_rest_of_the_minute()
    {
        var t = new LoginThrottle(_time);
        for (int i = 0; i < 5; i++) { Assert.Null(t.RetryAfter("1.1.1.1", "Ivan")); t.Failed("1.1.1.1", "ivan"); }
        var wait = t.RetryAfter("1.1.1.1", "IVAN");
        Assert.NotNull(wait);
        Assert.InRange(wait!.Value.TotalSeconds, 1, 60);
        Assert.Null(t.RetryAfter("1.1.1.1", "petr")); // другой логин с того же IP — пока можно

        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(t.RetryAfter("1.1.1.1", "ivan"));
    }

    [Fact]
    public void Twenty_failures_from_one_ip_block_every_login()
    {
        var t = new LoginThrottle(_time);
        for (int i = 0; i < 20; i++) t.Failed("2.2.2.2", "u" + i);
        Assert.NotNull(t.RetryAfter("2.2.2.2", "fresh"));
        Assert.Null(t.RetryAfter("3.3.3.3", "fresh"));
    }

    [Fact]
    public void Success_clears_failures_of_the_pair()
    {
        var t = new LoginThrottle(_time);
        for (int i = 0; i < 4; i++) t.Failed("1.1.1.1", "ivan");
        t.Succeeded("1.1.1.1", "ivan");
        t.Failed("1.1.1.1", "ivan");
        Assert.Null(t.RetryAfter("1.1.1.1", "ivan"));
    }
}

[Collection("network-api")]
public sealed class LoginThrottleApiTests(NetworkApiFactory factory)
{
    [Fact]
    public async Task Api_returns_429_with_retry_after()
    {
        var client = factory.ClientFrom("10.9.9." + Random.Shared.Next(1, 250));
        string login = "brute" + Guid.NewGuid().ToString("N")[..6];
        // Локальная учётка: неверный пароль — неудачная попытка с любого адреса (пароль домена с сети дал бы 400).
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest { Login = login, Password = "Right-123456" });
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "x" })).StatusCode);
        var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "x" });
        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.True(r.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Unknown_login_takes_comparable_time_to_wrong_password()
    {
        string login = "timing" + Guid.NewGuid().ToString("N")[..6];
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest { Login = login, Password = "Right-123456" });
        var users = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IUserService>();
        await users.ValidateAsync(login, "warm-up");

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) await users.ValidateAsync(login, "wrong");
        var wrong = sw.Elapsed;
        sw.Restart();
        for (int i = 0; i < 5; i++) await users.ValidateAsync("missing-" + login, "wrong");
        var missing = sw.Elapsed;

        Assert.True(missing > wrong * 0.3, $"unknown {missing.TotalMilliseconds} ms vs wrong {wrong.TotalMilliseconds} ms");
    }
}
