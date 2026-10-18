using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class ReplyToThreadTests
{
    private static Dictionary<string, object?> Args(int post, string message = "thanks") => new() { ["post_id"] = Ids.Post(post), ["message"] = message };

    // Break: use the id of the reply instead of the thread root, which Mattermost rejects or nests wrongly.
    [Fact]
    public async Task A_reply_to_a_reply_goes_under_the_thread_root()
    {
        var handler = TestHost.StandardMattermost()
            .RespondGetPost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "a reply", rootId: Ids.Post(1)))
            .RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("reply_to_thread", Args(2));

        Assert.False(isError);
        var write = Assert.Single(handler.Writes());
        using var body = JsonDocument.Parse(write.Body!);
        Assert.Equal(Ids.Post(1), body.RootElement.GetProperty("root_id").GetString());
        Assert.Equal(Ids.DevGeneral, body.RootElement.GetProperty("channel_id").GetString());
        Assert.Equal("thanks", body.RootElement.GetProperty("message").GetString());
        using var result = JsonDocument.Parse(text);
        Assert.Equal("dev/general", result.RootElement.GetProperty("channel").GetString());
        Assert.Equal(Ids.Post(90), result.RootElement.GetProperty("id").GetString());
    }

    // Break: use the root of an empty root_id, so a reply to a root post has no root.
    [Fact]
    public async Task A_reply_to_a_root_post_uses_that_post_as_the_root()
    {
        var handler = TestHost.StandardMattermost()
            .RespondGetPost(Mm.Post(Ids.Post(1), Mm.T0, Ids.Bob, Ids.DevGeneral, "a root"))
            .RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        await host.CallAsync("reply_to_thread", Args(1));

        using var body = JsonDocument.Parse(Assert.Single(handler.Writes()).Body!);
        Assert.Equal(Ids.Post(1), body.RootElement.GetProperty("root_id").GetString());
    }

    // Break: reply first and check the channel later, or let a refusal show the channel or the missing post.
    [Fact]
    public async Task A_post_in_a_refused_channel_and_a_missing_post_get_the_same_text_and_nothing_is_sent()
    {
        var handler = TestHost.StandardMattermost()
            .RespondGetPost(Mm.Post(Ids.Post(5), Mm.T0, Ids.Bob, Ids.OpsSecret, "secret"))
            .RespondWithStatus(HttpMethod.Get, WriteFakes.PostPath(Ids.Post(6)), HttpStatusCode.NotFound)
            .RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (refusedError, refused) = await host.CallAsync("reply_to_thread", Args(5));
        var (missingError, missing) = await host.CallAsync("reply_to_thread", Args(6));

        Assert.True(refusedError);
        Assert.True(missingError);
        Assert.EndsWith(Msg.PostUnavailable, refused);
        Assert.EndsWith(Msg.PostUnavailable, missing);
        Assert.DoesNotContain(Ids.OpsSecret, refused);
        Assert.Empty(handler.Writes());
    }

    // Break: forget the deleted flag.
    [Fact]
    public async Task A_deleted_post_is_refused()
    {
        var handler = TestHost.StandardMattermost()
            .RespondGetPost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "gone", deleteAt: Mm.T0 + 1))
            .RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("reply_to_thread", Args(2));

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
        Assert.Empty(handler.Writes());
    }

    // Break: skip the message rules for replies.
    [Theory]
    [InlineData("", "message is empty")]
    [InlineData("@channel hi", "message must not notify everyone (@all, @channel, @here)")]
    public async Task A_bad_message_is_refused_before_anything_is_read_or_sent(string message, string expected)
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("reply_to_thread", Args(2, message));

        Assert.True(isError);
        Assert.EndsWith(expected, text);
        Assert.Empty(handler.Writes());
    }

    // Break: skip the id check.
    [Fact]
    public async Task A_malformed_post_id_is_refused()
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("reply_to_thread", new() { ["post_id"] = "../channels", ["message"] = "hi" });

        Assert.True(isError);
        Assert.EndsWith("invalid post id", text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.Contains(".."));
    }
}
