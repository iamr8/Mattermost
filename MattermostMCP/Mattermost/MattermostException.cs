using ModelContextProtocol;

namespace MattermostMcp.Mattermost;

public enum MattermostErrorKind
{
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    Timeout,
    Unreachable,
    Other,
}

/// <summary>
/// A Mattermost failure with fixed, safe text. It is an <see cref="McpException"/>, so the model sees
/// the message; the response body is never copied into it.
/// </summary>
public sealed class MattermostException(MattermostErrorKind kind, string message, int? status = null) : McpException(message)
{
    public MattermostErrorKind Kind { get; } = kind;

    public int? Status { get; } = status;
}
