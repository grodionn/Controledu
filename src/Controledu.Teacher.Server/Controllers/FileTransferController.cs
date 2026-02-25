using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Hubs;
using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Chunked file transfer API.
/// </summary>
[ApiController]
[Route("api/files")]
public sealed class FileTransferController(
    IFileTransferCoordinator transferCoordinator,
    IStudentRegistry studentRegistry,
    IPairedClientStore pairedClientStore,
    IHubContext<StudentHub> studentHubContext,
    IHubContext<TeacherHub> teacherHubContext,
    IAuditService auditService,
    ILogger<FileTransferController> logger) : ControllerBase
{
    private static readonly Action<ILogger, string, int, Exception?> LogDispatchedTransfer =
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(4001, nameof(LogDispatchedTransfer)), "Dispatched transfer {TransferId} to {Count} clients");

    /// <summary>
    /// Initializes upload.
    /// </summary>
    [HttpPost("upload/init")]
    [ProducesResponseType<FileUploadInitResponse>(StatusCodes.Status200OK)]
    public Task<FileUploadInitResponse> InitUpload([FromBody] FileUploadInitRequest request, CancellationToken cancellationToken) =>
        transferCoordinator.InitializeUploadAsync(request, cancellationToken);

    /// <summary>
    /// Uploads a transfer chunk.
    /// </summary>
    [HttpPut("upload/{transferId}/chunk/{chunkIndex:int}")]
    [ProducesResponseType<FileChunkUploadResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FileChunkUploadResult>> UploadChunk(string transferId, int chunkIndex, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory, cancellationToken);

        var chunkData = memory.ToArray();
        var expectedHash = Request.Headers["X-Chunk-Sha256"].FirstOrDefault();
        var result = await transferCoordinator.SaveChunkAsync(transferId, chunkIndex, chunkData, expectedHash, cancellationToken);
        return result.Accepted ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Dispatches uploaded file to selected student clients.
    /// </summary>
    [HttpPost("{transferId}/dispatch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Dispatch(string transferId, [FromBody] FileDispatchRequestDto request, CancellationToken cancellationToken)
    {
        if (!string.Equals(transferId, request.TransferId, StringComparison.Ordinal))
        {
            return BadRequest("Transfer id mismatch.");
        }

        var command = await transferCoordinator.CreateDispatchCommandAsync(transferId, request.TargetClientIds, cancellationToken);

        var delivered = new List<string>();
        foreach (var clientId in request.TargetClientIds.Distinct(StringComparer.Ordinal))
        {
            if (!studentRegistry.TryGetConnectionId(clientId, out var connectionId) || string.IsNullOrWhiteSpace(connectionId))
            {
                continue;
            }

            await studentHubContext.Clients.Client(connectionId).SendAsync(HubMethods.FileTransferAssigned, command, cancellationToken);
            delivered.Add(clientId);
        }

        await teacherHubContext.Clients.All.SendAsync(
            HubMethods.FileProgressUpdated,
            new FileDeliveryProgressDto(transferId, "server", 0, command.TotalChunks, false, $"Dispatched to {delivered.Count} clients"),
            cancellationToken);

        await auditService.RecordAsync("file_dispatched", "operator", $"Transfer {transferId} -> {string.Join(',', delivered)}", cancellationToken);

        LogDispatchedTransfer(logger, transferId, delivered.Count, null);
        return Ok(new { deliveredCount = delivered.Count });
    }

    /// <summary>
    /// Returns missing chunks list for student resume.
    /// </summary>
    [HttpPost("{transferId}/missing")]
    [ProducesResponseType<MissingChunksResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MissingChunksResponseDto>> MissingChunks(string transferId, [FromBody] MissingChunksRequestDto request, CancellationToken cancellationToken)
    {
        if (!await ValidateStudentRequestAsync(request.ClientId, cancellationToken))
        {
            return Unauthorized("Invalid client token.");
        }

        var result = await transferCoordinator.GetMissingChunksAsync(transferId, request.ExistingChunkIndexes, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Downloads a specific chunk.
    /// </summary>
    [HttpGet("{transferId}/chunk/{chunkIndex:int}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadChunk(string transferId, int chunkIndex, [FromQuery] string clientId, CancellationToken cancellationToken)
    {
        if (!await ValidateStudentRequestAsync(clientId, cancellationToken))
        {
            return Unauthorized("Invalid client token.");
        }

        var chunk = await transferCoordinator.GetChunkAsync(transferId, chunkIndex, cancellationToken);
        Response.Headers.Append("X-Chunk-Sha256", chunk.Sha256);
        return File(chunk.Data, "application/octet-stream");
    }

    private async Task<bool> ValidateStudentRequestAsync(string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var token = Request.Headers["X-Controledu-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return await pairedClientStore.ValidateTokenAsync(clientId, token, cancellationToken);
    }
}
