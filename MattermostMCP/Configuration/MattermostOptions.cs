namespace MattermostMcp.Configuration;

public sealed class MattermostOptions
{
    private const string UrlVariable = "MATTERMOST_URL";
    private const string TokenVariable = "MATTERMOST_TOKEN";
    private const string AllowlistVariable = "MATTERMOST_ALLOWLIST";
    private const string ReadOnlyVariable = "MATTERMOST_READ_ONLY";
    private const string ApiSuffix = "/api/v4";

    private MattermostOptions(Uri baseUri, string token, Allowlist allowlist, bool readOnly)
    {
        BaseUri = baseUri;
        Token = token;
        Allowlist = allowlist;
        ReadOnly = readOnly;
    }

    /// <summary>The API root, always ending in <c>/api/v4/</c>.</summary>
    public Uri BaseUri { get; }

    public string Token { get; }

    public Allowlist Allowlist { get; }

    /// <summary>True unless <c>MATTERMOST_READ_ONLY=false</c>. Write tools exist only when this is false.</summary>
    public bool ReadOnly { get; }

    public string Host => BaseUri.Authority;

    public static MattermostOptions FromEnvironment(Func<string, string?> get)
    {
        var url = Require(get, UrlVariable);
        var token = Require(get, TokenVariable);
        var allowlist = Require(get, AllowlistVariable);

        return new MattermostOptions(ParseUrl(url), token, Allowlist.Parse(allowlist), ParseReadOnly(get(ReadOnlyVariable)));
    }

    // The token is deliberately absent.
    public override string ToString() => $"MattermostOptions {{ Host = {Host}, Allowlist = {string.Join(",", Allowlist.Entries)}, ReadOnly = {ReadOnly} }}";

    // Unset means read-only. Only true or false are valid: a typo must not turn writes on.
    private static bool ParseReadOnly(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            null => true,
            "true" => true,
            "false" => false,
            _ => throw new ConfigurationException($"{ReadOnlyVariable} must be true or false"),
        };

    private static string Require(Func<string, string?> get, string name)
    {
        var value = get(name);
        return string.IsNullOrWhiteSpace(value) ? throw new ConfigurationException($"{name} is not set") : value.Trim();
    }

    private static Uri ParseUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            throw new ConfigurationException($"{UrlVariable} must be an absolute URL");
        }

        var secure = uri.Scheme == Uri.UriSchemeHttps;
        var loopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!secure && !loopbackHttp)
        {
            throw new ConfigurationException($"{UrlVariable} must use https (http is allowed only for localhost)");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(ApiSuffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^ApiSuffix.Length];
        }

        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port, path + ApiSuffix + "/").Uri;
    }
}
