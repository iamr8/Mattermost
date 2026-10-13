namespace MattermostMcp.Tests.Fakes;

/// <summary>The fixed refusal texts, written out literally; the SDK prefixes its own text, so tests match the end.</summary>
internal static class Msg
{
    public static string Unavailable(string input) => $"channel not available: {input} (not a member, or outside the allowlist)";

    public const string PostUnavailable = "post not available";
}
