using Controledu.Student.Host;
using Controledu.Student.Host.Contracts;
using Microsoft.AspNetCore.Builder;

namespace Controledu.Tests.Infrastructure;

internal sealed class StudentLocalHostIntegrationHost : IAsyncDisposable
{
    private readonly string _tempRoot;
    private readonly WebApplication _app;
    private readonly Uri _baseAddress;

    private StudentLocalHostIntegrationHost(string tempRoot, WebApplication app, Uri baseAddress, string token)
    {
        _tempRoot = tempRoot;
        _app = app;
        _baseAddress = baseAddress;
        Token = token;
    }

    public string Token { get; }

    public static async Task<StudentLocalHostIntegrationHost> CreateAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "controledu-tests", "student-host", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var storagePath = Path.Combine(tempRoot, "student-shared.db");
        var localPort = TestPortAllocator.GetFreeTcpPort();
        var baseAddress = new Uri($"http://127.0.0.1:{localPort}/");

        var args = new[]
        {
            $"--StudentHost:LocalPort={localPort}",
            $"--StudentHost:StorageFile={storagePath}",
            "--StudentHost:DiscoveryPort=40555",
            "--StudentHost:DiscoveryTimeoutMs=500",
            $"--StudentHost:AgentExecutablePath={Path.Combine(tempRoot, "fake-agent.exe")}",
            "--StudentHost:WindowTitle=Controledu Student Test Host",
            "--StudentHost:HandRaiseCooldownSeconds=20",
            "--AutoUpdate:Enabled=false",
        };

        var app = StudentLocalHostFactory.Build(args);
        await app.StartAsync();

        using var bootstrapClient = new HttpClient { BaseAddress = baseAddress };
        var session = await bootstrapClient.GetFromJsonAsync<SessionTokenResponse>("/api/session");
        var token = session?.Token ?? throw new InvalidOperationException("Local session token was not issued.");

        return new StudentLocalHostIntegrationHost(tempRoot, app, baseAddress, token);
    }

    public HttpClient CreateClient(bool withAuthHeader)
    {
        var client = new HttpClient { BaseAddress = _baseAddress };
        if (withAuthHeader)
        {
            client.DefaultRequestHeaders.Add("X-Controledu-LocalToken", Token);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
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
