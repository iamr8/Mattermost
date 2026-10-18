using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using MattermostMcp.Mattermost;
using MattermostMcp.Output;
using MattermostMcp.Resolution;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MattermostMcp.Tools;

public sealed partial class PostTools(
    ChannelResolver resolver,
    ChannelMap map,
    ChannelLabeler labeler,
    ServerState state,
    MattermostClient client,
    PostShaper shaper)
{
    private const string Counts = " Zero counts and false flags are omitted from each post.";

    [McpServerTool(Name = "get_posts", Title = "Get posts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reads recent posts of one channel, newest first. Pass `next_before` from the result as `before` to read older posts." + Counts)]
    public async Task<string> GetPosts(
        [Description("team/name, name, @username, or a channel id.")] string channel,
        [Description("How many posts, 1 to 200. Default 30.")] int? limit = null,
        [Description("A post id: return posts older than this one.")] string? before = null,
        CancellationToken cancellationToken = default)
    {
        var count = Limits.Resolve(limit, 30, 200);
        var cursor = before is null ? null : Limits.PostId(before);
        var resolved = await resolver.ResolveAsync(channel, cancellationToken);

        var page = await client.GetPostsAsync(resolved.Channel.Id, count, cursor, cancellationToken);

        // Follow `order`: the `posts` map is unordered and may hold extra thread roots.
        var posts = page.Order.Where(page.Posts.ContainsKey).Select(id => page.Posts[id]).ToList();
        var views = await shaper.ShapeAsync(posts, null, cancellationToken);

        // `prev_post_id` points just outside the page, so the cursor is the oldest id inside it.
        var nextBefore = page.Order.Count > 0 && page.PrevPostId != "" ? page.Order[^1] : null;
        return Compact.Serialize(new PostsResult(resolved.Label, views, nextBefore));
    }

    [McpServerTool(Name = "get_thread", Title = "Get thread", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reads a thread: the root post and its replies, oldest first. `truncated` means more replies exist." + Counts)]
    public async Task<string> GetThread(
        [Description("The id of any post in the thread.")] string post_id,
        [Description("How many replies, 1 to 200. Default 50. The root is not counted.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var id = Limits.PostId(post_id);
        var count = Limits.Resolve(limit, 50, 200);

        // The post must be fetched to learn its channel; nothing from it is kept unless the channel passes.
        // A missing post and a refused one get the same text, and the server-supplied channel id is not shown.
        Post post;
        ResolvedChannel resolved;
        try
        {
            post = await client.GetPostAsync(id, cancellationToken);
            resolved = await resolver.ResolveAsync(post.ChannelId, cancellationToken);
        }
        catch (Exception ex) when (IsPostRefusal(ex))
        {
            throw new McpException("post not available");
        }

        var thread = await client.GetThreadAsync(id, cancellationToken);
        if (thread.Posts.Values.Any(p => p.ChannelId != resolved.Channel.Id))
        {
            throw new McpException("thread spans several channels");
        }

        var rootId = post.RootId == "" ? post.Id : post.RootId;
        var replies = thread.Posts.Values
            .Where(p => p.Id != rootId && p.DeleteAt == 0)
            .OrderBy(p => p.CreateAt)
            .ThenBy(p => p.Id)
            .ToList();
        var shown = replies.Take(count).ToList();

        var root = thread.Posts.TryGetValue(rootId, out var rootPost) ? rootPost : null;
        var views = await shaper.ShapeAsync(root is null ? shown : [root, .. shown], null, cancellationToken);
        var rootView = views.FirstOrDefault(v => v.Id == rootId); // the shaper drops a deleted root

        return Compact.Serialize(new ThreadResult(resolved.Label, rootView, views.Where(v => v.Id != rootId).ToList(), replies.Count > count));
    }

    [McpServerTool(Name = "search_posts", Title = "Search posts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Searches posts, newest first. Supports quotes, -exclusion and trailing * wildcards in `query`. Use the `channel` argument, not in:. Hits from channels that are not allowed are left out silently." + Counts)]
    public async Task<string> SearchPosts(
        [Description("Search terms.")] string query,
        [Description("Limit to one channel: team/name, name, @username, or a channel id.")] string? channel = null,
        [Description("Only posts by this username.")] string? from = null,
        [Description("Only posts after this date, yyyy-mm-dd.")] string? after_date = null,
        [Description("Only posts before this date, yyyy-mm-dd.")] string? before_date = null,
        [Description("Only posts on this date, yyyy-mm-dd.")] string? on_date = null,
        [Description("How many posts, 1 to 100. Default 30, in total across teams.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var count = Limits.Resolve(limit, 30, 100);
        var terms = BuildTerms(query, from, after_date, before_date, on_date);

        var scope = channel is null ? null : await resolver.ResolveAsync(channel, cancellationToken);
        var teamIds = scope switch
        {
            null => state.Policy.SearchTeamIds,
            { Channel.TeamId: not "" } => [scope.Channel.TeamId],
            _ => state.Policy.SearchTeamIds.Take(1).ToList(),
        };
        if (teamIds.Count == 0)
        {
            throw new McpException("no team to search in");
        }

        if (scope is not null)
        {
            terms += $" in:{scope.Channel.Name}";
        }

        var hits = new Dictionary<string, Post>();
        foreach (var teamId in teamIds)
        {
            var found = await client.SearchPostsAsync(teamId, terms, count, cancellationToken);
            foreach (var id in found.Order.Where(found.Posts.ContainsKey))
            {
                hits.TryAdd(id, found.Posts[id]);
            }
        }

        // The hard check: only posts whose channel passes the same predicate as every other tool.
        await map.EnsureFreshAsync(cancellationToken);
        // Dropped hits leave no trace: a count would tell the model that matches exist in hidden channels.
        var kept = new List<(Post Post, Channel Channel)>();
        foreach (var post in hits.Values.Where(p => p.DeleteAt == 0))
        {
            var hitChannel = map.Find(post.ChannelId);
            if (hitChannel is not null && state.Policy.IsAllowed(hitChannel) && (scope is null || hitChannel.Id == scope.Channel.Id))
            {
                kept.Add((post, hitChannel));
            }
        }

        var top = kept.OrderByDescending(k => k.Post.CreateAt).ThenBy(k => k.Post.Id).Take(count).ToList();
        var labels = await labeler.LabelsAsync(top.Select(k => k.Channel), cancellationToken);
        var views = await shaper.ShapeAsync(top.Select(k => k.Post).ToList(), p => labels[p.ChannelId], cancellationToken);

        return Compact.Serialize(new SearchResult(views));
    }

    internal static bool IsPostRefusal(Exception ex)
        => ex is MattermostException { Kind: MattermostErrorKind.NotFound or MattermostErrorKind.Forbidden }
           || (ex is McpException && ex is not MattermostException);

    private static string BuildTerms(string? query, string? from, string? after, string? before, string? on)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("query is required");
        }

        if (InFilter().IsMatch(query))
        {
            throw new McpException("use the channel argument instead of in:");
        }

        var terms = query.Trim();
        if (from is not null)
        {
            var user = from.Trim().TrimStart('@');
            terms += Patterns.Username.IsMatch(user) ? $" from:{user}" : throw new McpException("invalid from");
        }

        terms += Date("after", after) + Date("before", before) + Date("on", on);
        return terms;
    }

    private static string Date(string key, string? value)
    {
        if (value is null)
        {
            return "";
        }

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? $" {key}:{value}"
            : throw new McpException($"{key}_date must be yyyy-mm-dd");
    }

    // Any "in:" that does not continue a word, so "\"x\"in:y" and "x-in:y" are caught but "plugin:y" is not.
    [GeneratedRegex(@"(?<![A-Za-z0-9])-?in:", RegexOptions.IgnoreCase)]
    private static partial Regex InFilter();
}
