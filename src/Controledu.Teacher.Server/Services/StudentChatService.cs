using Controledu.Transport.Dto;
using System.Collections.Concurrent;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// In-memory chat history per student for teacher focused-view conversations.
/// </summary>
public interface IStudentChatService
{
    /// <summary>
    /// Adds message to history and returns normalized payload.
    /// </summary>
    StudentTeacherChatMessageDto Add(StudentTeacherChatMessageDto message);

    /// <summary>
    /// Returns latest messages for one student in ascending time order.
    /// </summary>
    IReadOnlyList<StudentTeacherChatMessageDto> GetLatest(string clientId, int take = 100);
}

internal sealed class StudentChatService : IStudentChatService
{
    private const int MaxMessagesPerStudent = 300;
    private readonly ConcurrentDictionary<string, List<StudentTeacherChatMessageDto>> _history = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);

    public StudentTeacherChatMessageDto Add(StudentTeacherChatMessageDto message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var clientId = (message.ClientId ?? string.Empty).Trim();
        if (clientId.Length == 0)
        {
            throw new InvalidOperationException("Client id is required for chat message.");
        }

        var normalized = message with
        {
            ClientId = clientId,
            MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? Guid.NewGuid().ToString("N") : message.MessageId.Trim(),
            SenderRole = string.IsNullOrWhiteSpace(message.SenderRole) ? "teacher" : message.SenderRole.Trim().ToLowerInvariant(),
            SenderDisplayName = string.IsNullOrWhiteSpace(message.SenderDisplayName) ? "Unknown" : message.SenderDisplayName.Trim(),
            Text = (message.Text ?? string.Empty).Trim(),
        };

        var sync = _locks.GetOrAdd(clientId, static _ => new object());
        lock (sync)
        {
            var list = _history.GetOrAdd(clientId, static _ => []);
            list.Add(normalized);
            if (list.Count > MaxMessagesPerStudent)
            {
                var removeCount = list.Count - MaxMessagesPerStudent;
                list.RemoveRange(0, removeCount);
            }
        }

        return normalized;
    }

    public IReadOnlyList<StudentTeacherChatMessageDto> GetLatest(string clientId, int take = 100)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return [];
        }

        var normalizedId = clientId.Trim();
        if (!_history.TryGetValue(normalizedId, out var list))
        {
            return [];
        }

        var sync = _locks.GetOrAdd(normalizedId, static _ => new object());
        lock (sync)
        {
            var count = Math.Clamp(take, 1, MaxMessagesPerStudent);
            return list.Count <= count
                ? list.ToArray()
                : list.Skip(Math.Max(0, list.Count - count)).ToArray();
        }
    }
}

