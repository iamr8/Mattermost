using System.Net;
using System.Text.Json;
using MattermostMcp.Configuration;

namespace MattermostMcp.Tests.Fakes;

/// <summary>Helpers for the write tools. Every write test runs against the fake; no real channel is touched.</summary>
internal static class WriteFakes
{
    /// <summary>Writes on, team dev only. Channel ops/secret is outside the allowlist.</summary>
    public static MattermostOptions Options(string allowlist = "dev") => TestHost.DefaultOptions(allowlist, readOnly: "false");

    public static string PostPath(string id) => $"/api/v4/posts/{id}";

    /// <summary>POST /posts: answers with the post the caller asked for, stamped with id 90 and time T0 + 5 s.</summary>
    public static FakeMattermostHandler RespondCreatePost(this FakeMattermostHandler handler)
        => handler.Respond(HttpMethod.Post, "/api/v4/posts", request =>
        {
            using var body = JsonDocument.Parse(request.Body!);
            var root = body.RootElement;
            var rootId = root.TryGetProperty("root_id", out var r) ? r.GetString()! : "";
            return Mm.Response(
                HttpStatusCode.OK,
                Mm.Json(Mm.Post(Ids.Post(90), Mm.T0 + 5000, Ids.Alice, root.GetProperty("channel_id").GetString()!, root.GetProperty("message").GetString()!, rootId)));
        });

    public static FakeMattermostHandler RespondGetPost(this FakeMattermostHandler handler, object post)
    {
        var id = (string)post.GetType().GetProperty("id")!.GetValue(post)!;
        return handler.RespondJson(HttpMethod.Get, PostPath(id), Mm.Json(post));
    }

    /// <summary>The requests that change data: everything that is not a GET and not the batch user lookup.</summary>
    public static IReadOnlyList<RecordedRequest> Writes(this FakeMattermostHandler handler)
        => handler.Requests.Where(r => r.Method != HttpMethod.Get && !r.Path.EndsWith("/users/ids")).ToList();

    public static async Task<TestHost> StartWritableAsync(FakeMattermostHandler handler, string allowlist = "dev", ManualTimeProvider? time = null)
    {
        var host = TestHost.Create(handler, time ?? new ManualTimeProvider(), Options(allowlist));
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
