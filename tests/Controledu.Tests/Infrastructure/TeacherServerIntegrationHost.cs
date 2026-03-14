using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server;
using Controledu.Teacher.Server.Controllers;
using Controledu.Teacher.Server.Security;
using Controledu.Teacher.Server.Services;
using Controledu.Transport.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Controledu.Tests.Infrastructure;

internal sealed class TeacherServerIntegrationHost : IAsyncDisposable
{
    private readonly string _tempRoot;
    private readonly WebApplication _app;

    private TeacherServerIntegrationHost(string tempRoot, WebApplication app, HttpClient client, string teacherToken)
    {
        _tempRoot = tempRoot;
        _app = app;
        Client = client;
        TeacherToken = teacherToken;
    }

    public HttpClient Client { get; }

    public string TeacherToken { get; }

    public IServiceProvider Services => _app.Services;

    public TestServer TestServer => _app.GetTestServer();

    public static async Task<TeacherServerIntegrationHost> CreateAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "controledu-tests", "teacher-server", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var storagePath = Path.Combine(tempRoot, "teacher-server.db");
        var transferRoot = Path.Combine(tempRoot, "transfers");

        var args = new[]
        {
            $"--TeacherServer:StorageFile={storagePath}",
            $"--TeacherServer:TransferRoot={transferRoot}",
            $"--TeacherServer:HttpPort={TestPortAllocator.GetFreeTcpPort()}",
            "--TeacherServer:DiscoveryPort=40555",
            "--TeacherServer:PairingPinLifetimeSeconds=60",
            "--TeacherServer:PairingTokenLifetimeHours=720",
        };

        var app = TeacherServerHostFactory.Build(args, static builder =>
        {
            builder.WebHost.UseTestServer();
        });

        await app.StartAsync();

        var client = app.GetTestClient();
        client.BaseAddress ??= new Uri("http://localhost");

        var session = await client.GetFromJsonAsync<TeacherSessionResponse>("/api/session");
        var teacherToken = session?.Token ?? throw new InvalidOperationException("Teacher session token was not issued.");
        client.DefaultRequestHeaders.Add(TeacherAuthDefaults.TokenHeaderName, teacherToken);

        return new TeacherServerIntegrationHost(tempRoot, app, client, teacherToken);
    }

    public HttpClient CreateAuthorizedClient()
    {
        var client = _app.GetTestClient();
        client.BaseAddress ??= new Uri("http://localhost");
        client.DefaultRequestHeaders.Add(TeacherAuthDefaults.TokenHeaderName, TeacherToken);
        return client;
    }

    public async Task SeedOnlineStudentAsync(string clientId, string token, string connectionId)
    {
        using var scope = Services.CreateScope();
        var pairedClientStore = scope.ServiceProvider.GetRequiredService<IPairedClientStore>();
        var studentRegistry = scope.ServiceProvider.GetRequiredService<IStudentRegistry>();

        await pairedClientStore.UpsertAsync(new PairedClientModel(
            ClientId: clientId,
            Token: token,
            HostName: "Student-PC",
            UserName: "student",
            OsDescription: "Windows",
            LocalIpAddress: "127.0.0.1",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TokenExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1)));

        _ = studentRegistry.Upsert(
            connectionId,
            new StudentRegistrationDto(
                clientId,
                token,
                "Student-PC",
                "student",
                "Windows",
                "127.0.0.1"),
            DateTimeOffset.UtcNow);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        await _app.StopAsync();
        await _app.DisposeAsync();

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in temporary test folders.
        }
    }
}
