using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class GetPostFilesTests
{
    private static Dictionary<string, object?> Args() => new() { ["post_id"] = Ids.Post(2) };

    private static FakeMattermostHandler Mattermost(object? post = null)
        => TestHost.StandardMattermost().RespondGetPost(post ?? Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "see files", fileIds: [Ids.File, Ids.File2]));

    // Break: return a wrong field, or the count instead of the list.
    [Fact]
    public async Task Lists_id_name_size_and_mime_of_each_file()
    {
        var handler = Mattermost().RespondPostFiles(
            Ids.Post(2),
            FileFakes.Info(Ids.File, Ids.Post(2), "report.txt", "txt", 5, "text/plain"),
            FileFakes.Info(Ids.File2, Ids.Post(2), "chart.png", "png", 2048, "image/png"));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_post_files", Args());

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal(Ids.File, files[0].GetProperty("id").GetString());
        Assert.Equal("report.txt", files[0].GetProperty("name").GetString());
        Assert.Equal(5, files[0].GetProperty("size").GetInt64());
        Assert.Equal("text/plain", files[0].GetProperty("mime").GetString());
        Assert.Equal("image/png", files[1].GetProperty("mime").GetString());
        Assert.Equal(2048, files[1].GetProperty("size").GetInt64());
    }

    // Break: omit the list when it is empty, so the model sees no answer.
    [Fact]
    public async Task A_post_without_files_gives_an_empty_list()
    {
        var handler = Mattermost().RespondPostFiles(Ids.Post(2));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_post_files", Args());

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(0, json.RootElement.GetProperty("files").GetArrayLength());
    }

    // Break: list the files before the channel check, or show the channel in the refusal.
    [Theory]
    [InlineData("hidden")]
    [InlineData("deleted")]
    [InlineData("missing")]
    public async Task A_hidden_deleted_or_missing_post_is_refused_and_no_file_info_is_requested(string kind)
    {
        var handler = kind switch
        {
            "hidden" => Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.OpsSecret, "secret")),
            "deleted" => Mattermost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, Ids.DevGeneral, "gone", deleteAt: Mm.T0 + 1)),
            _ => TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, WriteFakes.PostPath(Ids.Post(2)), HttpStatusCode.NotFound),
        };
        handler.RespondPostFiles(Ids.Post(2), FileFakes.Info(Ids.File, Ids.Post(2)));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_post_files", Args());

        Assert.True(isError);
        Assert.EndsWith(Msg.PostUnavailable, text);
        Assert.DoesNotContain(Ids.OpsSecret, text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/files/info"));
    }

    // Break: show the server file name raw, with control characters or without a length limit.
    [Fact]
    public async Task The_display_name_loses_control_characters_and_is_cut_to_100_characters()
    {
        var handler = Mattermost().RespondPostFiles(
            Ids.Post(2),
            FileFakes.Info(Ids.File, Ids.Post(2), "re\u0007po\nrt.txt"),
            FileFakes.Info(Ids.File2, Ids.Post(2), new string('a', 300) + ".txt"));
        await using var host = await FileFakes.StartAsync(handler);

        var (_, text) = await host.CallAsync("get_post_files", Args());

        using var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal("report.txt", files[0].GetProperty("name").GetString());
        Assert.Equal(100, files[1].GetProperty("name").GetString()!.Length);
    }

    // Break: skip the id check.
    [Fact]
    public async Task A_malformed_post_id_is_refused_and_nothing_is_requested()
    {
        var handler = Mattermost();
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_post_files", new() { ["post_id"] = "../files" });

        Assert.True(isError);
        Assert.EndsWith("invalid post id", text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.Contains(".."));
    }
}
