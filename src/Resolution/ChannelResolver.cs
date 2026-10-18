using MattermostMcp.Mattermost;
using ModelContextProtocol;

namespace MattermostMcp.Resolution;

public sealed record ResolvedChannel(Channel Channel, string Label);

/// <summary>
/// The only way a tool gets a channel id. Accepts <c>team/name</c>, <c>name</c>, <c>@username</c> and a channel id,
/// and refuses anything outside the channel map or the allowlist before any content is fetched.
/// </summary>
public sealed class ChannelResolver(ChannelMap map, ServerState state, MattermostClient client, ChannelLabeler labeler)
{
    public async Task<ResolvedChannel> ResolveAsync(string input, CancellationToken cancellationToken)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0)
        {
            throw new McpException("channel is required");
        }

        await map.EnsureFreshAsync(cancellationToken);

        var isDirect = input.StartsWith('@');
        string? peerId = null;
        var matches = await LookupAsync();
        if (matches.Count == 0)
        {
            await map.RefreshAsync(cancellationToken);
            matches = await LookupAsync();
        }

        // One text for "no such channel", "not a member" and "outside the allowlist": a guessed name must not
        // reveal which channels the user is in or which ones the allowlist hides.
        var allowed = matches.Where(state.Policy.IsAllowed).ToList();
        if (allowed.Count == 0)
        {
            throw Unavailable(input);
        }

        if (allowed.Count > 1)
        {
            var labels = await labeler.LabelsAsync(allowed, cancellationToken);
            throw new McpException($"ambiguous channel '{input}', use one of: {string.Join(", ", labels.Values.Order())}");
        }

        var channel = allowed[0];
        return new ResolvedChannel(channel, isDirect ? input : await labeler.LabelAsync(channel, cancellationToken));

        async Task<IReadOnlyList<Channel>> LookupAsync()
        {
            if (isDirect)
            {
                peerId ??= await FindUserIdAsync(input[1..]);
                var name = ChannelNames.DirectName(state.Me.Id, peerId);
                return map.Channels.Where(c => c.Type == Channel.Direct && c.Name == name).ToList();
            }

            if (input.Contains('/'))
            {
                var parts = input.Split('/');
                var team = parts.Length == 2 ? state.Teams.FirstOrDefault(t => t.Name == parts[0]) : null;
                return team is null ? [] : map.Channels.Where(c => c.TeamId == team.Id && c.Name == parts[1]).ToList();
            }

            if (Patterns.Id.IsMatch(input))
            {
                return map.Find(input) is { } byId ? [byId] : [];
            }

            return map.Channels.Where(c => c.Type is Channel.Public or Channel.Private && c.Name == input).ToList();
        }

        async Task<string> FindUserIdAsync(string username)
        {
            if (!Patterns.Username.IsMatch(username))
            {
                throw Unavailable(input);
            }

            try
            {
                return (await client.GetUserByUsernameAsync(username, cancellationToken)).Id;
            }
            catch (MattermostException ex) when (ex.Kind == MattermostErrorKind.NotFound)
            {
                throw Unavailable(input);
            }
        }
    }

    private static McpException Unavailable(string input)
        => new($"channel not available: {input} (not a member, or outside the allowlist)");
}
