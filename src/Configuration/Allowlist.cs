using System.Text.RegularExpressions;

namespace MattermostMcp.Configuration;

/// <summary>Which channels the server may reach. <c>*</c> means every channel the user is in.</summary>
public sealed partial class Allowlist
{
    private const string Variable = "MATTERMOST_ALLOWLIST";

    private Allowlist(IReadOnlyList<string> entries, IReadOnlyList<AllowlistEntry> parsed)
    {
        Entries = entries;
        Parsed = parsed;
    }

    public bool AllowsAll => Entries is ["*"];

    /// <summary>The entries as written.</summary>
    public IReadOnlyList<string> Entries { get; }

    public IReadOnlyList<AllowlistEntry> Parsed { get; }

    public static Allowlist Parse(string raw)
    {
        var entries = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (entries.Length == 0)
        {
            throw new ConfigurationException($"{Variable} is not set");
        }

        if (entries.Length > 1 && entries.Contains("*"))
        {
            throw new ConfigurationException($"{Variable}: * must be the only entry");
        }

        return new Allowlist(entries, entries.Select(ParseEntry).ToArray());
    }

    // A 26-char [a-z0-9] entry is read as a channel id, never as a team name.
    private static AllowlistEntry ParseEntry(string entry)
    {
        if (entry == "*")
        {
            return new AllEntry();
        }

        if (entry.StartsWith('@'))
        {
            return UserName().IsMatch(entry[1..]) ? new DirectEntry(entry[1..]) : throw Invalid(entry);
        }

        if (entry.Contains('/'))
        {
            var parts = entry.Split('/');
            return parts.Length == 2 && Slug().IsMatch(parts[0]) && Slug().IsMatch(parts[1])
                ? new ChannelEntry(parts[0], parts[1])
                : throw Invalid(entry);
        }

        if (Patterns.Id.IsMatch(entry))
        {
            return new ChannelIdEntry(entry);
        }

        return Slug().IsMatch(entry) ? new TeamEntry(entry) : throw Invalid(entry);
    }

    private static ConfigurationException Invalid(string entry) => new($"{Variable}: invalid entry '{entry}'");

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex Slug();

    [GeneratedRegex("^[a-z0-9._-]{1,64}$")]
    private static partial Regex UserName();
}
