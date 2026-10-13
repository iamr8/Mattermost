using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace MattermostMcp.Mattermost;

/// <summary>
/// Read-only calls to the Mattermost v4 API. Holds only the factory and a client name, so it is safe as a
/// singleton; the <see cref="HttpClient"/> is created per call.
/// </summary>
public sealed partial class MattermostClient(IHttpClientFactory factory, string clientName)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task<User> GetMeAsync(CancellationToken cancellationToken)
        => GetAsync<User>("users/me", "current user", cancellationToken);

    public Task<List<Team>> GetTeamsAsync(string userId, CancellationToken cancellationToken)
        => GetAsync<List<Team>>($"users/{Uri.EscapeDataString(userId)}/teams", "teams", cancellationToken);

    /// <summary>Every channel the user is in, across teams, direct and group channels included.</summary>
    public Task<List<Channel>> GetChannelsAsync(string userId, CancellationToken cancellationToken)
        => GetAsync<List<Channel>>($"users/{Uri.EscapeDataString(userId)}/channels", "channels", cancellationToken);

    public Task<PostList> GetPostsAsync(string channelId, int perPage, string? before, CancellationToken cancellationToken)
    {
        var path = $"channels/{Uri.EscapeDataString(channelId)}/posts?per_page={perPage}&skipFetchThreads=true";
        if (before is not null)
        {
            path += $"&before={Uri.EscapeDataString(before)}";
        }

        return GetAsync<PostList>(path, "channel", cancellationToken);
    }

    public Task<Post> GetPostAsync(string postId, CancellationToken cancellationToken)
        => GetAsync<Post>($"posts/{Uri.EscapeDataString(postId)}", "post", cancellationToken);

    public Task<PostList> GetThreadAsync(string postId, CancellationToken cancellationToken)
        => GetAsync<PostList>($"posts/{Uri.EscapeDataString(postId)}/thread", "thread", cancellationToken);

    public Task<PostList> SearchPostsAsync(string teamId, string terms, int perPage, CancellationToken cancellationToken)
        => SendAsync<PostList>(
            HttpMethod.Post,
            $"teams/{Uri.EscapeDataString(teamId)}/posts/search",
            new { terms, is_or_search = false, page = 0, per_page = perPage },
            "search",
            cancellationToken);

    public Task<List<User>> GetUsersByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        => SendAsync<List<User>>(HttpMethod.Post, "users/ids", ids, "users", cancellationToken);

    public Task<UserProfile> GetUserByUsernameAsync(string username, CancellationToken cancellationToken)
        => GetAsync<UserProfile>($"users/username/{Uri.EscapeDataString(username)}", "user", cancellationToken);

    public Task<UserProfile> GetUserAsync(string userId, CancellationToken cancellationToken)
        => GetAsync<UserProfile>($"users/{Uri.EscapeDataString(userId)}", "user", cancellationToken);

    private Task<T> GetAsync<T>(string path, string resource, CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Get, path, null, resource, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string resource, CancellationToken cancellationToken)
    {
        var http = factory.CreateClient(clientName);
        var host = http.BaseAddress!.Authority;

        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await MapStatusAsync(response, resource, cancellationToken);
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                   ?? throw new MattermostException(MattermostErrorKind.Other, "mattermost sent an empty response");
        }
        catch (HttpRequestException)
        {
            throw new MattermostException(MattermostErrorKind.Unreachable, $"cannot reach {host}");
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or BrokenCircuitException or RateLimiterRejectedException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MattermostException(MattermostErrorKind.Timeout, "mattermost did not answer in time");
        }
        catch (JsonException)
        {
            throw new MattermostException(MattermostErrorKind.Other, "mattermost sent an unreadable response");
        }
    }

    private static async Task<MattermostException> MapStatusAsync(HttpResponseMessage response, string resource, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(MattermostErrorKind.Unauthorized, "token rejected", status),
            HttpStatusCode.Forbidden => new(MattermostErrorKind.Forbidden, $"no permission for {resource}", status),
            HttpStatusCode.NotFound => new(MattermostErrorKind.NotFound, $"not found: {resource}", status),
            HttpStatusCode.TooManyRequests => new(MattermostErrorKind.RateLimited, "rate limited, try later", status),
            _ => new(MattermostErrorKind.Other, await OtherMessageAsync(response, status, cancellationToken), status),
        };
    }

    // Only the short error code (the body's "id", e.g. api.context.session_expired.app_error) is kept.
    private static async Task<string> OtherMessageAsync(HttpResponseMessage response, int status, CancellationToken cancellationToken)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (body.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && ErrorCode().IsMatch(id.GetString()!))
            {
                return $"mattermost error {status} {id.GetString()}";
            }
        }
        catch (JsonException)
        {
        }

        return $"mattermost error {status}";
    }

    [GeneratedRegex("^[A-Za-z0-9_.]{1,100}$")]
    private static partial Regex ErrorCode();
}
