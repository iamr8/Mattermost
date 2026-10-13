namespace MattermostMcp.Tests.Fakes;

/// <summary>The fixture ids. Each is 26 chars, like a real Mattermost id.</summary>
internal static class Ids
{
    public const string Alice = "u1aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string Bob = "u2bbbbbbbbbbbbbbbbbbbbbbbb";
    public const string Carol = "u3cccccccccccccccccccccccc";

    public const string TeamDev = "t1aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string TeamOps = "t2aaaaaaaaaaaaaaaaaaaaaaaa";

    public const string DevGeneral = "c1aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string DevRandom = "c2aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string OpsGeneral = "c3aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string OpsSecret = "c4aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string DmAliceBob = "c5aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string DmAliceCarol = "c6aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string GroupAll = "c7aaaaaaaaaaaaaaaaaaaaaaaa";

    public static string Post(int n) => $"p{n}" + new string('a', 24);
}
