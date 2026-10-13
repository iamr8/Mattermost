namespace MattermostMcp;

public static class MattermostHttp
{
    /// <summary>Tool calls: full resilience pipeline.</summary>
    public const string ClientName = "mattermost";

    /// <summary>Startup checks: one attempt, no retries.</summary>
    public const string BootstrapClientName = "mattermost-bootstrap";

    /// <summary>Writes: one attempt, no retries, so a retry cannot double a post.</summary>
    public const string WriteClientName = "mattermost-write";

    /// <summary>File downloads: no retries and a longer timeout than the 30 s total of the read client.</summary>
    public const string DownloadClientName = "mattermost-download";
}
