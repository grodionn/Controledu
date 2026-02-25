using Controledu.Common.IO;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Models;
using Controledu.Teacher.Server.Options;
using Microsoft.Extensions.Options;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Resolves and persists stable teacher server identity.
/// </summary>
public interface IServerIdentityService
{
    /// <summary>
    /// Returns resolved server identity.
    /// </summary>
    Task<ServerIdentity> GetIdentityAsync(CancellationToken cancellationToken = default);
}

internal sealed class ServerIdentityService(ISettingsStore settingsStore, IOptions<TeacherServerOptions> options) : IServerIdentityService
{
    private const string ServerIdKey = "teacher.server.id";

    public async Task<ServerIdentity> GetIdentityAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.Value.ServerId;
        var existing = await settingsStore.GetAsync(ServerIdKey, cancellationToken);

        var serverId = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : !string.IsNullOrWhiteSpace(existing)
                ? existing
                : Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(existing) || !string.Equals(existing, serverId, StringComparison.Ordinal))
        {
            await settingsStore.SetAsync(ServerIdKey, serverId, cancellationToken);
        }

        var fingerprint = HashingUtility.Sha256Hex(serverId);
        return new ServerIdentity(serverId, ResolveServerDisplayName(options.Value.ServerName), fingerprint);
    }

    private static string ResolveServerDisplayName(string configuredName)
    {
        var machineName = Environment.MachineName.Trim();
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            return machineName;
        }

        var trimmed = configuredName.Trim();
        if (trimmed.Contains(machineName, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed} ({machineName})";
    }
}
