using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;
using ModelContextProtocol.Protocol;

namespace MattermostMcp.Tests;

public class WhoAmITests
{
    private static FakeMattermostHandler Mattermost()
        => new FakeMattermostHandler()
            .RespondWithFixture(HttpMethod.Get, "/api/v4/users/me", "me.json")
            .RespondWithFixture(HttpMethod.Get, "/api/v4/users/u1aaaaaaaaaaaaaaaaaaaaaaaa/teams", "teams.json")
            .RespondWithFixture(HttpMethod.Get, "/api/v4/users/u1aaaaaaaaaaaaaaaaaaaaaaaa/channels", "channels.json");

    // Break: forget an annotation, so the SDK default (destructive, open world) applies.
    [Fact]
    public async Task Tools_list_has_whoami_marked_read_only()
    {
        await using var host = TestHost.Create(Mattermost());
        await host.BootstrapAsync(TestContext.Current.CancellationToken);
        var client = await host.ConnectAsync(TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var whoami = Assert.Single(tools, t => t.Name == "whoami");
        var hints = whoami.ProtocolTool.Annotations;
        Assert.NotNull(hints);
        Assert.True(hints.ReadOnlyHint);
        Assert.False(hints.DestructiveHint);
        Assert.True(hints.IdempotentHint);
        Assert.False(hints.OpenWorldHint);
    }

    // Break: call the wrong endpoint, or drop the teams call.
    [Fact]
    public async Task Whoami_returns_user_teams_allowlist_and_host()
    {
        await using var host = TestHost.Create(Mattermost(), TestHost.DefaultOptions(allowlist: "dev, ops/general"));
        await host.BootstrapAsync(TestContext.Current.CancellationToken);
        var client = await host.ConnectAsync(TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("whoami", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var root = json.RootElement;
        Assert.Equal("alice", root.GetProperty("username").GetString());
        Assert.Equal("u1aaaaaaaaaaaaaaaaaaaaaaaa", root.GetProperty("user_id").GetString());
        Assert.Equal("chat.example.com", root.GetProperty("server").GetString());
        Assert.Equal(["dev", "ops/general"], root.GetProperty("allowlist").EnumerateArray().Select(e => e.GetString()).ToArray());
        var teams = root.GetProperty("teams").EnumerateArray()
            .Select(t => (t.GetProperty("name").GetString(), t.GetProperty("display_name").GetString())).ToArray();
        Assert.Equal([("dev", "Dev Team"), ("ops", "Operations")], teams);
        Assert.DoesNotContain(TestHost.Token, Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    // Break: use the wrong base path, so requests go to /api/v4/api/v4/users/me.
    [Fact]
    public async Task Requests_use_the_api_base_and_carry_the_bearer_token()
    {
        var handler = Mattermost();
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(url: "https://chat.example.com/api/v4/"));

        await host.BootstrapAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["/api/v4/users/me", "/api/v4/users/u1aaaaaaaaaaaaaaaaaaaaaaaa/teams", "/api/v4/users/u1aaaaaaaaaaaaaaaaaaaaaaaa/channels"],
            handler.Requests.Select(r => r.Path).ToArray());
        Assert.All(handler.Requests, r => Assert.Equal($"Bearer {TestHost.Token}", r.Authorization));
    }
}
