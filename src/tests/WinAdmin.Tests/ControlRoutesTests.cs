using System.Net;

namespace WinAdmin.Tests;

/// <summary>
/// POST /services/{name}/{операция} и /printers/{name}/{операция}: параметр маршрута не должен
/// называться «action» — в MVC это зарезервированное значение (имя метода), и маршрут не совпадал (405).
/// </summary>
[Collection("network-api")]
public sealed class ControlRoutesTests(NetworkApiFactory factory)
{
    [Theory]
    [InlineData("/api/v1/services/NoSuchService123/frobnicate")]
    [InlineData("/api/v1/printers/NoSuchPrinter123/frobnicate")]
    public async Task Control_routes_reach_the_action(string url)
    {
        var admin = await factory.ClientAsAdministratorAsync();
        var response = await admin.PostAsync(url, null);
        // Неизвестная операция отклоняется проверкой в действии — значит, маршрут совпал.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
