using Controledu.Storage.Models;

namespace Controledu.Storage.Stores;

/// <summary>
/// Key/value settings store.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Gets setting value or null.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes setting value.
    /// </summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

/// <summary>
/// Student binding persistence store.
/// </summary>
public interface IStudentBindingStore
{
    /// <summary>
    /// Returns current binding or null.
    /// </summary>
    Task<StudentBindingModel?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates binding.
    /// </summary>
    Task SaveAsync(StudentBindingModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears existing binding.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Teacher-side paired clients store.
/// </summary>
public interface IPairedClientStore
{
    /// <summary>
    /// Adds or updates paired client.
    /// </summary>
    Task UpsertAsync(PairedClientModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up paired client by id.
    /// </summary>
    Task<PairedClientModel?> FindAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates token for client id.
    /// </summary>
    Task<bool> ValidateTokenAsync(string clientId, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes paired client by id.
    /// </summary>
    Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Audit log store.
/// </summary>
public interface IAuditLogStore
{
    /// <summary>
    /// Appends log row.
    /// </summary>
    Task AppendAsync(string action, string actor, string details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns latest rows.
    /// </summary>
    Task<IReadOnlyList<AuditLogModel>> GetLatestAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transfer resume state store.
/// </summary>
public interface ITransferStateStore
{
    /// <summary>
    /// Gets transfer state or null.
    /// </summary>
    Task<TransferStateModel?> GetAsync(string transferId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts transfer state.
    /// </summary>
    Task SaveAsync(TransferStateModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes transfer state.
    /// </summary>
    Task DeleteAsync(string transferId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Initializes storage schema.
/// </summary>
public interface IStorageInitializer
{
    /// <summary>
    /// Ensures database is created.
    /// </summary>
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
