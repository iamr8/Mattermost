using System.Diagnostics;

namespace MattermostMcp.Tests;

public class SubprocessTests
{
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunServerAsync(Dictionary<string, string> env, CancellationToken cancellationToken)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "MattermostMCP.dll");
        var info = new ProcessStartInfo("dotnet", [dll])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var name in new[] { "MATTERMOST_URL", "MATTERMOST_TOKEN", "MATTERMOST_ALLOWLIST" })
        {
            info.Environment.Remove(name);
        }

        foreach (var (name, value) in env)
        {
            info.Environment[name] = value;
        }

        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(60)); // safety net only
        await process.WaitForExitAsync(guard.Token);

        return (process.ExitCode, await stdout, await stderr);
    }

    // Break: a stray Console.WriteLine, or lazy validation (process starts and waits on stdin).
    [Fact]
    public async Task Missing_environment_exits_1_naming_the_variable_and_writes_nothing_to_stdout()
    {
        var (exitCode, stdout, stderr) = await RunServerAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("MATTERMOST_URL", stderr);
    }

    // Break: log to stdout (the default console target), which corrupts the MCP stream.
    // Port 1 on loopback refuses at once, so no real server is needed.
    [Fact]
    public async Task Unreachable_server_exits_1_and_logs_only_to_stderr()
    {
        var env = new Dictionary<string, string>
        {
            ["MATTERMOST_URL"] = "http://127.0.0.1:1",
            ["MATTERMOST_TOKEN"] = "tok-SENTINEL-8f3a1c",
            ["MATTERMOST_ALLOWLIST"] = "*",
        };

        var (exitCode, stdout, stderr) = await RunServerAsync(env, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("Connecting to 127.0.0.1:1", stderr);
        Assert.Contains("cannot reach 127.0.0.1:1", stderr);
        Assert.DoesNotContain("tok-SENTINEL-8f3a1c", stderr);
    }
}
