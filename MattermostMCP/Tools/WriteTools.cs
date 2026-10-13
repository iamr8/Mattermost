using System.ComponentModel;
using System.Text.RegularExpressions;
using MattermostMcp.Mattermost;
using MattermostMcp.Output;
using MattermostMcp.Resolution;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MattermostMcp.Tools;

/// <summary>Registered only when <c>MATTERMOST_READ_ONLY=false</c>. Every tool goes through the same allowlist as the reads.</summary>
public sealed partial class WriteTools(ChannelResolver resolver, ServerState state, MattermostClient client, WriteGuard guard)
{
    private const string PostUnavailable = "post not available";

    [McpServerTool(Name = "post_message", Title = "Post message", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Posts a new message to one channel. Only post what the user asked for. @all, @channel and @here are refused.")]
    public async Task<string> PostMessage(
        [Description("team/name, name, @username, or a channel id.")] string channel,
        [Description("The message text, Markdown allowed, up to 16000 characters.")] string message,
        CancellationToken cancellationToken = default)
    {
        var text = WriteGuard.Message(message);
        var resolved = await resolver.ResolveAsync(channel, cancellationToken);

        guard.Spend();
        var created = await client.CreatePostAsync(resolved.Channel.Id, text, null, cancellationToken);

        return Compact.Serialize(new WriteResult(created.Id, resolved.Label, Compact.Iso(created.CreateAt)));
    }

    [McpServerTool(Name = "reply_to_thread", Title = "Reply to thread", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Replies in the thread of a post. Only reply with what the user asked for. @all, @channel and @here are refused.")]
    public async Task<string> ReplyToThread(
        [Description("The id of any post in the thread.")] string post_id,
        [Description("The reply text, Markdown allowed, up to 16000 characters.")] string message,
        CancellationToken cancellationToken = default)
    {
        var id = Limits.PostId(post_id);
        var text = WriteGuard.Message(message);
        var (post, resolved) = await OpenPostAsync(id, cancellationToken);

        guard.Spend();
        var created = await client.CreatePostAsync(resolved.Channel.Id, text, post.RootId == "" ? post.Id : post.RootId, cancellationToken);

        return Compact.Serialize(new WriteResult(created.Id, resolved.Label, Compact.Iso(created.CreateAt)));
    }

    [McpServerTool(Name = "add_reaction", Title = "Add reaction", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds an emoji reaction to a post, as the token owner.")]
    public async Task<string> AddReaction(
        [Description("The id of the post.")] string post_id,
        [Description("The emoji name, for example thumbsup. Colons are optional.")] string emoji,
        CancellationToken cancellationToken = default)
    {
        var id = Limits.PostId(post_id);
        var name = EmojiName(emoji);
        var (post, _) = await OpenPostAsync(id, cancellationToken);

        guard.Spend();
        var reaction = await client.AddReactionAsync(state.Me.Id, post.Id, name, cancellationToken);

        return Compact.Serialize(new ReactionResult(reaction.PostId, reaction.EmojiName));
    }

    [McpServerTool(Name = "edit_post", Title = "Edit post", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Replaces the text of one of the token owner's own posts. Other people's posts cannot be edited. @all, @channel and @here are refused.")]
    public async Task<string> EditPost(
        [Description("The id of the post to edit.")] string post_id,
        [Description("The new text, Markdown allowed, up to 16000 characters.")] string message,
        CancellationToken cancellationToken = default)
    {
        var id = Limits.PostId(post_id);
        var text = WriteGuard.Message(message);
        var (post, _) = await OpenPostAsync(id, cancellationToken);

        // Same text as every other refusal, so the model cannot tell a foreign post from a hidden one.
        if (post.UserId != state.Me.Id)
        {
            throw new McpException(PostUnavailable);
        }

        guard.Spend();
        var patched = await client.PatchPostAsync(post.Id, text, cancellationToken);

        return Compact.Serialize(new EditResult(patched.Id, true));
    }

    /// <summary>Fetches a post and checks its channel passes the allowlist. A missing, hidden or deleted post all read the same.</summary>
    private async Task<(Post Post, ResolvedChannel Channel)> OpenPostAsync(string id, CancellationToken cancellationToken)
    {
        Post post;
        ResolvedChannel resolved;
        try
        {
            post = await client.GetPostAsync(id, cancellationToken);
            resolved = await resolver.ResolveAsync(post.ChannelId, cancellationToken);
        }
        catch (Exception ex) when (PostTools.IsPostRefusal(ex))
        {
            throw new McpException(PostUnavailable);
        }

        return post.DeleteAt == 0 ? (post, resolved) : throw new McpException(PostUnavailable);
    }

    // ":thumbsup:" and "thumbsup" both give "thumbsup". One pair of colons at most.
    private static string EmojiName(string? emoji)
    {
        var name = (emoji ?? "").Trim();
        if (name.Length > 2 && name[0] == ':' && name[^1] == ':')
        {
            name = name[1..^1];
        }

        return Emoji().IsMatch(name) ? name : throw new McpException("invalid emoji name");
    }

    [GeneratedRegex("^[a-z0-9_+-]{1,64}$")]
    private static partial Regex Emoji();
}
