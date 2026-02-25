using Controledu.Transport.Dto;
using System.Globalization;

namespace Controledu.Discovery;

/// <summary>
/// UDP discovery response parser and formatter.
/// </summary>
public static class DiscoveryMessageFormatter
{
    private const string Prefix = "CONTROLEDU_HERE";

    /// <summary>
    /// Builds a discovery response payload.
    /// </summary>
    public static string BuildResponse(string host, int port, string serverId, string serverName) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix} {host}:{port} {serverId} {serverName}");

    /// <summary>
    /// Parses a discovery response payload.
    /// </summary>
    public static bool TryParseResponse(string payload, out DiscoveredServerDto? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hostPort = parts[1].Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        if (hostPort.Length != 2 || !int.TryParse(hostPort[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            return false;
        }

        result = new DiscoveredServerDto(parts[2], parts[3], hostPort[0], port);
        return true;
    }
}
