using MattermostMcp.Tests.Fakes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MattermostMcp.Tests;

/// <summary>The real server process over stdio against a stub HTTP server. No real Mattermost is involved.</summary>
public class SubprocessWriteTests
{
    private static async Task<McpClient> ConnectAsync(StubMattermostServer stub, string? readOnly, CancellationToken cancellationToken)
    {
        var env = new Dictionary<string, string?>
        {
            ["MATTERMOST_URL"] = stub.Url,
            ["MATTERMOST_TOKEN"] = TestHost.Token,
            ["MATTERMOST_ALLOWLIST"] = "dev",
            ["MATTERMOST_READ_ONLY"] = readOnly,
        };
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "MattermostMCP.dll")],
            EnvironmentVariables = env,
        });
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static async Task<string> CallAsync(McpClient client, string tool, Dictionary<string, object?> args, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: cancellationToken);
        Assert.NotEqual(true, result.IsError);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
    }

    // Break: forget to register the write client, or to map its base address and token, in the real host.
    [Fact(Timeout = 90000)]
    public async Task Post_reply_react_and_edit_work_end_to_end_in_the_real_process()
    {
        var token = TestContext.Current.CancellationToken;
        using var stub = new StubMattermostServer();
        await using var client = await ConnectAsync(stub, "false", token);

        await CallAsync(client, "post_message", new() { ["channel"] = "dev/general", ["message"] = "one" }, token);
        await CallAsync(client, "reply_to_thread", new() { ["post_id"] = Ids.Post(2), ["message"] = "two" }, token);
        await CallAsync(client, "add_reaction", new() { ["post_id"] = Ids.Post(2), ["emoji"] = ":thumbsup:" }, token);
        await CallAsync(client, "edit_post", new() { ["post_id"] = Ids.Post(2), ["message"] = "three" }, token);

        var writes = stub.Writes;
        Assert.Equal(
            [("POST", "/api/v4/posts"), ("POST", "/api/v4/posts"), ("POST", "/api/v4/reactions"), ("PUT", $"/api/v4/posts/{Ids.Post(2)}/patch")],
            writes.Select(w => (w.Method, w.Path)).ToArray());
        Assert.All(writes, w => Assert.Equal("Bearer " + TestHost.Token, w.Authorization));
    }

    // Break: register the write tools although the variable is unset.
    [Fact(Timeout = 90000)]
    public async Task Without_the_variable_the_real_process_has_no_write_tools_and_sends_no_write()
    {
        var token = TestContext.Current.CancellationToken;
        using var stub = new StubMattermostServer();
        await using var client = await ConnectAsync(stub, null, token);

        var tools = await client.ListToolsAsync(cancellationToken: token);

        Assert.DoesNotContain(tools, t => t.Name is "post_message" or "reply_to_thread" or "add_reaction" or "edit_post");
        Assert.Empty(stub.Writes);
    }
}
