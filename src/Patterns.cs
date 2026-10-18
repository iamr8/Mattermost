using System.Text.RegularExpressions;

namespace MattermostMcp;

/// <summary>Shapes checked before a value goes into a URL path or a search term.</summary>
public static partial class Patterns
{
    /// <summary>A Mattermost id: 26 lowercase letters and digits.</summary>
    public static Regex Id { get; } = IdRegex();

    public static Regex Username { get; } = UsernameRegex();

    [GeneratedRegex("^[a-z0-9]{26}$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^[a-z0-9._-]{1,64}$")]
    private static partial Regex UsernameRegex();
}
