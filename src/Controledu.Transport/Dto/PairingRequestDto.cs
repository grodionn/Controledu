namespace Controledu.Transport.Dto;

/// <summary>
/// Pairing request submitted by student.
/// </summary>
public sealed record PairingRequestDto(
    string PinCode,
    string HostName,
    string UserName,
    string OsDescription,
    string? LocalIpAddress);

