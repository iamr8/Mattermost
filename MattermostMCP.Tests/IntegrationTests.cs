using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MattermostMcp.Tests;

/// <summary>
/// Opt-in smoke test against a real server, through the real process: needs MATTERMOST_INTEGRATION=1 and the
/// three MATTERMOST_* variables in the environment. It checks shape and identity only; live content changes.
/// </summary>
public class IntegrationTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("MATTERMOST_INTEGRATION") == "1";

    [Fact(SkipUnless = nameof(Enabled), SkipType = typeof(IntegrationTests), Skip = "Set MATTERMOST_INTEGRATION=1 and the MATTERMOST_* variables to run.")]
    public async Task The_built_server_starts_from_the_environment_and_answers_whoami_and_list_channels()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "mattermost",
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "MattermostMCP.dll")],
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

        var whoami = await client.CallToolAsync("whoami", cancellationToken: TestContext.Current.CancellationToken);
        var channels = await client.CallToolAsync("list_channels", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, whoami.IsError);
        using var who = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(whoami.Content)).Text);
        var username = who.RootElement.GetProperty("username").GetString();
        Assert.False(string.IsNullOrEmpty(username));
        if (Environment.GetEnvironmentVariable("MATTERMOST_TEST_USERNAME") is { Length: > 0 } expected)
        {
            Assert.Equal(expected, username);
        }

        Assert.NotEqual(true, channels.IsError);
        using var list = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(channels.Content)).Text);
        Assert.Equal(JsonValueKind.Array, list.RootElement.ValueKind);
    }
}
