using MattermostMcp.Mattermost;

namespace MattermostMcp.Resolution;

/// <summary>
/// The channels the user is in. A singleton: stdio scopes every request. It is refreshed when it is older
/// than a minute and on a lookup miss; one refresh runs at a time and concurrent callers share it.
/// </summary>
public sealed class ChannelMap(MattermostClient client, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private string? _userId;
    private Snapshot? _snapshot;
    private Task? _refresh;

    public IReadOnlyList<Channel> Channels => Current.Channels;

    public Channel? Find(string id) => Current.ById.GetValueOrDefault(id);

    /// <summary>Loads the first snapshot on the startup client (one attempt, no retries).</summary>
    public async Task InitializeAsync(MattermostClient bootstrapClient, string userId, CancellationToken cancellationToken)
    {
        _userId = userId;
        Store(await bootstrapClient.GetChannelsAsync(userId, cancellationToken));
    }

    public Task EnsureFreshAsync(CancellationToken cancellationToken)
        => time.GetUtcNow() - Current.At > MaxAge ? RefreshAsync(cancellationToken) : Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        Task shared;
        lock (_gate)
        {
            if (_refresh is null || _refresh.IsCompleted)
            {
                _refresh = LoadAsync();
            }

            shared = _refresh;
        }

        return shared.WaitAsync(cancellationToken);
    }

    // The shared load must not die with the first caller's cancellation; the client's own timeouts bound it.
    private async Task LoadAsync() => Store(await client.GetChannelsAsync(_userId!, CancellationToken.None));

    private void Store(List<Channel> channels)
        => _snapshot = new Snapshot(channels, channels.ToDictionary(c => c.Id), time.GetUtcNow());

    private Snapshot Current => _snapshot ?? throw new InvalidOperationException("Channel map is not loaded yet.");

    private sealed record Snapshot(IReadOnlyList<Channel> Channels, IReadOnlyDictionary<string, Channel> ById, DateTimeOffset At);
}
