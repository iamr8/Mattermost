using System.Net;
using System.Text;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class DownloadFileTests
{
    private const string Sentinel = "SENTINEL-body-7731";
    private const string Unavailable = "file not available";
    private const string TooLarge = "file too large (max 25 MB)";

    private static Dictionary<string, object?> Args(string id = Ids.File) => new() { ["file_id"] = id };

    /// <summary>A file on post 2, written by Bob in <paramref name="channel"/>.</summary>
    private static FakeMattermostHandler Mattermost(string channel = Ids.DevGeneral, object? info = null, byte[]? body = null, long deleteAt = 0)
        => TestHost.StandardMattermost()
            .RespondGetPost(Mm.Post(Ids.Post(2), Mm.T0, Ids.Bob, channel, "see file", deleteAt: deleteAt, fileIds: [Ids.File]))
            .RespondFileInfo(info ?? FileFakes.Info(Ids.File, Ids.Post(2)))
            .RespondFileBody(Ids.File, body ?? Encoding.UTF8.GetBytes("hello"));

    // Break: use the wrong endpoint, a wrong path, or drop the metadata from the answer.
    [Fact]
    public async Task An_allowed_file_is_saved_and_the_path_is_returned()
    {
        var handler = Mattermost();
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        var path = json.RootElement.GetProperty("path").GetString()!;
        Assert.Equal(Path.Combine(host.FilesFolder, Ids.File + ".txt"), path);
        Assert.Equal("hello", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("report.txt", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(5, json.RootElement.GetProperty("size").GetInt64());
        Assert.Equal("text/plain", json.RootElement.GetProperty("mime").GetString());
        Assert.Single(handler.Requests, r => r.Path == FileFakes.InfoPath(Ids.File));
        Assert.Single(handler.BodyRequests());
        Assert.Equal(FileFakes.BodyPath(Ids.File), handler.BodyRequests()[0].Path);
    }

    // Break: fetch the body before the channel check, or show the channel in the refusal.
    [Fact]
    public async Task A_file_in_a_hidden_channel_is_refused_and_its_body_is_never_requested()
    {
        var handler = Mattermost(channel: Ids.OpsSecret);
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.True(isError);
        Assert.EndsWith(Unavailable, text);
        Assert.DoesNotContain(Ids.OpsSecret, text);
        Assert.Empty(handler.BodyRequests());
        Assert.Empty(host.SavedFiles());
    }

    // Break: give each refusal its own text, so the model can tell a deleted post from a file with no post.
    [Theory]
    [InlineData("no post")]
    [InlineData("deleted post")]
    [InlineData("missing file")]
    [InlineData("forbidden file")]
    [InlineData("missing post")]
    public async Task Other_refusals_read_the_same_and_no_body_is_requested(string kind)
    {
        var handler = kind switch
        {
            "no post" => Mattermost(info: FileFakes.Info(Ids.File, "")),
            "deleted post" => Mattermost(deleteAt: Mm.T0 + 1),
            "missing file" => TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, FileFakes.InfoPath(Ids.File), HttpStatusCode.NotFound),
            "forbidden file" => TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, FileFakes.InfoPath(Ids.File), HttpStatusCode.Forbidden),
            _ => TestHost.StandardMattermost()
                .RespondFileInfo(FileFakes.Info(Ids.File, Ids.Post(2)))
                .RespondWithStatus(HttpMethod.Get, WriteFakes.PostPath(Ids.Post(2)), HttpStatusCode.NotFound),
        };
        handler.RespondFileBody(Ids.File, [1, 2, 3]);
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.True(isError);
        Assert.EndsWith(Unavailable, text);
        Assert.Empty(handler.BodyRequests());
        Assert.Empty(host.SavedFiles());
    }

    // Break: skip the size check on the info, so a huge file is fetched before it is refused.
    [Fact]
    public async Task A_file_the_info_says_is_over_25_MB_is_refused_before_the_body_is_requested()
    {
        var handler = Mattermost(info: FileFakes.Info(Ids.File, Ids.Post(2), size: FileFakes.Limit + 1));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.True(isError);
        Assert.EndsWith(TooLarge, text);
        Assert.Empty(handler.BodyRequests());
    }

    // Break: trust the size in the info, so a body bigger than it said fills memory or disk.
    [Fact]
    public async Task A_body_bigger_than_the_info_said_is_stopped_and_nothing_is_saved()
    {
        var handler = Mattermost(info: FileFakes.Info(Ids.File, Ids.Post(2), size: 1024), body: new byte[FileFakes.Limit + 1]);
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.True(isError);
        Assert.EndsWith(TooLarge, text);
        Assert.Empty(host.SavedFiles());
    }

    // Break: put the server file name or extension into the path.
    [Fact]
    public async Task The_server_file_name_never_reaches_the_path()
    {
        var handler = Mattermost(info: FileFakes.Info(Ids.File, Ids.Post(2), name: "../../etc/passwd", extension: "../x"));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(Path.Combine(host.FilesFolder, Ids.File + ".bin"), json.RootElement.GetProperty("path").GetString());
        Assert.Equal([Path.Combine(host.FilesFolder, Ids.File + ".bin")], host.SavedFiles());
    }

    // Break: skip the id check, so "../x" reaches the URL.
    [Theory]
    [InlineData("../x")]
    [InlineData("f1aaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("")]
    public async Task A_malformed_file_id_is_refused_and_nothing_is_requested(string id)
    {
        var handler = Mattermost();
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args(id));

        Assert.True(isError);
        Assert.EndsWith("invalid file id", text);
        Assert.DoesNotContain(handler.Requests, r => r.Path.Contains("/files"));
    }

    // Break: send the download through the retrying client, so one call asks three times.
    [Fact(Timeout = 15000)]
    public async Task A_server_error_is_not_retried_and_leaks_nothing()
    {
        var handler = Mattermost()
            .Respond(HttpMethod.Get, FileFakes.BodyPath(Ids.File), _ => Mm.Response(
                HttpStatusCode.InternalServerError,
                $"{{\"id\":\"api.file.get_file.app_error\",\"message\":\"{Sentinel} {TestHost.Token}\"}}"));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.True(isError);
        Assert.EndsWith("mattermost error 500 api.file.get_file.app_error", text);
        Assert.Single(handler.BodyRequests());
        Assert.DoesNotContain(Sentinel, text);
        Assert.DoesNotContain(TestHost.Token, text);
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains(Sentinel) || line.Contains(TestHost.Token));
        Assert.Empty(host.SavedFiles());
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested); // ended before the timeout
    }

    // Break: let a timeout escape as a masked error.
    [Fact]
    public async Task A_timeout_gives_the_fixed_text_and_saves_nothing()
    {
        var handler = Mattermost()
            .Respond(HttpMethod.Get, FileFakes.BodyPath(Ids.File), _ => throw new TaskCanceledException("timed out", new TimeoutException()));
        await using var host = await FileFakes.StartAsync(handler);

        var (isError, text) = await host.CallAsync("download_file", Args());

        Assert.True(isError);
        Assert.EndsWith("mattermost did not answer in time", text);
        Assert.Empty(host.SavedFiles());
    }
}
