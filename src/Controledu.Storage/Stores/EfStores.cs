using Controledu.Storage.Data;
using Controledu.Storage.Entities;
using Controledu.Storage.Models;
using Microsoft.EntityFrameworkCore;

namespace Controledu.Storage.Stores;

internal sealed class EfSettingsStore(IDbContextFactory<ControleduDbContext> contextFactory) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        return row?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Settings.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (row is null)
        {
            db.Settings.Add(new AppSettingEntity { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class EfStudentBindingStore(IDbContextFactory<ControleduDbContext> contextFactory) : IStudentBindingStore
{
    public async Task<StudentBindingModel?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.StudentBindings
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new StudentBindingModel(
                row.ServerId,
                row.ServerName,
                row.ServerBaseUrl,
                row.ServerFingerprint,
                row.ClientId,
                row.ProtectedTokenBase64,
                row.UpdatedAtUtc);
    }

    public async Task SaveAsync(StudentBindingModel model, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.StudentBindings.FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            row = new StudentBindingEntity
            {
                ServerId = model.ServerId,
                ServerName = model.ServerName,
                ServerBaseUrl = model.ServerBaseUrl,
                ServerFingerprint = model.ServerFingerprint,
                ClientId = model.ClientId,
                ProtectedTokenBase64 = model.ProtectedTokenBase64,
                UpdatedAtUtc = model.UpdatedAtUtc,
            };
            db.StudentBindings.Add(row);
        }
        else
        {
            row.ServerId = model.ServerId;
            row.ServerName = model.ServerName;
            row.ServerBaseUrl = model.ServerBaseUrl;
            row.ServerFingerprint = model.ServerFingerprint;
            row.ClientId = model.ClientId;
            row.ProtectedTokenBase64 = model.ProtectedTokenBase64;
            row.UpdatedAtUtc = model.UpdatedAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.StudentBindings.RemoveRange(db.StudentBindings);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class EfPairedClientStore(IDbContextFactory<ControleduDbContext> contextFactory) : IPairedClientStore
{
    public async Task UpsertAsync(PairedClientModel model, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PairedClients.FirstOrDefaultAsync(x => x.ClientId == model.ClientId, cancellationToken);

        if (entity is null)
        {
            entity = new PairedClientEntity
            {
                ClientId = model.ClientId,
                Token = model.Token,
                HostName = model.HostName,
                UserName = model.UserName,
                OsDescription = model.OsDescription,
                LocalIpAddress = model.LocalIpAddress,
                CreatedAtUtc = model.CreatedAtUtc,
                TokenExpiresAtUtc = model.TokenExpiresAtUtc,
            };
            db.PairedClients.Add(entity);
        }
        else
        {
            entity.Token = model.Token;
            entity.HostName = model.HostName;
            entity.UserName = model.UserName;
            entity.OsDescription = model.OsDescription;
            entity.LocalIpAddress = model.LocalIpAddress;
            entity.TokenExpiresAtUtc = model.TokenExpiresAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PairedClientModel?> FindAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PairedClients.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
        return row is null
            ? null
            : new PairedClientModel(row.ClientId, row.Token, row.HostName, row.UserName, row.OsDescription, row.LocalIpAddress, row.CreatedAtUtc, row.TokenExpiresAtUtc);
    }

    public async Task<bool> ValidateTokenAsync(string clientId, string token, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PairedClients.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
        return row is not null && row.TokenExpiresAtUtc > DateTimeOffset.UtcNow && string.Equals(row.Token, token, StringComparison.Ordinal);
    }

    public async Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PairedClients.FirstOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
        if (row is null)
        {
            return false;
        }

        db.PairedClients.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed class EfAuditLogStore(IDbContextFactory<ControleduDbContext> contextFactory) : IAuditLogStore
{
    public async Task AppendAsync(string action, string actor, string details, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.AuditLogs.Add(new AuditLogEntity
        {
            Action = action,
            Actor = actor,
            Details = details,
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogModel>> GetLatestAsync(int take, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new AuditLogModel(x.Id, x.TimestampUtc, x.Action, x.Actor, x.Details))
            .ToArray();
    }
}

internal sealed class EfTransferStateStore(IDbContextFactory<ControleduDbContext> contextFactory) : ITransferStateStore
{
    public async Task<TransferStateModel?> GetAsync(string transferId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.TransferStates.AsNoTracking().FirstOrDefaultAsync(x => x.TransferId == transferId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var completed = System.Text.Json.JsonSerializer.Deserialize<List<int>>(row.CompletedChunkIndexesJson) ?? [];

        return new TransferStateModel(
            row.TransferId,
            row.FileName,
            row.Sha256,
            row.ChunkSize,
            row.TotalChunks,
            completed,
            row.PartialFilePath,
            row.UpdatedAtUtc);
    }

    public async Task SaveAsync(TransferStateModel model, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.TransferStates.FirstOrDefaultAsync(x => x.TransferId == model.TransferId, cancellationToken);
        var json = System.Text.Json.JsonSerializer.Serialize(model.CompletedChunkIndexes.OrderBy(x => x));

        if (row is null)
        {
            row = new TransferStateEntity
            {
                TransferId = model.TransferId,
                FileName = model.FileName,
                Sha256 = model.Sha256,
                ChunkSize = model.ChunkSize,
                TotalChunks = model.TotalChunks,
                CompletedChunkIndexesJson = json,
                PartialFilePath = model.PartialFilePath,
                UpdatedAtUtc = model.UpdatedAtUtc,
            };
            db.TransferStates.Add(row);
        }
        else
        {
            row.FileName = model.FileName;
            row.Sha256 = model.Sha256;
            row.ChunkSize = model.ChunkSize;
            row.TotalChunks = model.TotalChunks;
            row.CompletedChunkIndexesJson = json;
            row.PartialFilePath = model.PartialFilePath;
            row.UpdatedAtUtc = model.UpdatedAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string transferId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.TransferStates.FirstOrDefaultAsync(x => x.TransferId == transferId, cancellationToken);
        if (row is null)
        {
            return;
        }

        db.TransferStates.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class StorageInitializer(IDbContextFactory<ControleduDbContext> contextFactory) : IStorageInitializer
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
