using MattermostMcp.Configuration;
using MattermostMcp.Mattermost;
using MattermostMcp.Resolution;
using Microsoft.Extensions.Logging;

namespace MattermostMcp;

/// <summary>
/// Startup checks, one attempt each under one budget, on a client without the retry pipeline.
/// Fails fast so Claude Code shows a broken server at once instead of on the first tool call.
/// </summary>
public sealed class Bootstrapper(
    IHttpClientFactory factory,
    MattermostOptions options,
    ServerState state,
    ChannelMap map,
    ILogger<Bootstrapper> logger)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        var client = new MattermostClient(factory, MattermostHttp.BootstrapClientName);

        try
        {
            logger.LogInformation("Connecting to {Host}", options.Host);
            var me = await client.GetMeAsync(budget.Token);
            var teams = await client.GetTeamsAsync(me.Id, budget.Token);
            await map.InitializeAsync(client, me.Id, budget.Token);
            var directIds = await ResolveDirectUsersAsync(client, budget.Token);

            var policy = AllowlistPolicy.Create(options.Allowlist, teams, map.Channels, me.Id, directIds);
            state.Set(me, teams, policy);
            logger.LogInformation("Connected as {Username} ({TeamCount} teams, {ChannelCount} channels)", me.Username, teams.Count, map.Channels.Count);
        }
        catch (MattermostException ex)
        {
            throw new StartupException(ex.Kind switch
            {
                MattermostErrorKind.Unauthorized or MattermostErrorKind.Forbidden => $"token rejected by {options.Host}",
                MattermostErrorKind.Unreachable or MattermostErrorKind.Timeout => $"cannot reach {options.Host}",
                _ => ex.Message,
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new StartupException($"cannot reach {options.Host}");
        }
    }

    private async Task<Dictionary<string, string>> ResolveDirectUsersAsync(MattermostClient client, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, string>();
        foreach (var entry in options.Allowlist.Parsed.OfType<DirectEntry>())
        {
            try
            {
                ids[entry.Username] = (await client.GetUserByUsernameAsync(entry.Username, cancellationToken)).Id;
            }
            catch (MattermostException ex) when (ex.Kind == MattermostErrorKind.NotFound)
            {
                throw new StartupException($"unknown allowlist entry: {entry.Raw}");
            }
        }

        return ids;
    }
}
