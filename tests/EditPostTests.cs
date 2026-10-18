using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class EditPostTests
{
    private static FakeMattermostHandler Mattermost(object post)
        => TestHost.StandardMattermost()
            .RespondGetPost(post)
            .Respond(HttpMethod.Put, WriteFakes.PostPath(Ids.Post(2)) + "/patch", request =>
            {
                using var body = JsonDocument.Parse(request.Body!);
                return Mm.Response(HttpStatusCode.OK, Mm.Json(Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, body.RootElement.GetProperty("message").GetString()!, editAt: Mm.T0 + 9000)));
            });

    private static Dictionary<string, object?> Args(string message = "fixed text") => new() { ["post_id"] = Ids.Post(2), ["message"] = message };

    // Break: send more than the message (so other fields are rewritten), or use PUT /posts/{id} instead of the patch route.
    [Fact]
    public async Task An_own_post_is_patched_with_the_new_text_only()
    {
        var handler = Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, "old text"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("edit_post", Args());

        Assert.False(isError);
        var write = Assert.Single(handler.Writes());
        Assert.Equal(HttpMethod.Put, write.Method);
        Assert.Equal(WriteFakes.PostPath(Ids.Post(2)) + "/patch", write.Path);
        using var body = JsonDocument.Parse(write.Body!);
        Assert.Equal(["message"], body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("fixed text", body.RootElement.GetProperty("message").GetString());
        using var result = JsonDocument.Parse(text);
        Assert.Equal(Ids.Post(2), result.RootElement.GetProperty("id").GetString());
        Assert.True(result.RootElement.GetProperty("edited").GetBoolean());
    }

    // Break: drop the author check, so Claude can rewrite what other people said.
    [Fact]
    public async Task A_post_by_someone_else_is_refused_and_nothing_is_sent()
    {
        var handler = Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "bob's words"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("edit_post", Args());

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
        Assert.Empty(handler.Writes());
    }

    // Break: give each refusal its own text, so the model can tell a deleted post from a hidden channel.
    [Fact]
    public async Task Deleted_hidden_and_missing_posts_all_get_the_same_text_and_nothing_is_sent()
    {
        var texts = new List<string>();
        foreach (var post in new object?[]
                 {
                     Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, "gone", deleteAt: Mm.T0 + 1),
                     Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.OpsSecret, "mine, but hidden"),
                     null,
                 })
        {
            var handler = post is null
                ? TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, WriteFakes.PostPath(Ids.Post(2)), HttpStatusCode.NotFound)
                : Mattermost(post);
            await using var host = await WriteFakes.StartWritableAsync(handler);

            var (isError, text) = await host.CallAsync("edit_post", Args());

            Assert.True(isError);
            Assert.Empty(handler.Writes());
            texts.Add(text);
        }

        Assert.All(texts, t => Assert.EndsWith(Msg.PostUnavailable, t));
        Assert.Single(texts.Select(t => t.Split(": ", 2)[^1]).Distinct());
    }

    // Break: skip the message rules for edits.
    [Theory]
    [InlineData("", "message is empty")]
    [InlineData("@all", "message must not notify everyone (@all, @channel, @here)")]
    public async Task A_bad_message_is_refused_and_nothing_is_sent(string message, string expected)
    {
        var handler = Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, "old text"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("edit_post", Args(message));

        Assert.True(isError);
        Assert.EndsWith(expected, text);
        Assert.Empty(handler.Writes());
    }

    // Break: let a server-side refusal (edit time limit) escape as a masked error, or copy its body into the text.
    [Fact]
    public async Task A_forbidden_edit_says_so_in_fixed_words()
    {
        var handler = TestHost.StandardMattermost()
            .RespondGetPost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, "old text"))
            .Respond(HttpMethod.Put, WriteFakes.PostPath(Ids.Post(2)) + "/patch", _ => Mm.Response(HttpStatusCode.Forbidden, "{\"message\":\"SENTINEL-body-7731\"}"));
        await using var host = await WriteFakes.StartWritableAsync(handler);

        var (isError, text) = await host.CallAsync("edit_post", Args());

        Assert.True(isError);
        Assert.EndsWith("no permission to write to post", text);
        Assert.DoesNotContain("SENTINEL-body-7731", text);
    }
}
