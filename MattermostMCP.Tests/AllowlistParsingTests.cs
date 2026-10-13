using MattermostMcp.Configuration;
using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class AllowlistParsingTests
{
    // Break: treat every entry as a team name, or lose the kind of an entry.
    [Fact]
    public void Entries_are_classified_by_shape()
    {
        var allowlist = Allowlist.Parse($"dev, ops/general, @bob, {Ids.GroupAll}");

        Assert.Equal(
            [
                new TeamEntry("dev"),
                new ChannelEntry("ops", "general"),
                new DirectEntry("bob"),
                new ChannelIdEntry(Ids.GroupAll),
            ],
            allowlist.Parsed);
    }

    [Theory]
    [InlineData("a/b/c")]
    [InlineData("/general")]
    [InlineData("dev/")]
    [InlineData("@")]
    [InlineData("@bob/x")]
    public void Malformed_entry_is_refused_naming_it(string entry)
    {
        var ex = Assert.Throws<ConfigurationException>(() => Allowlist.Parse($"dev, {entry}"));

        Assert.Contains(entry, ex.Message);
    }

    [Fact]
    public void Star_parses_to_the_all_entry()
    {
        Assert.Equal([new AllEntry()], Allowlist.Parse("*").Parsed);
    }
}
