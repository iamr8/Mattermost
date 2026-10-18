using MattermostMcp.Mattermost;
using MattermostMcp.Resolution;
using ModelContextProtocol;

namespace MattermostMcp.Tools;

/// <summary>
/// Opens a post for a tool that acts on it. The post must exist, not be deleted, and sit in an allowed channel.
/// Every failure gives the caller's one refusal text, so a missing, deleted and hidden post read the same.
/// </summary>
public sealed class PostAccess(ChannelResolver resolver, MattermostClient client)
{
    public async Task<(Post Post, ResolvedChannel Channel)> OpenAsync(string postId, string refusal, CancellationToken cancellationToken)
    {
        Post post;
        ResolvedChannel resolved;
        try
        {
            post = await client.GetPostAsync(postId, cancellationToken);
            resolved = await resolver.ResolveAsync(post.ChannelId, cancellationToken);
        }
        catch (Exception ex) when (PostTools.IsPostRefusal(ex))
        {
            throw new McpException(refusal);
        }

        return post.DeleteAt == 0 ? (post, resolved) : throw new McpException(refusal);
    }
}
