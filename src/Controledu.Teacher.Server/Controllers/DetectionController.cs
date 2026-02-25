using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Controledu.Common.Runtime;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Hubs;
using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// AI detection policy and event feed endpoints.
/// </summary>
[ApiController]
[Route("api/detection")]
public sealed class DetectionController(
    IDetectionPolicyService detectionPolicyService,
    IDetectionEventStore detectionEventStore,
    IStudentRegistry studentRegistry,
    IPairedClientStore pairedClientStore,
    IHubContext<StudentHub> studentHub,
    IHubContext<TeacherHub> teacherHub,
    IAuditService auditService) : ControllerBase
{
    private const string ExportsRootFolder = "detection-exports";

    /// <summary>
    /// Returns current detection policy.
    /// </summary>
    [HttpGet("settings")]
    [ProducesResponseType<DetectionPolicyDto>(StatusCodes.Status200OK)]
    public Task<DetectionPolicyDto> GetSettings(CancellationToken cancellationToken) =>
        detectionPolicyService.GetAsync(cancellationToken);

    /// <summary>
    /// Updates detection policy and broadcasts it to connected agents.
    /// </summary>
    [HttpPut("settings")]
    [ProducesResponseType<DetectionPolicyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DetectionPolicyDto>> UpdateSettings([FromBody] DetectionPolicyDto policy, CancellationToken cancellationToken)
    {
        var saved = await detectionPolicyService.SaveAsync(policy, "operator", cancellationToken);

        studentRegistry.SetDetectionEnabledForAll(saved.Enabled);
        await studentHub.Clients.All.SendAsync(HubMethods.DetectionPolicyUpdated, saved, cancellationToken);
        await teacherHub.Clients.All.SendAsync(HubMethods.DetectionPolicyUpdated, saved, cancellationToken);
        await teacherHub.Clients.All.SendAsync(HubMethods.StudentListChanged, studentRegistry.GetAll(), cancellationToken);

        return Ok(saved);
    }

    /// <summary>
    /// Updates only data-collection mode. Optional targets apply immediate transient push.
    /// </summary>
    [HttpPost("data-collection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDataCollection([FromBody] DataCollectionToggleRequest request, CancellationToken cancellationToken)
    {
        var current = await detectionPolicyService.GetAsync(cancellationToken);
        var updated = current with
        {
            DataCollectionModeEnabled = false,
            DataCollectionSampleRate = 0,
            DataCollectionStoreFullFrames = false,
            DataCollectionStoreThumbnails = false,
        };
        await detectionPolicyService.SaveAsync(updated, "operator", cancellationToken);

        if (request.TargetClientIds is { Length: > 0 })
        {
            var targets = request.TargetClientIds.Distinct(StringComparer.Ordinal).ToArray();
            foreach (var target in targets)
            {
                if (!studentRegistry.TryGetConnectionId(target, out var connectionId) || string.IsNullOrWhiteSpace(connectionId))
                {
                    continue;
                }

                await studentHub.Clients.Client(connectionId).SendAsync(HubMethods.DetectionPolicyUpdated, updated, cancellationToken);
            }

            await auditService.RecordAsync("detection_data_collection_targets", "operator", $"requested={request.Enabled}; effective=false; targets={string.Join(',', targets)}", cancellationToken);
        }
        else
        {
            await studentHub.Clients.All.SendAsync(HubMethods.DetectionPolicyUpdated, updated, cancellationToken);
            await teacherHub.Clients.All.SendAsync(HubMethods.DetectionPolicyUpdated, updated, cancellationToken);
            await auditService.RecordAsync("detection_data_collection_global", "operator", $"requested={request.Enabled}; effective=false", cancellationToken);
        }

        return Ok(new { ok = true, enabled = false, message = "Data collection is disabled in production mode." });
    }

    /// <summary>
    /// Returns latest AI detection alert events.
    /// </summary>
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<AlertEventDto>>(StatusCodes.Status200OK)]
    public IReadOnlyList<AlertEventDto> GetEvents([FromQuery] int take = 200) =>
        detectionEventStore.GetLatest(take);

    /// <summary>
    /// Requests immediate dataset export from selected online students.
    /// </summary>
    [HttpPost("exports/request")]
    [ProducesResponseType<DetectionExportRequestResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DetectionExportRequestResultDto>> RequestExports(
        [FromBody] DetectionExportRequestDto? request,
        CancellationToken cancellationToken)
    {
        var knownStudents = studentRegistry.GetAll();

        var requestedClientIds = request?.TargetClientIds is { Length: > 0 }
            ? request.TargetClientIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : knownStudents
                .Where(static student => student.IsOnline)
                .Select(static student => student.ClientId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        var requested = new List<string>(requestedClientIds.Length);
        var skipped = new List<string>();

        foreach (var clientId in requestedClientIds)
        {
            if (!studentRegistry.TryGetConnectionId(clientId, out var connectionId) || string.IsNullOrWhiteSpace(connectionId))
            {
                skipped.Add(clientId);
                continue;
            }

            await studentHub.Clients.Client(connectionId).SendAsync(
                HubMethods.DetectionExportRequested,
                Guid.NewGuid().ToString("N"),
                cancellationToken);

            requested.Add(clientId);
        }

        await auditService.RecordAsync(
            "detection_export_requested",
            "operator",
            $"requested={requested.Count}; skipped={skipped.Count}; targets={string.Join(',', requestedClientIds)}",
            cancellationToken);

        return Ok(new DetectionExportRequestResultDto(
            RequestedCount: requested.Count,
            SkippedCount: skipped.Count,
            RequestedClientIds: requested.ToArray(),
            SkippedClientIds: skipped.ToArray()));
    }

    /// <summary>
    /// Upload endpoint used by student agent to send diagnostics export archive.
    /// </summary>
    [HttpPost("exports/upload")]
    [ProducesResponseType<DetectionExportArtifactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DetectionExportArtifactDto>> UploadExport(
        [FromQuery] string clientId,
        CancellationToken cancellationToken)
    {
        if (!await ValidateStudentRequestAsync(clientId, cancellationToken))
        {
            return Unauthorized("Invalid client token.");
        }

        var exportsRoot = GetExportsRootPath();
        Directory.CreateDirectory(exportsRoot);

        var studentDirectory = Path.Combine(exportsRoot, SanitizeSegment(clientId));
        Directory.CreateDirectory(studentDirectory);

        var incomingName = Request.Headers["X-Controledu-FileName"].FirstOrDefault();
        var normalizedName = string.IsNullOrWhiteSpace(incomingName) ? "dataset-export.zip" : Path.GetFileName(incomingName);
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{normalizedName}";
        var filePath = Path.Combine(studentDirectory, fileName);

        await using (var target = System.IO.File.Create(filePath))
        {
            await Request.Body.CopyToAsync(target, cancellationToken);
        }

        var artifact = BuildExportArtifact(new FileInfo(filePath));
        await teacherHub.Clients.All.SendAsync(HubMethods.DetectionExportReady, artifact, cancellationToken);
        await auditService.RecordAsync(
            "detection_export_uploaded",
            clientId,
            $"file={artifact.FileName}; size={artifact.SizeBytes}",
            cancellationToken);

        return Ok(artifact);
    }

    /// <summary>
    /// Lists uploaded diagnostics export archives.
    /// </summary>
    [HttpGet("exports/list")]
    [ProducesResponseType<IReadOnlyList<DetectionExportArtifactDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<DetectionExportArtifactDto>> ListExports([FromQuery] int take = 80)
    {
        var exportsRoot = GetExportsRootPath();
        if (!Directory.Exists(exportsRoot))
        {
            return Ok(Array.Empty<DetectionExportArtifactDto>());
        }

        var effectiveTake = Math.Clamp(take, 1, 500);
        var list = Directory
            .EnumerateFiles(exportsRoot, "*.zip", SearchOption.AllDirectories)
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .Take(effectiveTake)
            .Select(BuildExportArtifact)
            .ToArray();

        return Ok(list);
    }

    /// <summary>
    /// Downloads a previously uploaded diagnostics export archive.
    /// </summary>
    [HttpGet("exports/download/{exportId}")]
    public IActionResult DownloadExport(string exportId)
    {
        if (!TryResolveExportPath(exportId, out var fullPath))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, "application/zip", Path.GetFileName(fullPath));
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

    private static string GetExportsRootPath() => Path.Combine(AppPaths.GetExportsPath(), ExportsRootFolder);

    private static string SanitizeSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private DetectionExportArtifactDto BuildExportArtifact(FileInfo file)
    {
        var exportsRoot = GetExportsRootPath();
        var relative = Path.GetRelativePath(exportsRoot, file.FullName);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var clientId = segments.Length > 1 ? segments[0] : "unknown";
        var studentName = studentRegistry.GetAll().FirstOrDefault(student => student.ClientId == clientId)?.HostName ?? clientId;
        var exportId = EncodeExportId(relative);

        return new DetectionExportArtifactDto(
            ExportId: exportId,
            ClientId: clientId,
            StudentDisplayName: studentName,
            CreatedAtUtc: new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            FileName: file.Name,
            SizeBytes: file.Length,
            DownloadUrl: $"/api/detection/exports/download/{Uri.EscapeDataString(exportId)}");
    }

    private static string EncodeExportId(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var bytes = Encoding.UTF8.GetBytes(normalized);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryResolveExportPath(string exportId, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(exportId))
        {
            return false;
        }

        var base64 = exportId.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        string? relative;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            relative = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(relative))
        {
            return false;
        }

        var root = GetExportsRootPath();
        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}

/// <summary>
/// Request for data collection mode toggling.
/// </summary>
public sealed record DataCollectionToggleRequest(bool Enabled, string[]? TargetClientIds = null);

/// <summary>
/// Request for immediate export collection from selected students.
/// </summary>
public sealed record DetectionExportRequestDto(string[]? TargetClientIds = null);

/// <summary>
/// Result of export request dispatch.
/// </summary>
public sealed record DetectionExportRequestResultDto(
    int RequestedCount,
    int SkippedCount,
    string[] RequestedClientIds,
    string[] SkippedClientIds);
