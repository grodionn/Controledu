using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Controledu.Teacher.Server.Hubs;
using Controledu.Teacher.Server.Services;
using Controledu.Storage.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Student management endpoints.
/// </summary>
[ApiController]
[Route("api/students")]
public sealed class StudentsController(
    IPairedClientStore pairedClientStore,
    IStudentRegistry studentRegistry,
    IStudentChatService studentChatService,
    IAuditService auditService,
    IHubContext<StudentHub> studentHub,
    IHubContext<TeacherHub> teacherHub) : ControllerBase
{
    /// <summary>
    /// Returns recent chat messages for one student conversation.
    /// </summary>
    [HttpGet("{clientId}/chat")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetStudentChatHistory(string clientId, [FromQuery] int take = 100)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("Client id is required.");
        }

        var messages = studentChatService.GetLatest(clientId, take);
        return Ok(new StudentChatHistoryResponse(messages));
    }

    /// <summary>
    /// Sends text chat message to one student endpoint overlay.
    /// </summary>
    [HttpPost("{clientId}/chat")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendTeacherChatMessage(
        string clientId,
        [FromBody] TeacherChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("Client id is required.");
        }

        var text = (request?.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return BadRequest("Text is required.");
        }

        if (!studentRegistry.TryGetConnectionId(clientId, out var connectionId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return NotFound("Student is offline or not connected.");
        }

        var message = studentChatService.Add(new StudentTeacherChatMessageDto(
            ClientId: clientId.Trim(),
            MessageId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            SenderRole: "teacher",
            SenderDisplayName: string.IsNullOrWhiteSpace(request!.TeacherDisplayName) ? "Teacher Console" : request.TeacherDisplayName.Trim(),
            Text: text));

        await studentHub.Clients.Client(connectionId).SendAsync(HubMethods.TeacherChatMessageRequested, message, cancellationToken);
        await teacherHub.Clients.All.SendAsync(HubMethods.ChatMessageReceived, message, cancellationToken);
        await auditService.RecordAsync("teacher_chat_sent", "operator", $"{clientId} len={message.Text.Length}", cancellationToken);

        return Ok(new { ok = true, message = "Teacher chat message dispatched.", chat = message });
    }

    /// <summary>
    /// Sends a teacher text message to one student endpoint for TTS playback.
    /// </summary>
    [HttpPost("{clientId}/tts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendTeacherTts(
        string clientId,
        [FromBody] TeacherTtsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("Client id is required.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.MessageText))
        {
            return BadRequest("Message text is required.");
        }

        if (!studentRegistry.TryGetConnectionId(clientId, out var connectionId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return NotFound("Student is offline or not connected.");
        }

        var command = new TeacherTtsCommandDto(
            ClientId: clientId.Trim(),
            MessageText: request.MessageText.Trim(),
            TeacherDisplayName: string.IsNullOrWhiteSpace(request.TeacherDisplayName) ? "Teacher Console" : request.TeacherDisplayName.Trim(),
            LanguageCode: string.IsNullOrWhiteSpace(request.LanguageCode) ? null : request.LanguageCode.Trim(),
            VoiceName: string.IsNullOrWhiteSpace(request.VoiceName) ? null : request.VoiceName.Trim(),
            SpeakingRate: request.SpeakingRate,
            Pitch: request.Pitch,
            RequestId: Guid.NewGuid().ToString("N"));

        await studentHub.Clients.Client(connectionId).SendAsync(HubMethods.TeacherTtsRequested, command, cancellationToken);
        await auditService.RecordAsync("teacher_tts_sent", "operator", $"{clientId} len={command.MessageText.Length}", cancellationToken);

        return Ok(new { ok = true, message = "Teacher TTS command dispatched." });
    }

    /// <summary>
    /// Assigns accessibility profile to online student endpoint.
    /// </summary>
    [HttpPost("{clientId}/accessibility-profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignAccessibilityProfile(
        string clientId,
        [FromBody] AccessibilityProfileAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("Client id is required.");
        }

        if (request?.Profile is null)
        {
            return BadRequest("Profile is required.");
        }

        if (!studentRegistry.TryGetConnectionId(clientId, out var connectionId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return NotFound("Student is offline or not connected.");
        }

        var command = new AccessibilityProfileAssignmentCommandDto(
            ClientId: clientId.Trim(),
            TeacherDisplayName: string.IsNullOrWhiteSpace(request.TeacherDisplayName) ? "Teacher Console" : request.TeacherDisplayName.Trim(),
            Profile: request.Profile);

        await studentHub.Clients.Client(connectionId).SendAsync(
            HubMethods.AccessibilityProfileAssigned,
            command,
            cancellationToken);

        await auditService.RecordAsync(
            "accessibility_profile_assigned",
            "operator",
            $"{clientId} preset={command.Profile.ActivePreset}",
            cancellationToken);

        return Ok(new { ok = true, message = "Accessibility profile command dispatched." });
    }

    /// <summary>
    /// Removes paired student and requests client-side unpair.
    /// </summary>
    [HttpDelete("{clientId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStudent(string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("Client id is required.");
        }

        var deleted = await pairedClientStore.DeleteAsync(clientId, cancellationToken);
        if (!deleted)
        {
            return NotFound("Device not found.");
        }

        if (studentRegistry.TryGetConnectionId(clientId, out var connectionId) && !string.IsNullOrWhiteSpace(connectionId))
        {
            await studentHub.Clients.Client(connectionId).SendAsync(
                HubMethods.ForceUnpair,
                "Server removed this device from the managed list.",
                cancellationToken);
        }

        studentRegistry.Remove(clientId);
        await teacherHub.Clients.All.SendAsync(HubMethods.StudentListChanged, studentRegistry.GetAll(), cancellationToken);
        await auditService.RecordAsync("device_removed", "operator", clientId, cancellationToken);

        return Ok(new { ok = true });
    }
}

/// <summary>
/// Teacher API payload for assigning accessibility profile to one student.
/// </summary>
public sealed record AccessibilityProfileAssignmentRequest(
    AccessibilityProfileUpdateDto Profile,
    string? TeacherDisplayName = null);

/// <summary>
/// Teacher API payload for sending TTS text to one student endpoint.
/// </summary>
public sealed record TeacherTtsRequest(
    string MessageText,
    string? TeacherDisplayName = null,
    string? LanguageCode = null,
    string? VoiceName = null,
    double? SpeakingRate = null,
    double? Pitch = null);

/// <summary>
/// Teacher API payload for sending text chat to one student.
/// </summary>
public sealed record TeacherChatMessageRequest(string Text, string? TeacherDisplayName = null);

/// <summary>
/// Teacher API response with recent student conversation history.
/// </summary>
public sealed record StudentChatHistoryResponse(IReadOnlyList<StudentTeacherChatMessageDto> Messages);
