using Controledu.Common.IO;
using Controledu.Teacher.Server.Services;
using Controledu.Tests.Infrastructure;
using Controledu.Transport.Dto;
using Microsoft.Extensions.DependencyInjection;

namespace Controledu.Tests;

public sealed class FileTransferCoordinatorIntegrationTests : IAsyncLifetime
{
    private TeacherServerIntegrationHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await TeacherServerIntegrationHost.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task SaveChunkAsync_InParallel_AllChunksAvailableForDispatch()
    {
        using var scope = _host.Services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IFileTransferCoordinator>();

        const int chunkSize = 4096;
        var chunkPayloads = new Dictionary<int, byte[]>
        {
            [0] = RandomBytes(chunkSize),
            [1] = RandomBytes(chunkSize),
            [2] = RandomBytes(chunkSize),
        };

        var init = await coordinator.InitializeUploadAsync(new FileUploadInitRequest(
            FileName: "lesson.pdf",
            FileSize: chunkPayloads.Values.Sum(static x => x.Length),
            Sha256: "unused-for-chunk-tests",
            ChunkSize: chunkSize,
            UploadedBy: "teacher"));

        var uploadTasks = chunkPayloads.Select(async pair =>
        {
            var result = await coordinator.SaveChunkAsync(
                init.TransferId,
                pair.Key,
                pair.Value,
                HashingUtility.Sha256Hex(pair.Value));

            Assert.True(result.Accepted);
        });

        await Task.WhenAll(uploadTasks);

        var missing = await coordinator.GetMissingChunksAsync(init.TransferId, [0]);
        Assert.Equal([1, 2], missing.MissingChunkIndexes.OrderBy(static x => x).ToArray());

        var dispatch = await coordinator.CreateDispatchCommandAsync(init.TransferId, ["student-001", "student-002"]);
        Assert.Equal(3, dispatch.TotalChunks);

        var downloadedChunk = await coordinator.GetChunkAsync(init.TransferId, 1);
        Assert.Equal(chunkPayloads[1], downloadedChunk.Data);
        Assert.Equal(HashingUtility.Sha256Hex(chunkPayloads[1]), downloadedChunk.Sha256);
    }

    private static byte[] RandomBytes(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
