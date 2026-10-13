using System.Net;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class WriteBudgetTests
{
    private const string TooMany = "too many writes, try again later";

    private static FakeMattermostHandler Mattermost()
        => TestHost.StandardMattermost()
            .RespondCreatePost()
            .RespondGetPost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "nice"))
            .Respond(HttpMethod.Post, "/api/v4/reactions", request => Mm.Response(HttpStatusCode.OK, request.Body!))
            .RespondJson(HttpMethod.Get, $"/api/v4/channels/{Ids.DevGeneral}/posts", Mm.PostList([], []));

    private static Task<(bool IsError, string Text)> Post(TestHost host, string message = "hi")
        => host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = message });

    // Break: no budget, a limit other than 20, or a window other than 10 minutes.
    [Fact]
    public async Task The_21st_write_in_ten_minutes_is_refused_and_one_passes_when_the_window_moves()
    {
        var time = new ManualTimeProvider();
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler, time: time);

        for (var i = 0; i < 20; i++)
        {
            Assert.False((await Post(host)).IsError, $"write {i + 1}");
        }

        var (isError, text) = await Post(host);
        Assert.True(isError);
        Assert.EndsWith(TooMany, text);
        Assert.Equal(20, handler.Writes().Count);

        time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        Assert.True((await Post(host)).IsError);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False((await Post(host)).IsError);
        Assert.Equal(21, handler.Writes().Count);
    }

    // Break: count each tool on its own, so the model gets 20 posts, 20 reactions and 20 edits.
    [Fact]
    public async Task All_write_tools_share_one_budget()
    {
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler);
        for (var i = 0; i < 10; i++)
        {
            await Post(host);
            await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = "thumbsup" });
        }

        var (isError, text) = await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = "thumbsup" });

        Assert.True(isError);
        Assert.EndsWith(TooMany, text);
        Assert.Equal(20, handler.Writes().Count);
    }

    // Break: take a unit of budget at the start of the call, before the checks that can refuse it.
    [Fact]
    public async Task Refused_calls_do_not_use_budget()
    {
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler);
        for (var i = 0; i < 30; i++)
        {
            await Post(host, message: "");
            await host.CallAsync("post_message", new() { ["channel"] = "ops/secret", ["message"] = "hi" });
            await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = "Bad Name" });
        }

        for (var i = 0; i < 20; i++)
        {
            Assert.False((await Post(host)).IsError, $"write {i + 1}");
        }
    }

    // Break: put the budget in front of reads too.
    [Fact]
    public async Task Reads_are_never_limited()
    {
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler);
        for (var i = 0; i < 20; i++)
        {
            await Post(host);
        }

        var (isError, _) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.False(isError);
    }
}
