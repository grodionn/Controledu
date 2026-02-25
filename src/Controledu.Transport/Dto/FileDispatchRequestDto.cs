namespace Controledu.Transport.Dto;

/// <summary>
/// Dispatch request to send uploaded file to students.
/// </summary>
public sealed record FileDispatchRequestDto(
    string TransferId,
    IReadOnlyList<string> TargetClientIds);

