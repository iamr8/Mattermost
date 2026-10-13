using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class AddReactionTests
{
    private static FakeMattermostHandler Mattermost(object? post = null)
        => TestHost.StandardMattermost()
            .RespondGetPost(post ?? Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "nice"))
            .Respond(HttpMethod.Post, "/api/v4/reactions", request => Mm.Response(HttpStatusCode.OK, request.Body!));

    // Break: send the colons, the wrong user, or the wrong post.
    [Theory]
    [InlineData(":thumbsup:")]
    [InlineData("thumbsup")]
    [InlineData(" :thumbsup: ")]
    public async Task The_reaction_is_sent_for_the_token_owner_without_colons(string emoji)
    {
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = emoji });

        Assert.False(isError);
        var write = Assert.Single(handler.Writes());
        Assert.Equal(HttpMethod.Post, write.Method);
        Assert.Equal("/api/v4/reactions", write.Path);
        using var body = JsonDocument.Parse(write.Body!);
        Assert.Equal(Ids.Alice, body.RootElement.GetProperty("user_id").GetString());
        Assert.Equal(Ids.Post(2), body.RootElement.GetProperty("post_id").GetString());
        Assert.Equal("thumbsup", body.RootElement.GetProperty("emoji_name").GetString());
        using var result = JsonDocument.Parse(text);
        Assert.Equal(Ids.Post(2), result.RootElement.GetProperty("post_id").GetString());
        Assert.Equal("thumbsup", result.RootElement.GetProperty("emoji").GetString());
    }

    // Break: skip emoji validation, so the model can inject into the request body.
    [Theory]
    [InlineData("Bad Name!")]
    [InlineData("Thumbs")]
    [InlineData("")]
    [InlineData(":")]
    [InlineData("a:b")]
    public async Task A_bad_emoji_name_is_refused_and_nothing_is_sent(string emoji)
    {
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = emoji });

        Assert.True(isError);
        Assert.EndsWith("invalid emoji name", text);
        Assert.Empty(handler.Writes());
    }

    [Fact]
    public async Task An_emoji_name_longer_than_64_characters_is_refused()
    {
        var handler = Mattermost();
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = new string('a', 65) });

        Assert.True(isError);
        Assert.EndsWith("invalid emoji name", text);
        Assert.Empty(handler.Writes());
    }

    // Break: react first and check the channel later.
    [Fact]
    public async Task A_post_in_a_refused_channel_is_refused_and_nothing_is_sent()
    {
        var handler = Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.OpsSecret, "secret"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = "thumbsup" });

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
        Assert.DoesNotContain(Ids.OpsSecret, text);
        Assert.Empty(handler.Writes());
    }

    // Break: forget the deleted flag.
    [Fact]
    public async Task A_deleted_post_is_refused()
    {
        var handler = Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "gone", deleteAt: Mm.T0 + 1));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = "thumbsup" });

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
        Assert.Empty(handler.Writes());
    }
}
