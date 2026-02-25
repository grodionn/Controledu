using Controledu.Transport.Dto;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Handles one-time pairing PIN lifecycle.
/// </summary>
public interface IPairingCodeService
{
    /// <summary>
    /// Generates a PIN code.
    /// </summary>
    PairingPinDto Generate();

    /// <summary>
    /// Validates and consumes a PIN code.
    /// </summary>
    bool TryConsume(string pinCode);

    /// <summary>
    /// Validates PIN without consuming.
    /// </summary>
    bool IsValid(string pinCode);
}

