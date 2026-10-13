using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MattermostMcp.Tests.Fakes;

internal sealed record StubRequest(string Method, string Path, string Body, string? Authorization);

/// <summary>
/// A real HTTP server on loopback for tests that run the real server process. It knows the fixture user,
/// teams and channels, one post by the token owner, and the four write routes.
/// </summary>
internal sealed class StubMattermostServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly CancellationTokenSource _stop = new();

    public StubMattermostServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public string Url { get; }

    public IReadOnlyList<StubRequest> Requests => _requests.ToArray();

    public IReadOnlyList<StubRequest> Writes => _requests.Where(r => r.Method != "GET" && !r.Path.EndsWith("/users/ids")).ToArray();

    private static readonly string FileInfo = Mm.Json(FileFakes.Info(Ids.File, Ids.Post(2)));

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var path = context.Request.Url!.AbsolutePath;
            var method = context.Request.HttpMethod;
            _requests.Enqueue(new StubRequest(method, path, body, context.Request.Headers["Authorization"]));

            var (status, json) = Route(method, path, body);
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private static (int Status, string Json) Route(string method, string path, string body)
    {
        var post = Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, "mine", rootId: Ids.Post(1));
        switch (method, path)
        {
            case ("GET", "/api/v4/users/me"): return (200, Fixture("me.json"));
            case ("GET", $"/api/v4/users/{Ids.Alice}/teams"): return (200, Fixture("teams.json"));
            case ("GET", $"/api/v4/users/{Ids.Alice}/channels"): return (200, Fixture("channels.json"));
            case ("GET", var p) when p == $"/api/v4/posts/{Ids.Post(2)}": return (200, Mm.Json(post));
            case ("POST", "/api/v4/posts"):
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    var rootId = root.TryGetProperty("root_id", out var r) ? r.GetString()! : "";
                    return (200, Mm.Json(Mm.Post(Ids.Post(90), Mm.T0 + 5000, Ids.Alice, root.GetProperty("channel_id").GetString()!, root.GetProperty("message").GetString()!, rootId)));
                }

            case ("POST", "/api/v4/reactions"): return (200, body);
            case ("GET", var p) when p == $"/api/v4/posts/{Ids.Post(2)}/files/info": return (200, "[" + FileInfo + "]");
            case ("GET", var p) when p == $"/api/v4/files/{Ids.File}/info": return (200, FileInfo);
            case ("GET", var p) when p == $"/api/v4/files/{Ids.File}": return (200, "hello");
            case ("PUT", var p) when p == $"/api/v4/posts/{Ids.Post(2)}/patch": return (200, Mm.Json(Mm.Post(Ids.Post(2), Mm.T0, Ids.Alice, Ids.DevGeneral, "edited", editAt: Mm.T0 + 9000)));
            default: return (404, "{\"id\":\"api.not_found\"}");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _stop.Dispose();
    }
}
