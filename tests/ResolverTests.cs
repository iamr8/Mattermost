using System.Net;
using MattermostMcp.Resolution;
using MattermostMcp.Tests.Fakes;
using ModelContextProtocol;

namespace MattermostMcp.Tests;

public class ResolverTests
{
    private static async Task<(TestHost Host, ChannelResolver Resolver)> StartAsync(string allowlist, FakeMattermostHandler? handler = null, ManualTimeProvider? time = null)
    {
        var host = TestHost.Create(handler ?? TestHost.StandardMattermost(), time ?? new ManualTimeProvider(), TestHost.DefaultOptions(allowlist));
        await host.BootstrapAsync(TestContext.Current.CancellationToken);
        return (host, host.Service<ChannelResolver>());
    }

    private static int ChannelListRequests(FakeMattermostHandler handler)
        => handler.Requests.Count(r => r.Path == $"/api/v4/users/{Ids.Alice}/channels");

    // Break: ignore the allowlist when a name matches, or list candidates the user may not see.
    [Fact]
    public async Task A_name_shared_by_two_teams_resolves_to_the_only_allowed_one()
    {
        var (host, resolver) = await StartAsync("dev, ops/secret");
        await using var _h = host;

        var resolved = await resolver.ResolveAsync("general", TestContext.Current.CancellationToken);

        Assert.Equal(Ids.DevGeneral, resolved.Channel.Id);
        Assert.Equal("dev/general", resolved.Label);
    }

    [Fact]
    public async Task A_name_shared_by_two_allowed_teams_is_refused_listing_both()
    {
        var (host, resolver) = await StartAsync("dev, ops");
        await using var _h = host;

        var ex = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("general", TestContext.Current.CancellationToken));

        Assert.Contains("dev/general", ex.Message);
        Assert.Contains("ops/general", ex.Message);
    }

    [Fact]
    public async Task Refusal_never_names_channels_outside_the_allowlist()
    {
        var (host, resolver) = await StartAsync("ops/secret");
        await using var _h = host;

        var ex = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("general", TestContext.Current.CancellationToken));

        Assert.Equal(Msg.Unavailable("general"), ex.Message);
    }

    // Break: check the allowlist after the fetch instead of before.
    [Fact]
    public async Task A_channel_outside_the_allowlist_is_refused_before_any_posts_request()
    {
        var handler = TestHost.StandardMattermost();
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_posts", new() { ["channel"] = "ops/general" });

        Assert.True(isError);
        Assert.EndsWith(Msg.Unavailable("ops/general"), text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/posts"));
    }

    [Fact]
    public async Task An_id_the_user_is_not_a_member_of_is_refused()
    {
        var (host, resolver) = await StartAsync("*");
        await using var _h = host;

        var ex = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("c9aaaaaaaaaaaaaaaaaaaaaaaa", TestContext.Current.CancellationToken));

        Assert.Equal(Msg.Unavailable("c9aaaaaaaaaaaaaaaaaaaaaaaa"), ex.Message);
    }

    // Break: refresh on every miss without a bound, or never refresh.
    [Fact]
    public async Task A_missing_name_triggers_exactly_one_refresh_then_succeeds_if_present()
    {
        var withNew = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "channels.json"))
            .Replace("\"name\": \"random\"", "\"name\": \"newchan\"");
        var handler = TestHost.StandardMattermost()
            .RespondSequence(
                HttpMethod.Get, $"/api/v4/users/{Ids.Alice}/channels",
                () => Mm.Response(HttpStatusCode.OK, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "channels.json"))),
                () => Mm.Response(HttpStatusCode.OK, withNew));
        var (host, resolver) = await StartAsync("*", handler);
        await using var _h = host;

        var resolved = await resolver.ResolveAsync("dev/newchan", TestContext.Current.CancellationToken);

        Assert.Equal(Ids.DevRandom, resolved.Channel.Id);
        Assert.Equal(2, ChannelListRequests(handler));
    }

    [Fact]
    public async Task A_name_that_never_appears_is_refused_after_one_refresh()
    {
        var handler = TestHost.StandardMattermost();
        var (host, resolver) = await StartAsync("*", handler);
        await using var _h = host;

        var ex = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("dev/ghost", TestContext.Current.CancellationToken));

        Assert.Equal(Msg.Unavailable("dev/ghost"), ex.Message);
        Assert.Equal(2, ChannelListRequests(handler));
    }

    // Break: no single-flight, so each concurrent caller sends its own refresh.
    [Fact]
    public async Task Concurrent_misses_share_one_refresh()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var withNew = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "channels.json"))
            .Replace("\"name\": \"random\"", "\"name\": \"newchan\"");
        var handler = TestHost.StandardMattermost();
        var (host, resolver) = await StartAsync("*", handler);
        await using var _h = host;
        handler.RespondWhenReleased(HttpMethod.Get, $"/api/v4/users/{Ids.Alice}/channels", withNew, arrived, gate);

        var first = resolver.ResolveAsync("dev/newchan", TestContext.Current.CancellationToken);
        await arrived.Task;
        var second = resolver.ResolveAsync("dev/newchan", TestContext.Current.CancellationToken);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, ChannelListRequests(handler)); // bootstrap + one shared refresh
    }

    // Break: match any DM that contains the name, or skip the lookup of the other user.
    [Fact]
    public async Task A_direct_channel_is_found_by_the_other_users_name()
    {
        var (host, resolver) = await StartAsync("*");
        await using var _h = host;

        var resolved = await resolver.ResolveAsync("@bob", TestContext.Current.CancellationToken);

        Assert.Equal(Ids.DmAliceBob, resolved.Channel.Id);
        Assert.Equal("@bob", resolved.Label);
    }

    [Fact]
    public async Task A_direct_channel_outside_the_allowlist_is_refused()
    {
        var handler = TestHost.StandardMattermost()
            .Respond(HttpMethod.Get, "/api/v4/users/username/carol", _ => Mm.Response(HttpStatusCode.OK, Mm.Json(new { id = Ids.Carol, username = "carol" })));
        var (host, resolver) = await StartAsync("@bob", handler);
        await using var _h = host;

        var ex = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("@carol", TestContext.Current.CancellationToken));

        Assert.Equal(Msg.Unavailable("@carol"), ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task A_direct_channel_that_does_not_exist_is_refused(HttpStatusCode lookup)
    {
        var handler = TestHost.StandardMattermost()
            .Respond(HttpMethod.Get, "/api/v4/users/username/erin", _ => lookup == HttpStatusCode.OK
                ? Mm.Response(HttpStatusCode.OK, Mm.Json(new { id = "u9eeeeeeeeeeeeeeeeeeeeeeee", username = "erin" }))
                : Mm.Response(lookup, "{}"));
        var (host, resolver) = await StartAsync("*", handler);
        await using var _h = host;

        var ex = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("@erin", TestContext.Current.CancellationToken));

        Assert.Equal(Msg.Unavailable("@erin"), ex.Message);
    }

    // Break: answer "not a member" and "not allowed" differently, so a guessed name reveals membership.
    [Fact]
    public async Task A_disallowed_channel_and_a_missing_one_are_refused_with_the_same_text()
    {
        var (host, resolver) = await StartAsync("dev");
        await using var _h = host;

        var disallowed = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("ops/general", TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<McpException>(() => resolver.ResolveAsync("ops/ghost", TestContext.Current.CancellationToken));

        Assert.Equal(Msg.Unavailable("ops/general"), disallowed.Message);
        Assert.Equal(Msg.Unavailable("ops/ghost"), missing.Message);
    }

    // Break: parse an entry kind but never grant it (denial-only tests would stay green).
    [Theory]
    [InlineData("dev", "dev/random")]
    [InlineData("ops/general", "ops/general")]
    [InlineData("@bob", "@bob")]
    [InlineData(Ids.GroupAll, Ids.GroupAll)]
    [InlineData(Ids.DevGeneral, Ids.DevGeneral)]
    public async Task Each_allowlist_entry_kind_grants_the_channel_it_names(string allowlist, string channel)
    {
        var (host, resolver) = await StartAsync(allowlist);
        await using var _h = host;

        var resolved = await resolver.ResolveAsync(channel, TestContext.Current.CancellationToken);

        Assert.NotNull(resolved.Channel);
    }

    // Break: refresh only on a miss, so a channel left in the map stays readable for ever.
    [Fact]
    public async Task A_map_older_than_a_minute_is_refreshed_before_use()
    {
        var handler = TestHost.StandardMattermost();
        var time = new ManualTimeProvider();
        var (host, resolver) = await StartAsync("*", handler, time);
        await using var _h = host;

        await resolver.ResolveAsync("dev/general", TestContext.Current.CancellationToken);
        Assert.Equal(1, ChannelListRequests(handler));

        time.Advance(TimeSpan.FromSeconds(61));
        await resolver.ResolveAsync("dev/general", TestContext.Current.CancellationToken);

        Assert.Equal(2, ChannelListRequests(handler));
    }
}
