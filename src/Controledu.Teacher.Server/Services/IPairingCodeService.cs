using Controledu.Transport.Dto;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Handles pairing PIN lifecycle.
/// </summary>
public interface IPairingCodeService
{
    /// <summary>
    /// Generates a PIN code.
    /// </summary>
    PairingPinDto Generate();

    /// <summary>
    /// Validates that a PIN code can still be used for pairing.
    /// </summary>
    bool TryUse(string pinCode);

    /// <summary>
    /// Validates PIN without consuming.
    /// </summary>
    bool IsValid(string pinCode);
}

