using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class PostMessageTests
{
    private const string Sentinel = "SENTINEL-body-7731";

    // Break: send the wrong channel id, drop the text, or return a made-up id.
    [Fact]
    public async Task Posts_to_an_allowed_channel_and_returns_the_new_id()
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = "hello team" });

        Assert.False(isError);
        var write = Assert.Single(handler.Writes());
        Assert.Equal(HttpMethod.Post, write.Method);
        Assert.Equal("/api/v4/posts", write.Path);
        using var body = JsonDocument.Parse(write.Body!);
        Assert.Equal(Ids.DevGeneral, body.RootElement.GetProperty("channel_id").GetString());
        Assert.Equal("hello team", body.RootElement.GetProperty("message").GetString());
        Assert.False(body.RootElement.TryGetProperty("root_id", out _));
        using var result = JsonDocument.Parse(text);
        Assert.Equal(Ids.Post(90), result.RootElement.GetProperty("id").GetString());
        Assert.Equal("dev/general", result.RootElement.GetProperty("channel").GetString());
        Assert.Equal("2026-09-21T14:13:25Z", result.RootElement.GetProperty("at").GetString());
    }

    // Break: check the allowlist after the write, or skip it for writes.
    [Theory]
    [InlineData("ops/secret")]
    [InlineData("ops/general")]
    [InlineData("@bob")]
    [InlineData("no-such-channel")]
    public async Task A_channel_outside_the_allowlist_is_refused_with_the_read_text_and_nothing_is_sent(string channel)
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("post_message", new() { ["channel"] = channel, ["message"] = "hello" });

        Assert.True(isError);
        Assert.EndsWith(Msg.Unavailable(channel), text);
        Assert.Empty(handler.Writes());
    }

    // Break: skip a message rule, or check it after the write.
    [Theory]
    [InlineData("", "message is empty")]
    [InlineData("   \n ", "message is empty")]
    [InlineData("@here", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("deploy now @here please", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("@ALL, look", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("hey\n@channel", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("(@Here)", "message must not notify everyone (@all, @channel, @here)")]
    // Mattermost matches \B@(channel|all|here)\b, so these ping everyone too.
    [InlineData("ping @all-hands", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("see -@here", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("foo.@here", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("_@channel_", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("@here.", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("**@all**", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("@here_ done", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("@hereé", "message must not notify everyone (@all, @channel, @here)")]
    [InlineData("é@all", "message must not notify everyone (@all, @channel, @here)")]
    public async Task A_bad_message_is_refused_and_nothing_is_sent(string message, string expected)
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = message });

        Assert.True(isError);
        Assert.EndsWith(expected, text);
        Assert.Empty(handler.Writes());
    }

    // Break: use a length limit above or below 16000 characters.
    [Fact]
    public async Task The_length_limit_is_16000_characters()
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (tooLongError, tooLongText) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = new string('a', 16001) });
        Assert.True(tooLongError);
        Assert.EndsWith("message is too long (max 16000 characters)", tooLongText);
        Assert.Empty(handler.Writes());

        var (exactError, _) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = new string('a', 16000) });
        Assert.False(exactError);
        Assert.Single(handler.Writes());
    }

    // Break: make the mass-mention rule match inside words, so mail addresses and user names are refused.
    [Theory]
    [InlineData("write to a@here.com")]
    [InlineData("see @channels and @heretic")]
    [InlineData("email me@all.example")]
    [InlineData("hello@channel")]
    public async Task Text_that_only_looks_like_a_mass_mention_is_allowed(string message)
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, _) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = message });

        Assert.False(isError);
        Assert.Single(handler.Writes());
    }

    // Break: send the write through the client with the retry handler, so one call posts three times.
    [Fact(Timeout = 15000)]
    public async Task A_server_error_is_not_retried_and_leaks_nothing()
    {
        var handler = TestHost.StandardMattermost()
            .Respond(HttpMethod.Post, "/api/v4/posts", _ => Mm.Response(
                HttpStatusCode.InternalServerError,
                $"{{\"id\":\"api.post.create_post.error\",\"message\":\"{Sentinel} {TestHost.Token}\"}}"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = "hello" });

        Assert.True(isError);
        Assert.EndsWith("mattermost error 500 api.post.create_post.error", text);
        Assert.Single(handler.Writes());
        Assert.DoesNotContain(Sentinel, text);
        Assert.DoesNotContain(TestHost.Token, text);
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains(Sentinel) || line.Contains(TestHost.Token));
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested); // ended before the timeout
    }

    // Break: report "cannot reach" or "timeout" (which say nothing was written) when the write may have gone through.
    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    public async Task After_a_timeout_or_network_error_the_result_is_unknown_and_nothing_is_retried(string failure)
    {
        var handler = TestHost.StandardMattermost()
            .Respond(HttpMethod.Post, "/api/v4/posts", _ => failure == "timeout"
                ? throw new TaskCanceledException("timed out", new TimeoutException())
                : throw new HttpRequestException($"connection reset {Sentinel}"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = "hello" });

        Assert.True(isError);
        Assert.EndsWith("result unknown: read the channel before writing again", text);
        Assert.Single(handler.Writes());
        Assert.DoesNotContain(Sentinel, text); // the exception detail stays out of the answer
        Assert.DoesNotContain(TestHost.Token, text);
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains(TestHost.Token));
    }

    // Break: map a write 403 to the read text "no permission for channel".
    [Fact]
    public async Task A_forbidden_write_says_so_in_fixed_words()
    {
        var handler = TestHost.StandardMattermost()
            .Respond(HttpMethod.Post, "/api/v4/posts", _ => Mm.Response(HttpStatusCode.Forbidden, $"{{\"message\":\"{Sentinel}\"}}"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = "hello" });

        Assert.True(isError);
        Assert.EndsWith("no permission to write to channel", text);
        Assert.DoesNotContain(Sentinel, text);
    }

    // Break: send the write with a bearer header other than the token, or none.
    [Fact]
    public async Task The_write_carries_the_bearer_token()
    {
        var handler = TestHost.StandardMattermost().RespondCreatePost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        await host.CallAsync("post_message", new() { ["channel"] = "dev/general", ["message"] = "hello" });

        Assert.Equal("Bearer " + TestHost.Token, Assert.Single(handler.Writes()).Authorization);
    }
}
