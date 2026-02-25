namespace Controledu.Transport.Dto;

/// <summary>
/// Discovery result for teacher server.
/// </summary>
public sealed record DiscoveredServerDto(
    string ServerId,
    string ServerName,
    string Host,
    int Port)
{
    /// <summary>
    /// Gets full base URL.
    /// </summary>
    public string BaseUrl => $"http://{Host}:{Port}";
}

