using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Controledu.Discovery.Services;

/// <summary>
/// UDP broadcast client for discovering teacher servers.
/// </summary>
public sealed class UdpDiscoveryClient(IOptions<DiscoveryOptions> options, ILogger<UdpDiscoveryClient> logger)
{
    private static readonly Action<ILogger, string, Exception?> LogProbeTarget =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1101, nameof(LogProbeTarget)), "Sending discovery probe to {Target}");

    private static readonly Action<ILogger, string, Exception?> LogInvalidDiscoveryPayload =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1102, nameof(LogInvalidDiscoveryPayload)), "Ignoring invalid discovery response payload: {Payload}");

    private static readonly Action<ILogger, string, int, Exception?> LogDiscoveryResponse =
        LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(1103, nameof(LogDiscoveryResponse)), "Received discovery response from {Host}:{Port}");

    private static readonly Action<ILogger, string, string, Exception?> LogDiscoveryCandidate =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1104, nameof(LogDiscoveryCandidate)), "Discovery candidate {ServerName} at {HostPort}");

    /// <summary>
    /// Sends discovery probe and returns collected servers.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredServerDto>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var probe = Encoding.UTF8.GetBytes(NetworkDefaults.DiscoveryProbe);
        var targets = BuildProbeTargets(options.Value.DiscoveryPort);
        var repeatCount = Math.Clamp(options.Value.ProbeRepeatCount, 1, 5);
        var repeatDelay = TimeSpan.FromMilliseconds(Math.Clamp(options.Value.ProbeRepeatIntervalMs, 20, 500));

        for (var attempt = 0; attempt < repeatCount; attempt++)
        {
            foreach (var target in targets)
            {
                LogProbeTarget(logger, target.ToString(), null);
                await udp.SendAsync(probe, probe.Length, target);
            }

            if (attempt + 1 < repeatCount)
            {
                await Task.Delay(repeatDelay, cancellationToken);
            }
        }

        var responses = new Dictionary<string, DiscoveredServerDto>(StringComparer.Ordinal);
        var timeout = TimeSpan.FromMilliseconds(Math.Max(300, options.Value.ProbeTimeoutMs));
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(remaining);

            try
            {
                var packet = await udp.ReceiveAsync(cts.Token);
                var payload = Encoding.UTF8.GetString(packet.Buffer);
                if (!DiscoveryMessageFormatter.TryParseResponse(payload, out var server) || server is null)
                {
                    LogInvalidDiscoveryPayload(logger, payload, null);
                    continue;
                }

                LogDiscoveryResponse(logger, server.Host, server.Port, null);
                responses[$"{server.ServerId}|{server.Host}|{server.Port}"] = server;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var localSubnets = GetLocalSubnets();
        var bestByServerId = new Dictionary<string, (DiscoveredServerDto Server, int Score)>(StringComparer.Ordinal);

        foreach (var server in responses.Values)
        {
            var groupingKey = string.IsNullOrWhiteSpace(server.ServerId)
                ? $"{server.Host}:{server.Port}"
                : server.ServerId;

            var score = ComputeEndpointScore(server, localSubnets);
            LogDiscoveryCandidate(logger, server.ServerName, $"{server.Host}:{server.Port}", null);

            if (!bestByServerId.TryGetValue(groupingKey, out var existing)
                || score > existing.Score
                || (score == existing.Score && string.Compare(server.Host, existing.Server.Host, StringComparison.OrdinalIgnoreCase) < 0))
            {
                bestByServerId[groupingKey] = (server, score);
            }
        }

        return bestByServerId
            .Values
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Server.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Server.Host, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Server)
            .ToArray();
    }

    private static List<IPEndPoint> BuildProbeTargets(int port)
    {
        var targets = new List<IPEndPoint>();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        AddTarget(IPAddress.Broadcast);
        if (IPAddress.TryParse(NetworkDefaults.DiscoveryMulticastGroup, out var multicast)
            && multicast.AddressFamily == AddressFamily.InterNetwork)
        {
            AddTarget(multicast);
        }

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var mask = unicast.IPv4Mask;
                if (mask is null)
                {
                    continue;
                }

                var ip = unicast.Address;
                if (IPAddress.IsLoopback(ip))
                {
                    continue;
                }

                if (ip.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                AddTarget(ComputeBroadcast(ip, mask));
            }
        }

        return targets;

        void AddTarget(IPAddress address)
        {
            var key = $"{address}:{port}";
            if (!dedupe.Add(key))
            {
                return;
            }

            targets.Add(new IPEndPoint(address, port));
        }
    }

    private static IPAddress ComputeBroadcast(IPAddress ipAddress, IPAddress subnetMask)
    {
        var ipBytes = ipAddress.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        if (ipBytes.Length != maskBytes.Length)
        {
            return IPAddress.Broadcast;
        }

        var broadcast = new byte[ipBytes.Length];
        for (var i = 0; i < broadcast.Length; i++)
        {
            broadcast[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
        }

        return new IPAddress(broadcast);
    }

    private static int ComputeEndpointScore(DiscoveredServerDto server, IReadOnlyList<LocalSubnet> localSubnets)
    {
        if (!IPAddress.TryParse(server.Host, out var ipAddress) || ipAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return -10;
        }

        var bytes = ipAddress.GetAddressBytes();
        var isLoopback = IPAddress.IsLoopback(ipAddress);
        var isLinkLocal = bytes[0] == 169 && bytes[1] == 254;
        var isPrivate = bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
        var sameSubnet = localSubnets.Any(subnet => subnet.Contains(ipAddress));

        var score = 0;
        if (sameSubnet)
        {
            score += 220;
        }

        if (isPrivate)
        {
            score += 80;
        }

        if (!isLinkLocal)
        {
            score += 20;
        }
        else
        {
            score -= 40;
        }

        if (isLoopback)
        {
            score -= 100;
        }

        return score;
    }

    private static List<LocalSubnet> GetLocalSubnets()
    {
        var result = new List<LocalSubnet>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                result.Add(new LocalSubnet(unicast.Address, unicast.IPv4Mask));
            }
        }

        return result;
    }

    private readonly record struct LocalSubnet(IPAddress Address, IPAddress Mask)
    {
        public bool Contains(IPAddress candidate)
        {
            var candidateBytes = candidate.GetAddressBytes();
            var addressBytes = Address.GetAddressBytes();
            var maskBytes = Mask.GetAddressBytes();
            if (candidateBytes.Length != addressBytes.Length || addressBytes.Length != maskBytes.Length)
            {
                return false;
            }

            for (var index = 0; index < candidateBytes.Length; index++)
            {
                if ((candidateBytes[index] & maskBytes[index]) != (addressBytes[index] & maskBytes[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
