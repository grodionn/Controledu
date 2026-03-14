using Controledu.Student.Host.Contracts;
using Controledu.Student.Host.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Controledu.Student.Host;

public static partial class StudentLocalHostFactory
{
    private static void MapChatEndpoints(WebApplication app)
    {
        app.MapGet("/api/chat/thread", async (IStudentChatService studentChatService, CancellationToken cancellationToken) =>
        {
            var thread = await studentChatService.GetThreadAsync(cancellationToken);
            return Results.Ok(thread);
        });

        app.MapPost("/api/chat/messages", async (
            StudentChatSendRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var message = await studentChatService.QueueStudentMessageAsync(request, cancellationToken);
                return Results.Ok(message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/chat/messages/teacher", async (
            TeacherChatLocalDeliveryRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var saved = await studentChatService.ReceiveTeacherMessageAsync(request, cancellationToken);
            return saved is null ? Results.BadRequest("Teacher chat message text is required.") : Results.Ok(saved);
        });

        app.MapPost("/api/chat/outgoing/peek", async (IStudentChatService studentChatService, CancellationToken cancellationToken) =>
        {
            var messages = await studentChatService.PeekOutgoingAsync(cancellationToken);
            return Results.Ok(new StudentChatOutboxPeekResponse(messages));
        });

        app.MapPost("/api/chat/outgoing/ack", async (
            StudentChatOutboxAckRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var removed = await studentChatService.AcknowledgeOutgoingAsync(request.MessageIds ?? [], cancellationToken);
            return Results.Ok(new OkResponse(true, $"Ack removed {removed} message(s)."));
        });

        app.MapPost("/api/chat/preferences", async (
            StudentChatPreferencesUpdateRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var prefs = await studentChatService.UpdatePreferencesAsync(request, cancellationToken);
            return Results.Ok(prefs);
        });

        app.MapGet("/api/captions/live", async (IStudentLiveCaptionService studentLiveCaptionService, CancellationToken cancellationToken) =>
        {
            var caption = await studentLiveCaptionService.GetCurrentAsync(cancellationToken);
            return Results.Ok(caption);
        });

        app.MapPost("/api/captions/live/teacher", async (
            TeacherLiveCaptionLocalDeliveryRequest request,
            IStudentLiveCaptionService studentLiveCaptionService,
            CancellationToken cancellationToken) =>
        {
            var caption = await studentLiveCaptionService.ApplyTeacherCaptionAsync(request, cancellationToken);
            return Results.Ok(caption);
        });
    }
}
