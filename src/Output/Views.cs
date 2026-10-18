using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MattermostMcp.Mattermost;

namespace MattermostMcp.Output;

public sealed record PostView(string Id, string At, string User, string Text, string? ReplyTo, int Replies, int Files, bool Edited, string? Channel);

public sealed record ChannelView(string Id, string Name, string? DisplayName, string Type, string LastPostAt, string? Purpose, string? Header);

public sealed record PostsResult(string Channel, IReadOnlyList<PostView> Posts, string? NextBefore);

public sealed record ThreadResult(string Channel, PostView? Root, IReadOnlyList<PostView> Replies, bool Truncated);

public sealed record SearchResult(IReadOnlyList<PostView> Posts);

public sealed record WriteResult(string Id, string Channel, string At);

public sealed record ReactionResult(string PostId, string Emoji);

public sealed record EditResult(string Id, bool Edited);

public sealed record FileView(string Id, string Name, long Size, string Mime);

public sealed record FilesResult(IReadOnlyList<FileView> Files);

public sealed record DownloadResult(string Path, string Name, long Size, string Mime);

public sealed record UserView(string Id, string Username, string? Nickname, string? FirstName, string? LastName, string? Position);

/// <summary>Compact JSON for the model: snake_case, no nulls, no zero counts, no false flags.</summary>
public static class Compact
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        // The default encoder turns each non-ASCII letter into a \uXXXX escape: six characters for one Persian letter.
        // The output goes to a model, never into HTML, so the relaxed encoder is safe.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Iso(long epochMs) => DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public static string ChannelTypeName(Channel channel) => channel.Type switch
    {
        Channel.Public => "public",
        Channel.Private => "private",
        Channel.Direct => "direct",
        Channel.Group => "group",
        _ => channel.Type,
    };
}
