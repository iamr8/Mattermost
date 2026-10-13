using MattermostMcp.Mattermost;

namespace MattermostMcp.Resolution;

/// <summary>Labels that the resolver accepts back: <c>team/name</c>, <c>@user</c> for a direct channel, the id for a group.</summary>
public sealed class ChannelLabeler(ServerState state, MattermostClient client)
{
    /// <summary>One user lookup for all direct channels in the batch; none if there are none.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LabelsAsync(IEnumerable<Channel> channels, CancellationToken cancellationToken)
    {
        var distinct = channels.DistinctBy(c => c.Id).ToList();
        var directIds = distinct
            .Where(c => c.Type == Channel.Direct)
            .Select(c => ChannelNames.OtherUserId(c, state.Me.Id))
            .OfType<string>()
            .Distinct()
            .ToList();
        var names = directIds.Count == 0
            ? new Dictionary<string, string>()
            : (await client.GetUsersByIdsAsync(directIds, cancellationToken)).ToDictionary(u => u.Id, u => u.Username);

        return distinct.ToDictionary(c => c.Id, c => Label(c, names));
    }

    public async Task<string> LabelAsync(Channel channel, CancellationToken cancellationToken)
        => (await LabelsAsync([channel], cancellationToken))[channel.Id];

    private string Label(Channel channel, Dictionary<string, string> usernames)
        => channel.Type switch
        {
            Channel.Direct => "@" + DirectPeer(channel, usernames),
            Channel.Group => channel.Id, // the only form a group channel resolves by; the members are in display_name
            _ => $"{TeamName(channel.TeamId)}/{channel.Name}",
        };

    private string DirectPeer(Channel channel, Dictionary<string, string> usernames)
    {
        var other = ChannelNames.OtherUserId(channel, state.Me.Id) ?? channel.Name;
        return usernames.GetValueOrDefault(other, other);
    }

    private string TeamName(string teamId) => state.Teams.FirstOrDefault(t => t.Id == teamId)?.Name ?? teamId;
}
