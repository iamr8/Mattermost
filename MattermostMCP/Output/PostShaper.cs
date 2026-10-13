using MattermostMcp.Mattermost;

namespace MattermostMcp.Output;

/// <summary>Turns posts into the compact shape: ISO times, usernames from one batched lookup, no deleted posts.</summary>
public sealed class PostShaper(MattermostClient client)
{
    public async Task<IReadOnlyList<PostView>> ShapeAsync(IReadOnlyList<Post> posts, Func<Post, string?>? channelLabel, CancellationToken cancellationToken)
    {
        var live = posts.Where(p => p.DeleteAt == 0).ToList();
        var ids = live.Select(p => p.UserId).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<string, string>()
            : (await client.GetUsersByIdsAsync(ids, cancellationToken)).ToDictionary(u => u.Id, u => u.Username);

        return live.Select(p => new PostView(
            p.Id,
            Compact.Iso(p.CreateAt),
            names.GetValueOrDefault(p.UserId, p.UserId),
            p.Message,
            Compact.NullIfEmpty(p.RootId),
            p.ReplyCount,
            p.FileIds?.Count ?? 0,
            p.EditAt > 0,
            channelLabel?.Invoke(p))).ToList();
    }
}
