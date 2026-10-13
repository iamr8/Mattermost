using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class SearchPostsTests
{
    private static string SearchPath(string teamId) => $"/api/v4/teams/{teamId}/posts/search";

    private static object Hit(int n, string channelId, string text = "hit") => Mm.Post(Ids.Post(n), Mm.T0 + n * 1000, Ids.Bob, channelId, $"{text} {n}");

    private static string Results(params object[] hits)
        => Mm.PostList(hits.Select(h => (string)h.GetType().GetProperty("id")!.GetValue(h)!).ToArray(), hits);

    private static async Task<(TestHost Host, FakeMattermostHandler Handler)> StartAsync(string allowlist, string? devResults = null, string? opsResults = null)
    {
        var handler = TestHost.StandardMattermost()
            .RespondJson(HttpMethod.Post, SearchPath(Ids.TeamDev), devResults ?? Results())
            .RespondJson(HttpMethod.Post, SearchPath(Ids.TeamOps), opsResults ?? Results());
        var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist));
        await host.StartAsync(TestContext.Current.CancellationToken);
        return (host, handler);
    }

    private static JsonElement Body(RecordedRequest request) => JsonDocument.Parse(request.Body!).RootElement;

    // Break: trust the in: filter and skip the post-filter, so a DM hit from a team search leaks.
    [Fact]
    public async Task Hits_from_channels_outside_the_allowlist_are_dropped_without_a_trace()
    {
        var (host, handler) = await StartAsync("dev", devResults: Results(Hit(1, Ids.DevGeneral), Hit(2, Ids.DmAliceBob, "private chat")));
        await using var _h = host;

        var (isError, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit" });

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.Equal([Ids.Post(1)], json.RootElement.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray());
        Assert.DoesNotContain("dropped", text); // a count would tell the model that hidden matches exist
        Assert.DoesNotContain("private chat", text);
        Assert.Single(handler.Requests, r => r.Path == SearchPath(Ids.TeamDev));
        Assert.DoesNotContain(handler.Requests, r => r.Path == SearchPath(Ids.TeamOps));
    }

    // Break: return the first team's hits, or keep the duplicate DM post.
    [Fact]
    public async Task Hits_from_several_teams_are_merged_deduplicated_newest_first_and_cut_to_the_limit()
    {
        var dm = Hit(9, Ids.DmAliceBob, "shared dm");
        var (host, handler) = await StartAsync(
            "*",
            devResults: Results(Hit(1, Ids.DevGeneral), Hit(5, Ids.DevGeneral), dm),
            opsResults: Results(Hit(3, Ids.OpsGeneral), Hit(7, Ids.OpsGeneral), dm));
        await using var _h = host;

        var (_, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit", ["limit"] = 3 });

        using var json = JsonDocument.Parse(text);
        Assert.Equal([Ids.Post(9), Ids.Post(7), Ids.Post(5)], json.RootElement.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray());
        var searches = handler.Requests.Where(r => r.Path.EndsWith("/posts/search")).ToArray();
        Assert.Equal(2, searches.Length);
        Assert.All(searches, s => Assert.Equal(3, Body(s).GetProperty("per_page").GetInt32()));
    }

    [Fact]
    public async Task Each_hit_names_its_channel_by_label()
    {
        var (host, _) = await StartAsync("*", devResults: Results(Hit(1, Ids.DevGeneral), Hit(2, Ids.DmAliceBob)));
        await using var _h = host;

        var (_, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit" });

        using var json = JsonDocument.Parse(text);
        var labels = json.RootElement.GetProperty("posts").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetString()!, p => p.GetProperty("channel").GetString());
        Assert.Equal("dev/general", labels[Ids.Post(1)]);
        Assert.Equal("@bob", labels[Ids.Post(2)]);
    }

    // Break: build in: from the raw argument, or let another channel's hit through.
    [Fact]
    public async Task A_channel_argument_scopes_the_terms_and_drops_hits_from_other_channels()
    {
        var (host, handler) = await StartAsync("*", devResults: Results(Hit(1, Ids.DevGeneral), Hit(2, Ids.DevRandom)));
        await using var _h = host;

        var (_, text) = await host.CallAsync("search_posts", new() { ["query"] = "deploy", ["channel"] = "dev/general", ["from"] = "bob", ["after_date"] = "2026-01-02", ["before_date"] = "2026-02-03", ["on_date"] = "2026-01-05" });

        var search = Assert.Single(handler.Requests, r => r.Path.EndsWith("/posts/search"));
        Assert.Equal(SearchPath(Ids.TeamDev), search.Path);
        Assert.Equal("deploy from:bob after:2026-01-02 before:2026-02-03 on:2026-01-05 in:general", Body(search).GetProperty("terms").GetString());
        Assert.False(Body(search).GetProperty("is_or_search").GetBoolean());
        using var json = JsonDocument.Parse(text);
        Assert.Equal([Ids.Post(1)], json.RootElement.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray());
    }

    [Theory]
    [InlineData("deploy in:secret", null, null)]
    [InlineData("deploy -in:random", null, null)]
    [InlineData("\"deploy\"in:secret", null, null)]
    [InlineData("deploy-in:secret", null, null)]
    [InlineData("deploy", "after_date", "2026-13-01")]
    [InlineData("deploy", "on_date", "yesterday")]
    [InlineData("deploy", "from", "bob in:x")]
    [InlineData("   ", null, null)]
    public async Task Bad_input_is_refused_before_any_search_request(string query, string? key, string? value)
    {
        var (host, handler) = await StartAsync("*");
        await using var _h = host;
        var args = new Dictionary<string, object?> { ["query"] = query };
        if (key is not null)
        {
            args[key] = value;
        }

        var (isError, _) = await host.CallAsync("search_posts", args);

        Assert.True(isError);
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/posts/search"));
    }

    // Break: a too-greedy in: check that refuses ordinary words like "plugin:".
    [Fact]
    public async Task A_word_that_merely_ends_in_in_is_searched()
    {
        var (host, handler) = await StartAsync("*");
        await using var _h = host;

        var (isError, _) = await host.CallAsync("search_posts", new() { ["query"] = "plugin:foo" });

        Assert.False(isError);
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("/posts/search"));
    }

    [Fact]
    public async Task A_direct_channel_argument_is_searched_by_its_raw_name_and_only_its_hits_are_kept()
    {
        var (host, handler) = await StartAsync("*", devResults: Results(Hit(1, Ids.DmAliceBob), Hit(2, Ids.DmAliceCarol)));
        await using var _h = host;

        var (_, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit", ["channel"] = "@bob" });

        var search = Assert.Single(handler.Requests, r => r.Path.EndsWith("/posts/search"));
        Assert.EndsWith($" in:{Ids.Alice}__{Ids.Bob}", Body(search).GetProperty("terms").GetString());
        using var json = JsonDocument.Parse(text);
        Assert.Equal([Ids.Post(1)], json.RootElement.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray());
    }

    // Break: search every team, ignoring the team named by a channel-only allowlist.
    [Fact]
    public async Task A_channel_only_allowlist_searches_only_that_channels_team()
    {
        var (host, handler) = await StartAsync("ops/general", opsResults: Results(Hit(1, Ids.OpsGeneral), Hit(2, Ids.OpsSecret)));
        await using var _h = host;

        var (_, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit" });

        var search = Assert.Single(handler.Requests, r => r.Path.EndsWith("/posts/search"));
        Assert.Equal(SearchPath(Ids.TeamOps), search.Path);
        using var json = JsonDocument.Parse(text);
        Assert.Equal([Ids.Post(1)], json.RootElement.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray());
        Assert.DoesNotContain("dropped", text);
    }

    // Break: let search bypass the resolver.
    [Fact]
    public async Task A_channel_outside_the_allowlist_is_refused_without_a_search_request()
    {
        var (host, handler) = await StartAsync("dev");
        await using var _h = host;

        var (isError, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit", ["channel"] = "ops/general" });

        Assert.True(isError);
        Assert.EndsWith(Msg.Unavailable("ops/general"), text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/posts/search"));
    }

    [Fact]
    public async Task An_allowlist_of_only_direct_channels_still_finds_a_team_to_search_through()
    {
        var (host, handler) = await StartAsync("@bob", devResults: Results(Hit(1, Ids.DmAliceBob), Hit(2, Ids.DmAliceCarol)));
        await using var _h = host;

        var (_, text) = await host.CallAsync("search_posts", new() { ["query"] = "hit" });

        Assert.Single(handler.Requests, r => r.Path.EndsWith("/posts/search"));
        using var json = JsonDocument.Parse(text);
        Assert.Equal([Ids.Post(1)], json.RootElement.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray());
    }
}
