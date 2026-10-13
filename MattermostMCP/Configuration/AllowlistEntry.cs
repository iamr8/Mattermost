namespace MattermostMcp.Configuration;

/// <summary>One entry of MATTERMOST_ALLOWLIST. <see cref="Raw"/> is the text the user wrote.</summary>
public abstract record AllowlistEntry
{
    public abstract string Raw { get; }
}

public sealed record AllEntry : AllowlistEntry
{
    public override string Raw => "*";
}

public sealed record TeamEntry(string Team) : AllowlistEntry
{
    public override string Raw => Team;
}

public sealed record ChannelEntry(string Team, string Channel) : AllowlistEntry
{
    public override string Raw => $"{Team}/{Channel}";
}

public sealed record DirectEntry(string Username) : AllowlistEntry
{
    public override string Raw => $"@{Username}";
}

public sealed record ChannelIdEntry(string Id) : AllowlistEntry
{
    public override string Raw => Id;
}
