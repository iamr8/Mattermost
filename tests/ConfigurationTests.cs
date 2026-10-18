using MattermostMcp.Configuration;

namespace MattermostMcp.Tests;

public class ConfigurationTests
{
    private static Func<string, string?> Env(params (string Key, string? Value)[] values)
        => new Dictionary<string, string?>(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).GetValueOrDefault;

    private static (string, string?)[] Valid(string url = "https://chat.example.com", string? token = "t", string? allowlist = "*")
        => [("MATTERMOST_URL", url), ("MATTERMOST_TOKEN", token), ("MATTERMOST_ALLOWLIST", allowlist)];

    // Break: validate lazily, or forget a required var.
    [Theory]
    [InlineData("MATTERMOST_URL")]
    [InlineData("MATTERMOST_TOKEN")]
    [InlineData("MATTERMOST_ALLOWLIST")]
    public void Missing_or_blank_required_variable_throws_naming_it(string name)
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            var values = Valid().Select(v => v.Item1 == name ? (v.Item1, value) : v).ToArray();

            var ex = Assert.Throws<ConfigurationException>(() => MattermostOptions.FromEnvironment(Env(values)));

            Assert.Contains(name, ex.Message);
        }
    }

    // Break: forget to strip the API suffix, so requests go to /api/v4/api/v4/...
    [Theory]
    [InlineData("https://chat.example.com", "https://chat.example.com/api/v4/")]
    [InlineData("https://chat.example.com/", "https://chat.example.com/api/v4/")]
    [InlineData("https://chat.example.com/api/v4", "https://chat.example.com/api/v4/")]
    [InlineData("https://chat.example.com/api/v4/", "https://chat.example.com/api/v4/")]
    [InlineData("https://chat.example.com/team-chat/", "https://chat.example.com/team-chat/api/v4/")]
    public void Url_is_normalized_to_the_api_base(string input, string expected)
    {
        var options = MattermostOptions.FromEnvironment(Env(Valid(url: input)));

        Assert.Equal(expected, options.BaseUri.ToString());
    }

    // Break: accept http for a remote host, sending the token in clear.
    [Theory]
    [InlineData("http://chat.example.com")]
    [InlineData("ftp://chat.example.com")]
    [InlineData("not a url")]
    public void Url_must_be_https_or_loopback_http(string url)
    {
        var ex = Assert.Throws<ConfigurationException>(() => MattermostOptions.FromEnvironment(Env(Valid(url: url))));

        Assert.Contains("MATTERMOST_URL", ex.Message);
    }

    [Theory]
    [InlineData("http://localhost:8065")]
    [InlineData("http://127.0.0.1:8065")]
    public void Http_is_allowed_for_loopback(string url)
    {
        var options = MattermostOptions.FromEnvironment(Env(Valid(url: url)));

        Assert.StartsWith(url, options.BaseUri.ToString());
    }

    // Break: treat "*" as an ordinary entry, or let it mix with others.
    [Fact]
    public void Star_allows_everything_and_only_when_alone()
    {
        var all = MattermostOptions.FromEnvironment(Env(Valid(allowlist: "*")));

        Assert.True(all.Allowlist.AllowsAll);

        var ex = Assert.Throws<ConfigurationException>(() => MattermostOptions.FromEnvironment(Env(Valid(allowlist: "dev,*"))));
        Assert.Contains("MATTERMOST_ALLOWLIST", ex.Message);
    }

    [Fact]
    public void Entries_are_trimmed_and_empty_ones_dropped()
    {
        var options = MattermostOptions.FromEnvironment(Env(Valid(allowlist: " dev , ops/general,, ")));

        Assert.False(options.Allowlist.AllowsAll);
        Assert.Equal(["dev", "ops/general"], options.Allowlist.Entries);
    }

    [Fact]
    public void Token_is_never_part_of_to_string()
    {
        var options = MattermostOptions.FromEnvironment(Env(Valid(token: "tok-SECRET-123")));

        Assert.DoesNotContain("tok-SECRET-123", options.ToString());
    }
}
