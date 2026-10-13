using System.Net;
using System.Text.Json;

namespace MattermostMcp.Tests.Fakes;

/// <summary>Builders for Mattermost-shaped JSON, written from the API reference (snake_case, epoch ms).</summary>
internal static class Mm
{
    public const long T0 = 1_790_000_000_000;

    public static object Post(string id, long createAt, string userId, string channelId, string message, string rootId = "", long deleteAt = 0, long editAt = 0, int replyCount = 0, string[]? fileIds = null)
        => new
        {
            id,
            create_at = createAt,
            update_at = createAt,
            edit_at = editAt,
            delete_at = deleteAt,
            user_id = userId,
            channel_id = channelId,
            root_id = rootId,
            message,
            type = "",
            reply_count = replyCount,
            file_ids = fileIds ?? [],
        };

    /// <summary>A PostList. <paramref name="posts"/> is keyed in the order given, which tests make differ from <paramref name="order"/>.</summary>
    public static string PostList(string[] order, object[] posts, string nextPostId = "", string prevPostId = "")
    {
        var map = new Dictionary<string, object>();
        foreach (var post in posts)
        {
            map[(string)post.GetType().GetProperty("id")!.GetValue(post)!] = post;
        }

        return JsonSerializer.Serialize(new { order, posts = map, next_post_id = nextPostId, prev_post_id = prevPostId });
    }

    public static string Json(object value) => JsonSerializer.Serialize(value);

    public static HttpResponseMessage Response(HttpStatusCode status, string body, string contentType = "application/json")
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, contentType) };
}
