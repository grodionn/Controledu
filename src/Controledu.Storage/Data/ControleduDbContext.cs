using Controledu.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace Controledu.Storage.Data;

/// <summary>
/// Shared SQLite database context for Controledu apps.
/// </summary>
public sealed class ControleduDbContext(DbContextOptions<ControleduDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Generic key/value settings table.
    /// </summary>
    public DbSet<AppSettingEntity> Settings => Set<AppSettingEntity>();

    /// <summary>
    /// Student server binding records.
    /// </summary>
    public DbSet<StudentBindingEntity> StudentBindings => Set<StudentBindingEntity>();

    /// <summary>
    /// Paired clients accepted by teacher server.
    /// </summary>
    public DbSet<PairedClientEntity> PairedClients => Set<PairedClientEntity>();

    /// <summary>
    /// Teacher-side audit logs.
    /// </summary>
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    /// <summary>
    /// Student-side transfer resume states.
    /// </summary>
    public DbSet<TransferStateEntity> TransferStates => Set<TransferStateEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettingEntity>().HasKey(x => x.Key);

        modelBuilder.Entity<StudentBindingEntity>()
            .HasIndex(x => x.ClientId)
            .IsUnique();

        modelBuilder.Entity<PairedClientEntity>()
            .HasIndex(x => x.ClientId)
            .IsUnique();

        modelBuilder.Entity<AuditLogEntity>()
            .HasIndex(x => x.TimestampUtc);

        modelBuilder.Entity<TransferStateEntity>().HasKey(x => x.TransferId);
    }
}
