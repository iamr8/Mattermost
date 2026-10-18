using System.Text.Json;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class GetUserTests
{
    private static string UserJson(string id, string username) => Mm.Json(new
    {
        id,
        username,
        nickname = "Bobby",
        first_name = "Bob",
        last_name = "Jones",
        position = "Designer",
        email = "bob@example.com",
        roles = "system_user",
    });

    // Break: return the email, or skip the @ strip.
    [Theory]
    [InlineData("bob", "/api/v4/users/username/bob")]
    [InlineData("@bob", "/api/v4/users/username/bob")]
    [InlineData(Ids.Bob, "/api/v4/users/" + Ids.Bob)]
    public async Task A_user_is_found_by_name_or_id_and_the_email_is_not_returned(string input, string path)
    {
        var handler = TestHost.StandardMattermost().Respond(HttpMethod.Get, path, _ => Mm.Response(System.Net.HttpStatusCode.OK, UserJson(Ids.Bob, "bob")));
        await using var host = TestHost.Create(handler);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var (isError, text) = await host.CallAsync("get_user", new() { ["user"] = input });

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("bob", json.RootElement.GetProperty("username").GetString());
        Assert.Equal("Designer", json.RootElement.GetProperty("position").GetString());
        Assert.DoesNotContain("bob@example.com", text);
        Assert.DoesNotContain("system_user", text);
    }

    [Theory]
    [InlineData("../users/me")]
    [InlineData("")]
    [InlineData("a b")]
    public async Task A_malformed_user_is_refused_without_a_request(string input)
    {
        var handler = TestHost.StandardMattermost();
        await using var host = TestHost.Create(handler);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var before = handler.Requests.Count;

        var (isError, _) = await host.CallAsync("get_user", new() { ["user"] = input });

        Assert.True(isError);
        Assert.Equal(before, handler.Requests.Count);
    }
}
