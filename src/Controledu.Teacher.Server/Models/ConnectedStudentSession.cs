using Controledu.Transport.Dto;

namespace Controledu.Teacher.Server.Models;

internal sealed class ConnectedStudentSession
{
    public required string ClientId { get; init; }

    public required string ConnectionId { get; set; }

    public required string HostName { get; set; }

    public required string UserName { get; set; }

    public string? LocalIpAddress { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public bool IsOnline { get; set; }

    public bool DetectionEnabled { get; set; } = true;

    public DateTimeOffset? LastDetectionAtUtc { get; set; }

    public string? LastDetectionClass { get; set; }

    public double? LastDetectionConfidence { get; set; }

    public int AlertCount { get; set; }

    public StudentInfoDto ToDto() =>
        new(ClientId, HostName, UserName, LocalIpAddress, LastSeenUtc, IsOnline, DetectionEnabled, LastDetectionAtUtc, LastDetectionClass, LastDetectionConfidence, AlertCount);
}

