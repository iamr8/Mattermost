using System.Net;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class BootstrapTests
{
    // Break: log request headers, or copy the response into the error text.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_token_stops_startup_without_leaking_it(HttpStatusCode status)
    {
        var handler = new FakeMattermostHandler().RespondWithStatus(HttpMethod.Get, "/api/v4/users/me", status);
        await using var host = TestHost.Create(handler);

        var ex = await Assert.ThrowsAsync<StartupException>(() => host.BootstrapAsync(TestContext.Current.CancellationToken));

        Assert.Equal("token rejected by chat.example.com", ex.Message);
        Assert.DoesNotContain(TestHost.Token, ex.ToString());
        Assert.NotEmpty(host.Logs.Lines);
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains(TestHost.Token));
    }

    [Fact]
    public async Task Unreachable_server_stops_startup_naming_the_host()
    {
        var handler = new FakeMattermostHandler().Respond(HttpMethod.Get, "/api/v4/users/me", _ => throw new HttpRequestException("connection refused"));
        await using var host = TestHost.Create(handler);

        var ex = await Assert.ThrowsAsync<StartupException>(() => host.BootstrapAsync(TestContext.Current.CancellationToken));

        Assert.Equal("cannot reach chat.example.com", ex.Message);
    }

    // Break: let a 200 with an HTML body (an SSO or proxy login page) escape as an unhandled exception.
    [Fact]
    public async Task A_200_that_is_not_json_stops_startup_cleanly()
    {
        var handler = new FakeMattermostHandler().Respond(HttpMethod.Get, "/api/v4/users/me", _ => Mm.Response(HttpStatusCode.OK, "<html>sign in</html>", "text/html"));
        await using var host = TestHost.Create(handler);

        var ex = await Assert.ThrowsAsync<StartupException>(() => host.BootstrapAsync(TestContext.Current.CancellationToken));

        Assert.Equal("mattermost sent an unreadable response", ex.Message);
    }

    // Break: put the full resilience pipeline on the startup client (3 retries would run).
    [Fact]
    public async Task Startup_makes_one_attempt_without_retries()
    {
        var handler = new FakeMattermostHandler().RespondWithStatus(HttpMethod.Get, "/api/v4/users/me", HttpStatusCode.InternalServerError);
        await using var host = TestHost.Create(handler);

        await Assert.ThrowsAsync<StartupException>(() => host.BootstrapAsync(TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }
}
