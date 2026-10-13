using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace MattermostMcp.Tools;

/// <summary>Message rules and the write budget. It knows nothing about Mattermost.</summary>
public sealed partial class WriteGuard(TimeProvider time)
{
    public const int MaxMessageLength = 16000;
    public const int MaxWrites = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly Queue<DateTimeOffset> _writes = new();
    private readonly object _gate = new();

    /// <summary>Returns the message unchanged, or refuses it.</summary>
    public static string Message(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new McpException("message is empty");
        }

        if (message.Length > MaxMessageLength)
        {
            throw new McpException($"message is too long (max {MaxMessageLength} characters)");
        }

        return MassMention().IsMatch(message)
            ? throw new McpException("message must not notify everyone (@all, @channel, @here)")
            : message;
    }

    /// <summary>Takes one write from the budget, or refuses. Call it right before the write, after every check.</summary>
    public void Spend()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            while (_writes.Count > 0 && now - _writes.Peek() >= Window)
            {
                _writes.Dequeue();
            }

            if (_writes.Count >= MaxWrites)
            {
                throw new McpException("too many writes, try again later");
            }

            _writes.Enqueue(now);
        }
    }

    // @all, @channel and @here as whole words. "a@here.com", "@all-hands" and "@heretic" do not match.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.\-])@(all|channel|here)(?![A-Za-z0-9_\-])", RegexOptions.IgnoreCase)]
    private static partial Regex MassMention();
}
