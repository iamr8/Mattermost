using System.Net;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class BootstrapAllowlistTests
{
    // Break: skip allowlist validation, so a typo silently allows nothing.
    [Theory]
    [InlineData("nope")]
    [InlineData("dev/nope")]
    [InlineData("nope/general")]
    [InlineData("c9aaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Unknown_entry_stops_startup_naming_it(string entry)
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost(), TestHost.DefaultOptions(allowlist: $"dev, {entry}"));

        var ex = await Assert.ThrowsAsync<StartupException>(() => host.BootstrapAsync(TestContext.Current.CancellationToken));

        Assert.Contains(entry, ex.Message);
    }

    [Fact]
    public async Task Unknown_user_entry_stops_startup_naming_it()
    {
        var handler = TestHost.StandardMattermost()
            .RespondWithStatus(HttpMethod.Get, "/api/v4/users/username/ghost", HttpStatusCode.NotFound);
        await using var host = TestHost.Create(handler, TestHost.DefaultOptions(allowlist: "@ghost"));

        var ex = await Assert.ThrowsAsync<StartupException>(() => host.BootstrapAsync(TestContext.Current.CancellationToken));

        Assert.Contains("@ghost", ex.Message);
    }

    [Fact]
    public async Task A_mix_of_all_entry_kinds_starts()
    {
        await using var host = TestHost.Create(
            TestHost.StandardMattermost(),
            TestHost.DefaultOptions(allowlist: $"dev, ops/general, @bob, {Ids.GroupAll}"));

        await host.BootstrapAsync(TestContext.Current.CancellationToken);
    }
}
