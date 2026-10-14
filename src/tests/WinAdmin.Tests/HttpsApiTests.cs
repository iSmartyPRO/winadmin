using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class HttpsApiTests(NetworkApiFactory factory)
{
    private string NetworkFile => Path.Combine(factory.DataDir, "network.json");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Get_includes_https_section()
    {
        var client = await factory.ClientAsAdministratorAsync();
        var dto = await client.GetFromJsonAsync<NetworkSettingsDto>("/api/v1/settings/network", JsonOpts);
        Assert.NotNull(dto!.Https);
    }

    [Fact]
    public async Task Certificates_require_network_permission_and_list_only_those_with_key()
    {
        var withKey = TestCertificates.Valid("list-" + Guid.NewGuid().ToString("N")[..6] + ".test.local");
        var noKey = TestCertificates.WithoutKey(TestCertificates.Valid("nokey.test.local"));
        factory.Certificates.Items.Add((CertificateStoreLocation.LocalMachine, withKey));
        factory.Certificates.Items.Add((CertificateStoreLocation.LocalMachine, noKey));

        var reader = await factory.ClientWithPermissionsAsync(PermissionIds.SystemRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/v1/settings/network/certificates")).StatusCode);

        var client = await factory.ClientAsAdministratorAsync();
        var list = await client.GetFromJsonAsync<List<CertificateInfo>>("/api/v1/settings/network/certificates", JsonOpts);
        var item = Assert.Single(list!, c => c.Thumbprint == withKey.Thumbprint);
        Assert.Equal(CertificateStoreLocation.LocalMachine, item.StoreLocation);
        Assert.DoesNotContain(list!, c => c.Thumbprint == noKey.Thumbprint);
    }

    [Fact]
    public async Task Unknown_certificate_returns_400_in_russian_and_keeps_file()
    {
        var client = await factory.ClientAsAdministratorAsync();
        var current = (await client.GetFromJsonAsync<NetworkSettingsDto>("/api/v1/settings/network", JsonOpts))!;
        string before = File.ReadAllText(NetworkFile);

        var response = await client.PutAsJsonAsync("/api/v1/settings/network", new
        {
            mode = current.Mode.ToString(), port = current.Port, allow = current.Allow,
            https = new { enabled = true, source = "Store", thumbprint = new string('C', 40), storeLocation = "LocalMachine" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("не найден", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, File.ReadAllText(NetworkFile));
    }

    [Fact]
    public async Task Pfx_password_is_stored_encrypted_and_never_returned()
    {
        var client = await factory.ClientAsAdministratorAsync();
        var current = (await client.GetFromJsonAsync<NetworkSettingsDto>("/api/v1/settings/network", JsonOpts))!;
        using var cert = TestCertificates.Valid("pfx.test.local");
        string pfx = Path.Combine(factory.DataDir, "panel-" + Guid.NewGuid().ToString("N") + ".pfx");
        const string password = "Pfx-Secret-42";
        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, password));

        var response = await client.PutAsJsonAsync("/api/v1/settings/network", new
        {
            mode = "Local", port = current.Port, allow = current.Allow,
            https = new { enabled = true, source = "Pfx", pfxPath = pfx, pfxPassword = password },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NetworkUpdateResult>(JsonOpts);
        Assert.Equal($"https://127.0.0.1:{current.Port}", result!.Url);

        var applied = await WaitForAuditAsync("https");
        Assert.True(applied?.Success, applied?.Details);
        string json = File.ReadAllText(NetworkFile);
        Assert.DoesNotContain(password, json);
        Assert.Contains("enc:v1:", json);
        Assert.DoesNotContain(password, applied!.Details);

        string raw = await client.GetStringAsync("/api/v1/settings/network");
        Assert.DoesNotContain("enc:v1", raw);
        Assert.DoesNotContain(password, raw);
        var dto = JsonSerializer.Deserialize<NetworkSettingsDto>(raw, JsonOpts)!;
        Assert.True(dto.Https!.Enabled);
        Assert.True(dto.Https.PfxPasswordSet);
        Assert.Equal(cert.Thumbprint, dto.Https.Certificate!.Thumbprint);
        Assert.Null(dto.Https.CertificateError);
        Assert.StartsWith("https://", dto.Url);

        // Вернуть HTTP, чтобы не влиять на другие тесты коллекции.
        await client.PutAsJsonAsync("/api/v1/settings/network", new
        {
            mode = current.Mode.ToString(), port = current.Port, allow = current.Allow, https = new { enabled = false },
        });
        var reset = await WaitForAuditAsync("→ local:" + current.Port, mustNotContain: "https");
        Assert.True(reset?.Success, reset?.Details);
    }

    [Fact]
    public async Task Self_signed_is_generated_with_machine_names_and_extra_dns()
    {
        var client = await factory.ClientAsAdministratorAsync();
        var response = await client.PostAsJsonAsync("/api/v1/settings/network/https/self-signed",
            new { dnsNames = new[] { "panel.test.local" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<CertificateInfo>(JsonOpts);
        Assert.Contains("localhost", info!.DnsNames);
        Assert.Contains("panel.test.local", info.DnsNames);
        Assert.True(info.NotAfter > DateTimeOffset.UtcNow.AddYears(1));
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "keys", "https.pfx")));

        var bad = await client.PostAsJsonAsync("/api/v1/settings/network/https/self-signed", new { dnsNames = new[] { "bad name" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("DNS", await bad.Content.ReadAsStringAsync());
    }

    private async Task<AuditEntryDto?> WaitForAuditAsync(string detail, string? mustNotContain = null)
    {
        for (int i = 0; i < 150; i++)
        {
            using var scope = factory.Services.CreateScope();
            var audit = await scope.ServiceProvider.GetRequiredService<IAuditService>().QueryAsync();
            var entry = audit.FirstOrDefault(a => a.Action == "settings.network" && a.Details?.Contains(detail) == true
                && (mustNotContain is null || !a.Details[a.Details.IndexOf('→')..].Contains(mustNotContain)));
            if (entry is not null) return entry;
            await Task.Delay(100);
        }
        return null;
    }
}
