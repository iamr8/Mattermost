using System.ComponentModel;
using System.Text.Json;
using MattermostMcp.Configuration;
using ModelContextProtocol.Server;

namespace MattermostMcp.Tools;

public sealed class WhoAmITool(ServerState state, MattermostOptions options)
{
    [McpServerTool(Name = "whoami", Title = "Who am I", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Shows which Mattermost user this server acts as, that user's teams, the channel allowlist in force, and the server host.")]
    public string WhoAmI() => JsonSerializer.Serialize(new
    {
        username = state.Me.Username,
        user_id = state.Me.Id,
        server = options.Host,
        allowlist = options.Allowlist.Entries,
        teams = state.Teams.Select(t => new { name = t.Name, display_name = t.DisplayName }),
    });
}
