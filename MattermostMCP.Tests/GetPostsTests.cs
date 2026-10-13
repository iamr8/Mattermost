using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class GetPostsTests
{
    private static string PostsPath(string channelId) => $"/api/v4/channels/{channelId}/posts";

    /// <summary>Three posts newest first; the map also holds an older thread root that is not in the page.</summary>
    private static string Page()
    {
        var p1 = Mm.Post(Ids.Post(1), Mm.T0 + 1000, Ids.Alice, Ids.DevGeneral, "first", replyCount: 2);
        var p2 = Mm.Post(Ids.Post(2), Mm.T0 + 2000, Ids.Bob, Ids.DevGeneral, "second", rootId: Ids.Post(0), editAt: Mm.T0 + 2500, fileIds: ["f1aaaaaaaaaaaaaaaaaaaaaaaa"]);
        var p3 = Mm.Post(Ids.Post(3), Mm.T0 + 3000, Ids.Alice, Ids.DevGeneral, "third");
        var root = Mm.Post(Ids.Post(0), Mm.T0, Ids.Bob, Ids.DevGeneral, "older root");
        return Mm.PostList([Ids.Post(3), Ids.Post(2), Ids.Post(1)], [p2, root, p1, p3], prevPostId: Ids.Post(0));
    }

    private static async Task<(TestHost Host, FakeMattermostHandler Handler)> StartAsync(string page, string allowlist = "*")
    {
        var handler = TestHost.StandardMattermost().RespondJson(HttpMethod.Get, PostsPath(Ids.DevGeneral), page);
        var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist));
        await host.StartAsync(TestContext.Current.CancellationToken);
        return (host, handler);
    }

    // Break: enumerate the posts map, use prev_post_id as the cursor, look users up one by one.
    [Fact]
    public async Task Posts_follow_order_shape_compactly_and_hand_back_the_oldest_id_as_cursor()
    {
        var (host, handler) = await StartAsync(Page());
        await using var _h = host;

        var (isError, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Equal("dev/general", root.GetProperty("channel").GetString());
        var posts = root.GetProperty("posts").EnumerateArray().ToArray();
        Assert.Equal([Ids.Post(3), Ids.Post(2), Ids.Post(1)], posts.Select(p => p.GetProperty("id").GetString()).ToArray());
        Assert.Equal(Ids.Post(1), root.GetProperty("next_before").GetString());
        Assert.Equal("alice", posts[0].GetProperty("user").GetString());
        Assert.Equal("2026-09-21T14:13:23Z", posts[0].GetProperty("at").GetString());
        Assert.Equal("third", posts[0].GetProperty("text").GetString());
        Assert.Equal(Ids.Post(0), posts[1].GetProperty("reply_to").GetString());
        Assert.True(posts[1].GetProperty("edited").GetBoolean());
        Assert.Equal(1, posts[1].GetProperty("files").GetInt32());
        Assert.Equal(2, posts[2].GetProperty("replies").GetInt32());
        Assert.False(posts[0].TryGetProperty("edited", out _)); // zero and false are omitted
        Assert.DoesNotContain("older root", text);
        Assert.Single(handler.Requests, r => r.Path == "/api/v4/users/ids");
    }

    [Fact]
    public async Task Request_carries_the_limit_and_skips_thread_roots_and_the_cursor_is_passed_on()
    {
        var (host, handler) = await StartAsync(Page());
        await using var _h = host;

        await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });
        await host.CallAsync("get_posts", new() { ["channel"] = "dev/general", ["limit"] = 10, ["before"] = Ids.Post(1) });

        var requests = handler.Requests.Where(r => r.Path == PostsPath(Ids.DevGeneral)).ToArray();
        Assert.Contains("per_page=30", requests[0].Query);
        Assert.Contains("skipFetchThreads=true", requests[0].Query);
        Assert.DoesNotContain("before=", requests[0].Query);
        Assert.Contains("per_page=10", requests[1].Query);
        Assert.Contains($"before={Ids.Post(1)}", requests[1].Query);
    }

    [Fact]
    public async Task Limit_below_one_is_refused_and_above_200_is_clamped()
    {
        var (host, handler) = await StartAsync(Page());
        await using var _h = host;

        var (zeroError, zero) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general", ["limit"] = 0 });
        await host.CallAsync("get_posts", new() { ["channel"] = "dev/general", ["limit"] = 500 });

        Assert.True(zeroError);
        Assert.Contains("limit", zero);
        var requests = handler.Requests.Where(r => r.Path == PostsPath(Ids.DevGeneral)).ToArray();
        Assert.Single(requests);
        Assert.Contains("per_page=200", requests[0].Query);
    }

    [Fact]
    public async Task Deleted_posts_are_dropped()
    {
        var live = Mm.Post(Ids.Post(1), Mm.T0 + 1000, Ids.Alice, Ids.DevGeneral, "alive");
        var gone = Mm.Post(Ids.Post(2), Mm.T0 + 2000, Ids.Alice, Ids.DevGeneral, "deleted text", deleteAt: Mm.T0 + 3000);
        var (host, _) = await StartAsync(Mm.PostList([Ids.Post(2), Ids.Post(1)], [live, gone]));
        await using var _h = host;

        var (_, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.Contains("alive", text);
        Assert.DoesNotContain("deleted text", text);
    }

    [Fact]
    public async Task An_empty_page_has_no_cursor()
    {
        var (host, _) = await StartAsync(Mm.PostList([], []));
        await using var _h = host;

        var (isError, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.False(json.RootElement.TryGetProperty("next_before", out _));
        Assert.Empty(json.RootElement.GetProperty("posts").EnumerateArray());
    }

    [Fact]
    public async Task A_before_cursor_that_is_not_a_post_id_is_refused_without_a_request()
    {
        var (host, handler) = await StartAsync(Page());
        await using var _h = host;

        var (isError, _) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general", ["before"] = "../etc" });

        Assert.True(isError);
        Assert.DoesNotContain(handler.Requests, r => r.Path == PostsPath(Ids.DevGeneral));
    }
}
