using System.Net;
using System.Text;

namespace MattermostMcp.Tests.Fakes;

internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization);

/// <summary>Serves hand-written fixtures by method + path and records every request.</summary>
internal sealed class FakeMattermostHandler : HttpMessageHandler
{
    private readonly Dictionary<(string Method, string Path), Func<RecordedRequest, HttpResponseMessage>> _routes = new();
    private readonly Dictionary<(string Method, string Path), Func<Task<HttpResponseMessage>>> _gated = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _gate = new();

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public FakeMattermostHandler RespondWithFixture(HttpMethod method, string path, string fixture, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return Respond(method, path, _ => Json(status, json));
    }

    public FakeMattermostHandler RespondWithStatus(HttpMethod method, string path, HttpStatusCode status, string body = "{}")
        => Respond(method, path, _ => Json(status, body));

    public FakeMattermostHandler Respond(HttpMethod method, string path, Func<RecordedRequest, HttpResponseMessage> handler)
    {
        _routes[(method.Method, path)] = handler;
        return this;
    }

    public FakeMattermostHandler RespondJson(HttpMethod method, string path, string json, HttpStatusCode status = HttpStatusCode.OK)
        => Respond(method, path, _ => Json(status, json));

    /// <summary>Answers each call from the queue in order; the last entry repeats.</summary>
    public FakeMattermostHandler RespondSequence(HttpMethod method, string path, params Func<HttpResponseMessage>[] responses)
    {
        var index = 0;
        return Respond(method, path, _ => responses[Math.Min(Interlocked.Increment(ref index) - 1, responses.Length - 1)]());
    }

    /// <summary>Holds the response until <paramref name="gate"/> is set; <paramref name="arrived"/> is set when the call lands.</summary>
    public FakeMattermostHandler RespondWhenReleased(HttpMethod method, string path, string json, TaskCompletionSource arrived, TaskCompletionSource gate)
    {
        _gated[(method.Method, path)] = async () =>
        {
            arrived.TrySetResult();
            await gate.Task;
            return Json(HttpStatusCode.OK, json);
        };
        return this;
    }

    /// <summary>POST /users/ids: returns the known users whose id is in the request body.</summary>
    public FakeMattermostHandler RespondUsersByIds(string fixture = "users.json")
    {
        var users = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture))).RootElement.EnumerateArray().ToArray();
        return Respond(HttpMethod.Post, "/api/v4/users/ids", request =>
        {
            var wanted = System.Text.Json.JsonSerializer.Deserialize<string[]>(request.Body!)!;
            var found = users.Where(u => wanted.Contains(u.GetProperty("id").GetString())).ToArray();
            return Json(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(found));
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            body,
            request.Headers.Authorization?.ToString());

        lock (_gate)
        {
            _requests.Add(recorded);
        }

        if (_gated.TryGetValue((request.Method.Method, recorded.Path), out var gated))
        {
            return await gated();
        }

        if (!_routes.TryGetValue((request.Method.Method, recorded.Path), out var handler))
        {
            throw new InvalidOperationException($"Unexpected request {request.Method} {recorded.Path}{recorded.Query}");
        }

        return handler(recorded);
    }
}
