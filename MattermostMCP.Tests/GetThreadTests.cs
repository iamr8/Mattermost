using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class GetThreadTests
{
    private const string Sentinel = "SENTINEL-body-7731";

    private static string PostPath(string id) => $"/api/v4/posts/{id}";

    // Break: fetch the thread before checking the channel, or echo the post into the refusal.
    [Fact]
    public async Task A_post_in_a_refused_channel_returns_only_the_fixed_refusal()
    {
        var secret = Mm.Post(Ids.Post(5), Mm.T0, Ids.Bob, Ids.OpsSecret, Sentinel);
        var handler = TestHost.StandardMattermost()
            .RespondJson(HttpMethod.Get, PostPath(Ids.Post(5)), Mm.Json(secret));
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_thread", new() { ["post_id"] = Ids.Post(5) });

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
        Assert.DoesNotContain(Ids.OpsSecret, text); // the channel id came from the server, not from the caller
        Assert.DoesNotContain(Sentinel, text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/thread"));
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains(Sentinel));
    }

    private static (FakeMattermostHandler Handler, string Thread) ThreeReplies(string replyChannel = "")
    {
        var root = Mm.Post(Ids.Post(1), Mm.T0, Ids.Alice, Ids.DevGeneral, "root", replyCount: 3);
        object Reply(int n, string user, string channel) => Mm.Post(Ids.Post(n), Mm.T0 + n * 1000, user, channel, $"reply {n}", rootId: Ids.Post(1));
        var channel2 = replyChannel == "" ? Ids.DevGeneral : replyChannel;
        var thread = Mm.PostList(
            [Ids.Post(4), Ids.Post(3), Ids.Post(2), Ids.Post(1)],
            [Reply(3, Ids.Bob, Ids.DevGeneral), root, Reply(4, Ids.Alice, channel2), Reply(2, Ids.Bob, Ids.DevGeneral)]);
        var handler = TestHost.StandardMattermost()
            .RespondJson(HttpMethod.Get, PostPath(Ids.Post(2)), Mm.Json(Reply(2, Ids.Bob, Ids.DevGeneral)))
            .RespondJson(HttpMethod.Get, PostPath(Ids.Post(2)) + "/thread", thread);
        return (handler, thread);
    }

    [Fact]
    public async Task Thread_returns_root_then_the_oldest_replies_and_flags_truncation()
    {
        var (handler, _) = ThreeReplies();
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_thread", new() { ["post_id"] = Ids.Post(2), ["limit"] = 2 });

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("root", json.RootElement.GetProperty("root").GetProperty("text").GetString());
        Assert.Equal(["reply 2", "reply 3"], json.RootElement.GetProperty("replies").EnumerateArray().Select(r => r.GetProperty("text").GetString()).ToArray());
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("dev/general", json.RootElement.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task A_reply_from_another_channel_refuses_the_whole_thread()
    {
        var (handler, _) = ThreeReplies(replyChannel: Ids.OpsSecret);
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_thread", new() { ["post_id"] = Ids.Post(2) });

        Assert.True(isError);
        Assert.DoesNotContain("reply 4", text);
    }

    // Break: tell "no such post" apart from "post in a refused channel".
    [Fact]
    public async Task A_missing_post_is_refused_with_the_same_text_as_a_refused_one()
    {
        var handler = TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, PostPath(Ids.Post(6)), System.Net.HttpStatusCode.NotFound);
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_thread", new() { ["post_id"] = Ids.Post(6) });

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
    }

    // Break: First() on a root that the shaper dropped as deleted.
    [Fact]
    public async Task A_deleted_root_is_left_out_and_the_replies_still_come_back()
    {
        var root = Mm.Post(Ids.Post(1), Mm.T0, Ids.Alice, Ids.DevGeneral, "deleted root", deleteAt: Mm.T0 + 5000);
        var reply = Mm.Post(Ids.Post(2), Mm.T0 + 1000, Ids.Bob, Ids.DevGeneral, "still here", rootId: Ids.Post(1));
        var handler = TestHost.StandardMattermost()
            .RespondJson(HttpMethod.Get, PostPath(Ids.Post(2)), Mm.Json(reply))
            .RespondJson(HttpMethod.Get, PostPath(Ids.Post(2)) + "/thread", Mm.PostList([Ids.Post(2), Ids.Post(1)], [reply, root]));
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_thread", new() { ["post_id"] = Ids.Post(2) });

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.False(json.RootElement.TryGetProperty("root", out _));
        Assert.Equal(["still here"], json.RootElement.GetProperty("replies").EnumerateArray().Select(r => r.GetProperty("text").GetString()).ToArray());
        Assert.DoesNotContain("deleted root", text);
    }

    [Fact]
    public async Task A_post_id_that_is_not_26_lowercase_chars_is_refused_without_a_request()
    {
        var handler = TestHost.StandardMattermost();
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, _) = await host.CallAsync("get_thread", new() { ["post_id"] = "../users/me" });

        Assert.True(isError);
        Assert.DoesNotContain(handler.Requests, r => r.Path.Contains("/posts/"));
    }
}
