using Controledu.Transport.Constants;
using Controledu.Discovery;
using Controledu.Teacher.Server.Options;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// UDP responder for student discovery probes.
/// </summary>
public sealed class UdpDiscoveryResponderService(
    IOptions<TeacherServerOptions> options,
    IServerIdentityService identityService,
    ILogger<UdpDiscoveryResponderService> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> LogListening =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(2001, nameof(LogListening)), "Discovery responder listening on UDP {Port}");

    private static readonly Action<ILogger, Exception?> LogPacketFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2002, nameof(LogPacketFailure)), "Failed to process discovery packet");

    private static readonly Action<ILogger, string, Exception?> LogMulticastJoinFailure =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2003, nameof(LogMulticastJoinFailure)), "Failed to join discovery multicast group {Group}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, options.Value.DiscoveryPort));
        TryJoinMulticastGroup(udp);

        LogListening(logger, options.Value.DiscoveryPort, null);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                var message = Encoding.UTF8.GetString(result.Buffer);
                if (!string.Equals(message, NetworkDefaults.DiscoveryProbe, StringComparison.Ordinal))
                {
                    continue;
                }

                var identity = await identityService.GetIdentityAsync(stoppingToken);
                var hostAddress = GetBestLocalAddressForPeer(result.RemoteEndPoint);
                var response = DiscoveryMessageFormatter.BuildResponse(hostAddress, options.Value.HttpPort, identity.ServerId, BuildDiscoveryServerName(identity.ServerName));
                var payload = Encoding.UTF8.GetBytes(response);
                await udp.SendAsync(payload, payload.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPacketFailure(logger, ex);
            }
        }
    }

    private static string GetBestLocalAddressForPeer(IPEndPoint peer)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(peer);
            if (probe.LocalEndPoint is IPEndPoint localEndPoint && !IPAddress.IsLoopback(localEndPoint.Address))
            {
                return localEndPoint.Address.ToString();
            }
        }
        catch
        {
            // Fallback below.
        }

        return GetFallbackLocalIPv4Address();
    }

    private static string GetFallbackLocalIPv4Address()
    {
        try
        {
            var address = Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(static x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));

            return address?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static string BuildDiscoveryServerName(string configuredName)
    {
        var machineName = Environment.MachineName.Trim();
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            return machineName;
        }

        var normalized = configuredName.Trim();
        if (normalized.Contains(machineName, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return $"{normalized} ({machineName})";
    }

    private void TryJoinMulticastGroup(UdpClient udp)
    {
        if (!IPAddress.TryParse(NetworkDefaults.DiscoveryMulticastGroup, out var multicast)
            || multicast.AddressFamily != AddressFamily.InterNetwork)
        {
            return;
        }

        try
        {
            udp.JoinMulticastGroup(multicast);
        }
        catch (Exception ex)
        {
            LogMulticastJoinFailure(logger, multicast.ToString(), ex);
        }
    }
}
