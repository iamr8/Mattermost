using MattermostMcp.Mattermost;
using MattermostMcp.Resolution;

namespace MattermostMcp;

/// <summary>What the server learned about the user at startup. Singleton: stdio scopes every request.</summary>
public sealed class ServerState
{
    private Snapshot? _snapshot;

    public User Me => Current.Me;

    public IReadOnlyList<Team> Teams => Current.Teams;

    public AllowlistPolicy Policy => Current.Policy;

    public void Set(User me, IReadOnlyList<Team> teams, AllowlistPolicy policy) => _snapshot = new Snapshot(me, teams, policy);

    private Snapshot Current => _snapshot ?? throw new InvalidOperationException("Server state is not loaded yet.");

    private sealed record Snapshot(User Me, IReadOnlyList<Team> Teams, AllowlistPolicy Policy);
}
