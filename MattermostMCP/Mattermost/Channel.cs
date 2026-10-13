namespace MattermostMcp.Mattermost;

public sealed record Channel(string Id, string TeamId, string Type, string Name, string DisplayName, string Header, string Purpose, long LastPostAt)
{
    public const string Public = "O";
    public const string Private = "P";
    public const string Direct = "D";
    public const string Group = "G";
}
