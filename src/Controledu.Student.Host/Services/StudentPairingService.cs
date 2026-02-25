using Controledu.Common.Security;
using Controledu.Discovery.Services;
using Controledu.Transport.Dto;
using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Net.Sockets;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Handles student discovery, pairing, and binding persistence.
/// </summary>
public interface IStudentPairingService
{
    /// <summary>
    /// Performs UDP discovery scan.
    /// </summary>
    Task<IReadOnlyList<DiscoveredServerDto>> DiscoverAsync(int? timeoutMs = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pairs student with teacher server and persists binding.
    /// </summary>
    Task<PairingResponseDto> PairAsync(string pin, string serverAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns stored binding or null.
    /// </summary>
    Task<StudentBindingModel?> GetBindingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears stored binding.
    /// </summary>
    Task ClearBindingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs health check for bound server.
    /// </summary>
    Task<bool> CheckServerOnlineAsync(StudentBindingModel binding, CancellationToken cancellationToken = default);
}

internal sealed class StudentPairingService(
    UdpDiscoveryClient discoveryClient,
    IStudentBindingStore bindingStore,
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector,
    ILogger<StudentPairingService> logger) : IStudentPairingService
{
    public async Task<IReadOnlyList<DiscoveredServerDto>> DiscoverAsync(int? timeoutMs = null, CancellationToken cancellationToken = default)
    {
        if (timeoutMs is null)
        {
            return await discoveryClient.DiscoverAsync(cancellationToken);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(300, timeoutMs.Value)));
        return await discoveryClient.DiscoverAsync(cts.Token);
    }

    public async Task<PairingResponseDto> PairAsync(string pin, string serverAddress, CancellationToken cancellationToken = default)
    {
        var existing = await bindingStore.GetAsync(cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException("Device is already connected. Disconnect first.");
        }

        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            throw new InvalidOperationException("Invalid PIN code.");
        }

        var baseUrl = NormalizeServerAddress(serverAddress);

        using var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        http.Timeout = TimeSpan.FromSeconds(10);

        var request = new PairingRequestDto(
            pin,
            Environment.MachineName,
            Environment.UserName,
            RuntimeInformation.OSDescription,
            GetLocalIpAddress());

        var response = await http.PostAsJsonAsync("api/pairing/complete", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Pairing failed ({(int)response.StatusCode})." : error);
        }

        var payload = await response.Content.ReadFromJsonAsync<PairingResponseDto>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Server returned empty pairing response.");

        var protectedToken = secretProtector.Protect(System.Text.Encoding.UTF8.GetBytes(payload.Token));
        var binding = new StudentBindingModel(
            payload.ServerId,
            payload.ServerName,
            payload.ServerBaseUrl,
            payload.ServerFingerprint,
            payload.ClientId,
            Convert.ToBase64String(protectedToken),
            DateTimeOffset.UtcNow);

        await bindingStore.SaveAsync(binding, cancellationToken);
        logger.LogInformation("Device paired with server {ServerName} ({ServerId})", payload.ServerName, payload.ServerId);

        return payload;
    }

    public Task<StudentBindingModel?> GetBindingAsync(CancellationToken cancellationToken = default) =>
        bindingStore.GetAsync(cancellationToken);

    public Task ClearBindingAsync(CancellationToken cancellationToken = default) =>
        bindingStore.ClearAsync(cancellationToken);

    public async Task<bool> CheckServerOnlineAsync(StudentBindingModel binding, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            var response = await http.GetAsync($"{binding.ServerBaseUrl.TrimEnd('/')}/api/server/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeServerAddress(string serverAddress)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
        {
            throw new InvalidOperationException("Server address is required.");
        }

        var trimmed = serverAddress.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"http://{trimmed}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Invalid server address.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
