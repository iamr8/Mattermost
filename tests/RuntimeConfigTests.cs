using System.Text.Json;

namespace MattermostMcp.Tests;

public class RuntimeConfigTests
{
    // Break: remove the setting from the csproj. Measured locally: it cuts the peak memory after heavy reads
    // from 105-134 MB to 101-103 MB, because the GC compacts the large-object heap sooner.
    [Fact]
    public void The_server_runs_with_the_conserve_memory_GC_setting()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MattermostMCP.runtimeconfig.json");

        using var json = JsonDocument.Parse(File.ReadAllText(path));

        var value = json.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties").GetProperty("System.GC.ConserveMemory");
        Assert.Equal(7, value.GetInt32());
    }
}
