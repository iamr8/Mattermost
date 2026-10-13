using System.Net;
using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class ErrorAndResilienceTests
{
    private static string PostsPath => $"/api/v4/channels/{Ids.DevGeneral}/posts";

    private static HttpResponseMessage RetryNow(HttpStatusCode status, string body = "{}")
    {
        var response = Mm.Response(status, body);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    private static async Task<TestHost> StartAsync(FakeMattermostHandler handler)
    {
        var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "*"));
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    // Break: remove the resilience handler from the tool client.
    [Fact]
    public async Task A_429_is_retried_and_the_next_answer_wins()
    {
        var page = Mm.PostList([Ids.Post(1)], [Mm.Post(Ids.Post(1), Mm.T0, Ids.Alice, Ids.DevGeneral, "hello")]);
        var handler = TestHost.StandardMattermost()
            .RespondSequence(HttpMethod.Get, PostsPath, () => RetryNow(HttpStatusCode.TooManyRequests), () => Mm.Response(HttpStatusCode.OK, page));
        await using var host = await StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.False(isError);
        Assert.Contains("hello", text);
        Assert.Equal(2, handler.Requests.Count(r => r.Path == PostsPath));
    }

    // Break: a retry that sends the POST again with an empty body.
    [Fact]
    public async Task A_search_is_retried_with_its_body_intact()
    {
        var path = $"/api/v4/teams/{Ids.TeamDev}/posts/search";
        var handler = TestHost.StandardMattermost()
            .RespondSequence(HttpMethod.Post, path, () => RetryNow(HttpStatusCode.TooManyRequests), () => Mm.Response(HttpStatusCode.OK, Mm.PostList([], [])));
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "dev"));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, _) = await host.CallAsync("search_posts", new() { ["query"] = "hit" });

        Assert.False(isError);
        var attempts = handler.Requests.Where(r => r.Path == path).ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(attempts[0].Body, attempts[1].Body);
        Assert.Contains("\"terms\":\"hit\"", attempts[1].Body);
    }

    [Fact]
    public async Task A_403_becomes_a_fixed_no_permission_message()
    {
        var handler = TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, PostsPath, HttpStatusCode.Forbidden, "{\"message\":\"secret detail\"}");
        await using var host = await StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.True(isError);
        Assert.Contains("no permission for channel", text);
        Assert.DoesNotContain("secret detail", text);
    }

    // Break: copy the upstream "message" into the error text.
    [Fact]
    public async Task A_500_keeps_only_the_error_code_never_the_message_or_token()
    {
        var body = $"{{\"id\":\"api.post.error\",\"message\":\"boom SENTINEL-500 {TestHost.Token}\"}}";
        var handler = TestHost.StandardMattermost()
            .RespondSequence(HttpMethod.Get, PostsPath, () => RetryNow(HttpStatusCode.InternalServerError, body));
        await using var host = await StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.True(isError);
        Assert.EndsWith("mattermost error 500 api.post.error", text);
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains("SENTINEL-500") || line.Contains(TestHost.Token));
    }

    [Fact]
    public async Task A_500_without_a_usable_code_still_hides_the_body()
    {
        var handler = TestHost.StandardMattermost()
            .RespondSequence(HttpMethod.Get, PostsPath, () => RetryNow(HttpStatusCode.InternalServerError, "<html>SENTINEL-html</html>"));
        await using var host = await StartAsync(handler);

        var (_, text) = await host.CallAsync("get_posts", new() { ["channel"] = "dev/general" });

        Assert.EndsWith("mattermost error 500", text);
        Assert.DoesNotContain("SENTINEL-html", text);
    }

    [Fact]
    public async Task A_404_names_the_resource_not_the_body()
    {
        var handler = TestHost.StandardMattermost().RespondWithStatus(HttpMethod.Get, "/api/v4/users/username/ghost", HttpStatusCode.NotFound);
        await using var host = await StartAsync(handler);

        var (isError, text) = await host.CallAsync("get_user", new() { ["user"] = "@ghost" });

        Assert.True(isError);
        Assert.Contains("not found: user", text);
    }
}
