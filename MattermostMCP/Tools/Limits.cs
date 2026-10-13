using ModelContextProtocol;

namespace MattermostMcp.Tools;

internal static class Limits
{
    public static int Resolve(int? value, int @default, int max)
        => value switch
        {
            < 1 => throw new McpException("limit must be at least 1"),
            null => @default,
            _ => Math.Min(value.Value, max),
        };

    public static string PostId(string? value)
        => value is not null && Patterns.Id.IsMatch(value) ? value : throw new McpException("invalid post id");
}
