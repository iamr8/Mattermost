using MattermostMcp.Configuration;
using MattermostMcp.Mattermost;

namespace MattermostMcp.Resolution;

/// <summary>
/// The allowlist resolved to ids. One predicate for every tool: <see cref="IsAllowed"/>.
/// Team entries match by team id at check time, so a channel joined later in that team is covered.
/// </summary>
public sealed class AllowlistPolicy
{
    private readonly bool _all;
    private readonly HashSet<string> _teamIds;
    private readonly HashSet<string> _channelIds;
    private readonly HashSet<string> _directNames;

    private AllowlistPolicy(bool all, HashSet<string> teamIds, HashSet<string> channelIds, HashSet<string> directNames, IReadOnlyList<string> searchTeamIds)
    {
        _all = all;
        _teamIds = teamIds;
        _channelIds = channelIds;
        _directNames = directNames;
        SearchTeamIds = searchTeamIds;
    }

    /// <summary>The teams whose search can reach allowed channels.</summary>
    public IReadOnlyList<string> SearchTeamIds { get; }

    public bool IsAllowed(Channel channel)
        => _all
           || (channel.TeamId != "" && _teamIds.Contains(channel.TeamId))
           || _channelIds.Contains(channel.Id)
           || (channel.Type == Channel.Direct && _directNames.Contains(channel.Name));

    /// <summary>Resolves every entry; the first one that matches nothing stops startup.</summary>
    public static AllowlistPolicy Create(
        Allowlist allowlist,
        IReadOnlyList<Team> teams,
        IReadOnlyList<Channel> channels,
        string meId,
        IReadOnlyDictionary<string, string> directUserIds)
    {
        if (allowlist.AllowsAll)
        {
            return new AllowlistPolicy(true, [], [], [], teams.Select(t => t.Id).ToArray());
        }

        var teamIds = new HashSet<string>();
        var channelIds = new HashSet<string>();
        var directNames = new HashSet<string>();
        var searchTeams = new List<string>();

        Team Team(string name, AllowlistEntry entry)
            => teams.FirstOrDefault(t => t.Name == name) ?? throw Unknown(entry);

        foreach (var entry in allowlist.Parsed)
        {
            switch (entry)
            {
                case TeamEntry team:
                    var teamMatch = Team(team.Team, entry);
                    teamIds.Add(teamMatch.Id);
                    searchTeams.Add(teamMatch.Id);
                    break;
                case ChannelEntry channel:
                    var owner = Team(channel.Team, entry);
                    var found = channels.FirstOrDefault(c => c.TeamId == owner.Id && c.Name == channel.Channel) ?? throw Unknown(entry);
                    channelIds.Add(found.Id);
                    searchTeams.Add(owner.Id);
                    break;
                case ChannelIdEntry id:
                    var byId = channels.FirstOrDefault(c => c.Id == id.Id) ?? throw Unknown(entry);
                    channelIds.Add(byId.Id);
                    if (byId.TeamId != "")
                    {
                        searchTeams.Add(byId.TeamId);
                    }

                    break;
                case DirectEntry direct:
                    directNames.Add(ChannelNames.DirectName(meId, directUserIds[direct.Username]));
                    break;
            }
        }

        // Direct and group posts show up in every team's search, so any team will do.
        if (searchTeams.Count == 0 && teams.Count > 0)
        {
            searchTeams.Add(teams[0].Id);
        }

        return new AllowlistPolicy(false, teamIds, channelIds, directNames, searchTeams.Distinct().ToArray());
    }

    private static StartupException Unknown(AllowlistEntry entry) => new($"unknown allowlist entry: {entry.Raw}");
}
