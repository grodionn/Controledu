using Microsoft.Extensions.Logging;
using Controledu.Transport.Constants;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Controledu.Discovery.Services;

/// <summary>
/// Hosted UDP responder for discovery probes.
/// </summary>
public sealed class UdpDiscoveryResponderService(
    IDiscoveryAnnouncementProvider announcementProvider,
    IOptions<DiscoveryOptions> options,
    ILogger<UdpDiscoveryResponderService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogPacketFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1001, nameof(LogPacketFailure)), "Failed processing discovery UDP packet");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, options.Value.DiscoveryPort));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var packet = await udp.ReceiveAsync(stoppingToken);
                var probe = Encoding.UTF8.GetString(packet.Buffer);
                if (!string.Equals(probe, NetworkDefaults.DiscoveryProbe, StringComparison.Ordinal))
                {
                    continue;
                }

                var announcement = await announcementProvider.GetAnnouncementAsync(stoppingToken);
                var responsePayload = DiscoveryMessageFormatter.BuildResponse(
                    announcement.Host,
                    announcement.Port,
                    announcement.ServerId,
                    announcement.ServerName);

                var data = Encoding.UTF8.GetBytes(responsePayload);
                await udp.SendAsync(data, data.Length, packet.RemoteEndPoint);
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
}
