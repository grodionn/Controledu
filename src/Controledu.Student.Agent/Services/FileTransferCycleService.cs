using Controledu.Student.Agent.Models;
using Controledu.Transport.Dto;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Handles queued file transfer commands.
/// </summary>
public interface IFileTransferCycleService
{
    /// <summary>
    /// Drains and processes all pending transfer commands.
    /// </summary>
    Task RunAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken);
}

internal sealed class FileTransferCycleService(
    FileTransferReceiver fileTransferReceiver,
    StudentHubClient hubClient,
    ILogger<FileTransferCycleService> logger) : IFileTransferCycleService
{
    public async Task RunAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueTransferCommand(out var command))
        {
            try
            {
                await fileTransferReceiver.ProcessAsync(binding, command, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Transfer {TransferId} failed", command.TransferId);
                await hubClient.SendFileProgressAsync(
                    new FileDeliveryProgressDto(command.TransferId, binding.ClientId, 0, command.TotalChunks, false, ex.Message),
                    cancellationToken);
            }
        }
    }
}
