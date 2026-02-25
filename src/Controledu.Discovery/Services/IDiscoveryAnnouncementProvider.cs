using Controledu.Discovery.Models;

namespace Controledu.Discovery.Services;

/// <summary>
/// Source of dynamic discovery announcement details.
/// </summary>
public interface IDiscoveryAnnouncementProvider
{
    /// <summary>
    /// Builds announcement payload.
    /// </summary>
    Task<DiscoveryAnnouncement> GetAnnouncementAsync(CancellationToken cancellationToken = default);
}
