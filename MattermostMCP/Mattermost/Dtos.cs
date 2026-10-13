namespace MattermostMcp.Mattermost;

public sealed record User(string Id, string Username);

public sealed record UserProfile(string Id, string Username, string? Nickname, string? FirstName, string? LastName, string? Position);

public sealed record Team(string Id, string Name, string DisplayName);

public sealed record Post
{
    public string Id { get; init; } = "";
    public long CreateAt { get; init; }
    public long EditAt { get; init; }
    public long DeleteAt { get; init; }
    public string UserId { get; init; } = "";
    public string ChannelId { get; init; } = "";
    public string RootId { get; init; } = "";
    public string Message { get; init; } = "";
    public int ReplyCount { get; init; }
    public List<string>? FileIds { get; init; }
}

/// <summary><c>Order</c> is the order; <c>Posts</c> is a map that may hold extra thread roots.</summary>
public sealed record PostList
{
    public List<string> Order { get; init; } = [];
    public Dictionary<string, Post> Posts { get; init; } = [];
    public string PrevPostId { get; init; } = "";
}
