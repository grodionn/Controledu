using Controledu.Student.Host.Contracts;
using Controledu.Tests.Infrastructure;
using System.Net;

namespace Controledu.Tests;

public sealed class StudentLocalHostFactoryIntegrationTests : IAsyncLifetime
{
    private StudentLocalHostIntegrationHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await StudentLocalHostIntegrationHost.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task SessionEndpoint_ReturnsLocalToken()
    {
        using var client = _host.CreateClient(withAuthHeader: false);
        var session = await client.GetFromJsonAsync<SessionTokenResponse>("/api/session");

        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.Token));
    }

    [Fact]
    public async Task StatusEndpoint_RequiresLocalToken()
    {
        using var anonymousClient = _host.CreateClient(withAuthHeader: false);
        using var unauthorized = await anonymousClient.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var authorizedClient = _host.CreateClient(withAuthHeader: true);
        using var authorized = await authorizedClient.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }
}
