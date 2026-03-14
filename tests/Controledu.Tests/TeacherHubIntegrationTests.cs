using Controledu.Tests.Infrastructure;
using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Microsoft.AspNetCore.SignalR.Client;

namespace Controledu.Tests;

public sealed class TeacherHubIntegrationTests : IAsyncLifetime
{
    private TeacherServerIntegrationHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await TeacherServerIntegrationHost.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task GeneratePairingPin_WithValidToken_ReturnsPin()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_host.Client.BaseAddress!, HubRoutes.TeacherHub), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_host.TeacherToken)!;
                options.HttpMessageHandlerFactory = _ => _host.TestServer.CreateHandler();
            })
            .Build();

        try
        {
            await connection.StartAsync();

            var pin = await connection.InvokeAsync<PairingPinDto>("GeneratePairingPin");

            Assert.NotNull(pin);
            Assert.Matches("^[0-9]{6}$", pin.PinCode);
            Assert.True(pin.ExpiresAtUtc > DateTimeOffset.UtcNow);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
