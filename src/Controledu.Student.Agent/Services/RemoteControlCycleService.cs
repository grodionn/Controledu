using Controledu.Student.Agent.Models;
using Controledu.Storage.Stores;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Handles remote control session/input synchronization.
/// </summary>
public interface IRemoteControlCycleService
{
    /// <summary>
    /// Runs one remote-control processing pass.
    /// </summary>
    Task RunAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        CancellationToken cancellationToken);
}

internal sealed class RemoteControlCycleService(
    IRemoteControlService remoteControlService,
    StudentHubClient hubClient,
    ISettingsStore settingsStore) : IRemoteControlCycleService
{
    public Task RunAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        CancellationToken cancellationToken) =>
        remoteControlService.ProcessAsync(binding, deviceDisplayName, hubClient, settingsStore, cancellationToken);
}
