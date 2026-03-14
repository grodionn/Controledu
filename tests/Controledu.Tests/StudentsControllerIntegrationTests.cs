using Controledu.Tests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Controledu.Tests;

public sealed class StudentsControllerIntegrationTests : IAsyncLifetime
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
    public async Task SendTeacherChatMessage_WithoutTeacherToken_ReturnsUnauthorized()
    {
        using var client = _host.TestServer.CreateClient();
        client.BaseAddress ??= new Uri("http://localhost");

        using var response = await client.PostAsJsonAsync("/api/students/student-001/chat", new { text = "Hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SendTeacherChatMessage_WithOnlineStudent_ReturnsOk()
    {
        const string clientId = "student-001";
        await _host.SeedOnlineStudentAsync(clientId, "token-001", "connection-001");

        using var client = _host.CreateAuthorizedClient();
        using var response = await client.PostAsJsonAsync($"/api/students/{clientId}/chat", new { text = "Teacher message" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(payload.GetProperty("ok").GetBoolean());
        Assert.Equal("Teacher message", payload.GetProperty("chat").GetProperty("text").GetString());
    }
}
