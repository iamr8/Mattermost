using System.ComponentModel;
using MattermostMcp.Mattermost;
using MattermostMcp.Output;
using MattermostMcp.Resolution;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MattermostMcp.Tools;

public sealed class ChannelTools(ChannelMap map, ServerState state, ChannelResolver resolver, ChannelLabeler labeler)
{
    private const int PurposeLimit = 200;

    [McpServerTool(Name = "list_channels", Title = "List channels", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists the channels this server may read, newest activity first. `name` is the value to pass as `channel` to other tools; direct channels are @username.")]
    public async Task<string> ListChannels(
        [Description("Team name (the URL slug) to list channels of.")] string? team = null,
        [Description("One of: public, private, direct, group.")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var wantedType = type is null ? null : ParseType(type);
        var wantedTeam = team is null ? null : state.Teams.FirstOrDefault(t => t.Name == team) ?? throw new McpException($"unknown team: {team}");

        await map.RefreshAsync(cancellationToken);

        var channels = map.Channels
            .Where(state.Policy.IsAllowed)
            .Where(c => wantedType is null || c.Type == wantedType)
            .Where(c => wantedTeam is null || c.TeamId == wantedTeam.Id)
            .OrderByDescending(c => c.LastPostAt)
            .ToList();
        var labels = await labeler.LabelsAsync(channels, cancellationToken);

        return Compact.Serialize(channels.Select(c => new ChannelView(
            c.Id,
            labels[c.Id],
            c.Type == Channel.Direct ? null : Compact.NullIfEmpty(c.DisplayName),
            Compact.ChannelTypeName(c),
            Compact.Iso(c.LastPostAt),
            Compact.NullIfEmpty(Truncate(c.Purpose)),
            null)));
    }

    [McpServerTool(Name = "get_channel", Title = "Get channel", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Shows one channel with its full header and purpose.")]
    public async Task<string> GetChannel(
        [Description("team/name, name, @username, or a channel id.")] string channel,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.ResolveAsync(channel, cancellationToken);
        var c = resolved.Channel;

        return Compact.Serialize(new ChannelView(
            c.Id,
            resolved.Label,
            c.Type == Channel.Direct ? null : Compact.NullIfEmpty(c.DisplayName),
            Compact.ChannelTypeName(c),
            Compact.Iso(c.LastPostAt),
            Compact.NullIfEmpty(c.Purpose),
            Compact.NullIfEmpty(c.Header)));
    }

    private static string ParseType(string type) => type switch
    {
        "public" => Channel.Public,
        "private" => Channel.Private,
        "direct" => Channel.Direct,
        "group" => Channel.Group,
        _ => throw new McpException("type must be one of: public, private, direct, group"),
    };

    private static string Truncate(string text) => text.Length <= PurposeLimit ? text : text[..PurposeLimit];
}
