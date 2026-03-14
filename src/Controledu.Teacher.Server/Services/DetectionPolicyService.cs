using Controledu.Transport.Dto;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Detection policy persistence and runtime access.
/// </summary>
public interface IDetectionPolicyService
{
    /// <summary>
    /// Returns current detection policy.
    /// </summary>
    Task<DetectionPolicyDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new detection policy.
    /// </summary>
    Task<DetectionPolicyDto> SaveAsync(DetectionPolicyDto policy, string actor, CancellationToken cancellationToken = default);
}

internal sealed class DetectionPolicyService : IDetectionPolicyService
{
    public Task<DetectionPolicyDto> GetAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(DetectionPolicyFactory.CreateProductionPolicy(enabled: true));
    }

    public Task<DetectionPolicyDto> SaveAsync(DetectionPolicyDto policy, string actor, CancellationToken cancellationToken = default)
    {
        _ = policy;
        _ = actor;
        _ = cancellationToken;
        throw new InvalidOperationException("Detection policy is locked in production mode.");
    }
}
