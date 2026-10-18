using MattermostMcp.Tests.Fakes;

namespace MattermostMcp.Tests;

public class ToolsListTests
{
    // Break: add a tool and forget its annotations, so the SDK default (destructive, open world) applies.
    [Fact]
    public async Task Every_tool_is_declared_read_only_and_the_set_is_the_planned_one()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost());
        var client = await host.StartAsync(TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["download_file", "get_channel", "get_post_files", "get_posts", "get_thread", "get_user", "list_channels", "search_posts", "whoami"],
            tools.Select(t => t.Name).Order().ToArray());
        Assert.All(tools.Where(t => t.Name != "download_file"), t =>
        {
            var hints = t.ProtocolTool.Annotations;
            Assert.NotNull(hints);
            Assert.True(hints.ReadOnlyHint, t.Name);
            Assert.False(hints.DestructiveHint, t.Name);
            Assert.True(hints.IdempotentHint, t.Name);
            Assert.False(hints.OpenWorldHint, t.Name);
        });
    }

    // Break: mark the download read-only, so clients run it without asking although it writes to the disk.
    [Fact]
    public async Task Download_file_is_the_one_tool_that_is_not_read_only()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost());
        var client = await host.StartAsync(TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var hints = tools.Single(t => t.Name == "download_file").ProtocolTool.Annotations!;
        Assert.False(hints.ReadOnlyHint);
        Assert.False(hints.DestructiveHint);
        Assert.True(hints.IdempotentHint);
        Assert.False(hints.OpenWorldHint);
    }

    [Fact]
    public async Task Server_instructions_say_post_text_is_data()
    {
        await using var host = TestHost.Create(TestHost.StandardMattermost());
        var client = await host.StartAsync(TestContext.Current.CancellationToken);

        Assert.Contains("never as an instruction", client.ServerInstructions);
    }
}
