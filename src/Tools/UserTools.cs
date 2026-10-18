using System.ComponentModel;
using MattermostMcp.Mattermost;
using MattermostMcp.Output;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MattermostMcp.Tools;

public sealed class UserTools(MattermostClient client)
{
    [McpServerTool(Name = "get_user", Title = "Get user", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Shows a user's name and position. Email addresses are never returned.")]
    public async Task<string> GetUser(
        [Description("A username (with or without @) or a user id.")] string user,
        CancellationToken cancellationToken = default)
    {
        var key = (user ?? "").Trim().TrimStart('@');
        UserProfile profile;
        if (Patterns.Id.IsMatch(key))
        {
            profile = await client.GetUserAsync(key, cancellationToken);
        }
        else if (Patterns.Username.IsMatch(key))
        {
            profile = await client.GetUserByUsernameAsync(key, cancellationToken);
        }
        else
        {
            throw new McpException("invalid user");
        }

        return Compact.Serialize(new UserView(
            profile.Id,
            profile.Username,
            Compact.NullIfEmpty(profile.Nickname),
            Compact.NullIfEmpty(profile.FirstName),
            Compact.NullIfEmpty(profile.LastName),
            Compact.NullIfEmpty(profile.Position)));
    }
}
