using Controledu.Storage.Data;
using Controledu.Storage.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Controledu.Storage.Extensions;

/// <summary>
/// Storage DI registration helpers.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQLite-backed storage services.
    /// </summary>
    public static IServiceCollection AddControleduStorage(this IServiceCollection services, string sqlitePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlitePath);

        var directory = Path.GetDirectoryName(sqlitePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContextFactory<ControleduDbContext>(options =>
            options.UseSqlite($"Data Source={sqlitePath}"));

        services.AddSingleton<ISettingsStore, EfSettingsStore>();
        services.AddSingleton<IStudentBindingStore, EfStudentBindingStore>();
        services.AddSingleton<IPairedClientStore, EfPairedClientStore>();
        services.AddSingleton<IAuditLogStore, EfAuditLogStore>();
        services.AddSingleton<ITransferStateStore, EfTransferStateStore>();
        services.AddSingleton<IStorageInitializer, StorageInitializer>();

        return services;
    }
}
