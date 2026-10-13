using MattermostMcp.Tests.Fakes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MattermostMcp.Tests;

/// <summary>The real server process over stdio against a stub HTTP server. No real Mattermost is involved.</summary>
public class SubprocessFileTests
{
    // Break: forget to register the download client or the file store in the real host.
    [Fact(Timeout = 90000)]
    public async Task A_file_is_listed_and_downloaded_end_to_end_in_the_real_process()
    {
        var token = TestContext.Current.CancellationToken;
        var saved = Path.Combine(Path.GetTempPath(), "mattermost-mcp", Ids.File + ".txt");
        if (File.Exists(saved))
        {
            File.Delete(saved);
        }

        using var stub = new StubMattermostServer();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "MattermostMCP.dll")],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["MATTERMOST_URL"] = stub.Url,
                ["MATTERMOST_TOKEN"] = TestHost.Token,
                ["MATTERMOST_ALLOWLIST"] = "dev",
                ["MATTERMOST_READ_ONLY"] = null,
            },
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: token);

        try
        {
            var list = await client.CallToolAsync("get_post_files", new Dictionary<string, object?> { ["post_id"] = Ids.Post(2) }, cancellationToken: token);
            Assert.NotEqual(true, list.IsError);
            Assert.Contains(Ids.File, string.Concat(list.Content.OfType<TextContentBlock>().Select(b => b.Text)));

            var download = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["file_id"] = Ids.File }, cancellationToken: token);
            Assert.NotEqual(true, download.IsError);
            Assert.Contains(saved, string.Concat(download.Content.OfType<TextContentBlock>().Select(b => b.Text)).Replace("\\/", "/"));
            Assert.Equal("hello", await File.ReadAllTextAsync(saved, token));
            Assert.All(stub.Requests.Where(r => r.Path.StartsWith("/api/v4/files/")), r => Assert.Equal("Bearer " + TestHost.Token, r.Authorization));
        }
        finally
        {
            if (File.Exists(saved))
            {
                File.Delete(saved);
            }
        }
    }
}
