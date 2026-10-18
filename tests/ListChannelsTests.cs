using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class ListChannelsTests
{
    private static JsonElement[] Parse(string text) => JsonDocument.Parse(text).RootElement.EnumerateArray().ToArray();

    // Break: build the list from the whole map instead of the allowed channels.
    [Fact]
    public async Task Only_allowed_channels_are_listed_and_nothing_else_leaks()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost(), TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("list_channels");

        Assert.False(isError);
        Assert.Equal([Ids.DevGeneral, Ids.DevRandom], Parse(text).Select(c => c.GetProperty("id").GetString()).Order().ToArray());
        Assert.DoesNotContain("Hidden ops plans", text);
        Assert.DoesNotContain("ops", text);
        Assert.DoesNotContain("bob", text);
    }

    // Break: show the raw "id__id" name of a direct channel.
    [Fact]
    public async Task Direct_and_group_channels_are_labelled_with_people_and_have_no_team()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost(), TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (_, text) = await host.CallAsync("list_channels");

        var byId = Parse(text).ToDictionary(c => c.GetProperty("id").GetString()!);
        Assert.Equal("@bob", byId[Ids.DmAliceBob].GetProperty("name").GetString());
        Assert.Equal("direct", byId[Ids.DmAliceBob].GetProperty("type").GetString());
        Assert.Equal("@carol", byId[Ids.DmAliceCarol].GetProperty("name").GetString());
        Assert.Equal("group", byId[Ids.GroupAll].GetProperty("type").GetString());
        Assert.Equal(Ids.GroupAll, byId[Ids.GroupAll].GetProperty("name").GetString()); // the id is the only form a group resolves by
        Assert.Equal("alice, bob, carol", byId[Ids.GroupAll].GetProperty("display_name").GetString());
        Assert.Equal("dev/general", byId[Ids.DevGeneral].GetProperty("name").GetString());
        Assert.Equal("private", byId[Ids.OpsSecret].GetProperty("type").GetString());
        Assert.DoesNotContain("__", text);
    }

    [Fact]
    public async Task Type_and_team_filters_narrow_the_list()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost(), TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (_, direct) = await host.CallAsync("list_channels", new() { ["type"] = "direct" });
        var (_, ops) = await host.CallAsync("list_channels", new() { ["team"] = "ops" });
        var (isError, _) = await host.CallAsync("list_channels", new() { ["type"] = "bogus" });

        Assert.Equal([Ids.DmAliceBob, Ids.DmAliceCarol], Parse(direct).Select(c => c.GetProperty("id").GetString()).Order().ToArray());
        Assert.Equal([Ids.OpsGeneral, Ids.OpsSecret], Parse(ops).Select(c => c.GetProperty("id").GetString()).Order().ToArray());
        Assert.True(isError);
    }

    // Break: fetch but discard the refresh, or serve the startup snapshot, so a channel joined later never shows up.
    [Fact]
    public async Task Each_call_lists_the_refreshed_map_not_the_startup_snapshot()
    {
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "channels.json"));
        var handler = TestHost.StandardMattermost()
            .RespondSequence(
                HttpMethod.Get, $"/api/v4/users/{Ids.Alice}/channels",
                () => Mm.Response(HttpStatusCode.OK, original),
                () => Mm.Response(HttpStatusCode.OK, original.Replace("\"name\": \"random\"", "\"name\": \"fresh\"")));
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (_, text) = await host.CallAsync("list_channels");

        Assert.Contains("dev/fresh", text);
        Assert.DoesNotContain("dev/random", text);
    }

    // Break: label a channel with something the resolver does not accept (a group's member list, a raw DM name).
    [Fact]
    public async Task Every_name_list_channels_shows_resolves_in_get_channel()
    {
        var handler = TestHost.StandardMattermost()
            .Respond(HttpMethod.Get, "/api/v4/users/username/carol", _ => Mm.Response(HttpStatusCode.OK, Mm.Json(new { id = Ids.Carol, username = "carol" })));
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);
        var (_, list) = await host.CallAsync("list_channels");

        var names = Parse(list).Select(c => c.GetProperty("name").GetString()!).ToArray();

        Assert.Equal(7, names.Length);
        foreach (var name in names)
        {
            var (isError, text) = await host.CallAsync("get_channel", new() { ["channel"] = name });
            Assert.False(isError, $"{name}: {text}");
        }
    }

    [Fact]
    public async Task Purpose_is_cut_to_200_chars_and_the_list_is_newest_first()
    {
        var long200 = new string('x', 300);
        var channels = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "channels.json")).Replace("Dev talk", long200);
        var handler = TestHost.StandardMattermost().RespondJson(HttpMethod.Get, $"/api/v4/users/{Ids.Alice}/channels", channels);
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (_, text) = await host.CallAsync("list_channels");

        var rows = Parse(text);
        Assert.Equal(200, rows[0].GetProperty("purpose").GetString()!.Length);
        Assert.Equal(Ids.DevGeneral, rows[0].GetProperty("id").GetString()); // last_post_at 1790000000000 > 1789000000000
    }

    [Fact]
    public async Task Get_channel_returns_full_header_and_purpose_for_an_allowed_channel_only()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost(), TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (okError, ok) = await host.CallAsync("get_channel", new() { ["channel"] = "dev/general" });
        var (refusedError, refused) = await host.CallAsync("get_channel", new() { ["channel"] = "ops/secret" });

        Assert.False(okError);
        using var json = JsonDocument.Parse(ok);
        Assert.Equal("Dev header", json.RootElement.GetProperty("header").GetString());
        Assert.Equal("Dev talk", json.RootElement.GetProperty("purpose").GetString());
        Assert.True(refusedError);
        Assert.EndsWith(Msg.Unavailable("ops/secret"), refused); // the SDK prefixes the tool name
    }
}
