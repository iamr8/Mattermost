using System.Net;
using System.Text.Json;

namespace MattermostMcp.Tests.Fakes;

/// <summary>Helpers for the file tools. Files are served by the fake; no real channel is touched.</summary>
internal static class FileFakes
{
    public const long Limit = 25L * 1024 * 1024;

    public static string InfoPath(string id) => $"/api/v4/files/{id}/info";

    public static string BodyPath(string id) => $"/api/v4/files/{id}";

    public static string PostFilesPath(string postId) => $"/api/v4/posts/{postId}/files/info";

    public static object Info(string id, string postId, string name = "report.txt", string extension = "txt", long size = 5, string mime = "text/plain")
        => new { id, post_id = postId, name, extension, size, mime_type = mime };

    public static FakeMattermostHandler RespondFileInfo(this FakeMattermostHandler handler, object info)
    {
        var id = (string)info.GetType().GetProperty("id")!.GetValue(info)!;
        return handler.RespondJson(HttpMethod.Get, InfoPath(id), Mm.Json(info));
    }

    public static FakeMattermostHandler RespondFileBody(this FakeMattermostHandler handler, string id, byte[] body)
        => handler.Respond(HttpMethod.Get, BodyPath(id), _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    public static FakeMattermostHandler RespondPostFiles(this FakeMattermostHandler handler, string postId, params object[] infos)
        => handler.RespondJson(HttpMethod.Get, PostFilesPath(postId), JsonSerializer.Serialize(infos));

    public static IReadOnlyList<RecordedRequest> BodyRequests(this FakeMattermostHandler handler)
        => handler.Requests.Where(r => r.Path.StartsWith("/api/v4/files/") && !r.Path.EndsWith("/info")).ToList();

    public static string[] SavedFiles(this TestHost host)
        => Directory.Exists(host.FilesFolder) ? Directory.GetFileSystemEntries(host.FilesFolder) : [];

    public static async Task<TestHost> StartAsync(FakeMattermostHandler handler, string allowlist = "dev")
    {
        var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist));
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
