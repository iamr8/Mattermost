using MattermostMcp.Configuration;

namespace MattermostMcp.Tests;

public class WriteConfigurationTests
{
    private static MattermostOptions Parse(string? readOnly)
        => MattermostOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["MATTERMOST_URL"] = "https://chat.example.com",
            ["MATTERMOST_TOKEN"] = "t",
            ["MATTERMOST_ALLOWLIST"] = "*",
            ["MATTERMOST_READ_ONLY"] = readOnly,
        }.GetValueOrDefault);

    // Break: default the flag to false, so a missing variable turns writes on.
    [Fact]
    public void Unset_means_read_only()
    {
        Assert.True(Parse(null).ReadOnly);
    }

    // Break: parse case-sensitively, or invert the result.
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData(" false ", false)]
    public void True_and_false_are_accepted_in_any_case(string value, bool expected)
    {
        Assert.Equal(expected, Parse(value).ReadOnly);
    }

    // Break: treat any unknown value as "not true", so a typo such as "flase" or "0" turns writes on.
    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("flase")]
    [InlineData("")]
    [InlineData("   ")]
    public void Any_other_value_stops_startup_and_names_the_variable(string value)
    {
        var ex = Assert.Throws<ConfigurationException>(() => Parse(value));

        Assert.Contains("MATTERMOST_READ_ONLY", ex.Message);
    }
}
