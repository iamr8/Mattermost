namespace MattermostMcp.Mattermost;

public static class ChannelNames
{
    /// <summary>The name of a direct channel: both user ids, ascending, joined by two underscores.</summary>
    public static string DirectName(string userA, string userB)
        => string.CompareOrdinal(userA, userB) <= 0 ? $"{userA}__{userB}" : $"{userB}__{userA}";

    /// <summary>The other member of a direct channel (the user itself for a self-DM).</summary>
    public static string? OtherUserId(Channel direct, string meId)
    {
        var parts = direct.Name.Split("__");
        if (parts.Length != 2)
        {
            return null;
        }

        return parts[0] == meId ? parts[1] : parts[0];
    }
}
