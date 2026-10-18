using MattermostMcp;
using MattermostMcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

MattermostOptions options;
try
{
    options = MattermostOptions.FromEnvironment(Environment.GetEnvironmentVariable);
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol; every log line goes to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services
    .AddMattermostMcp(options)
    .WithStdioServerTransport();

using var host = builder.Build();

try
{
    await host.Services.GetRequiredService<Bootstrapper>().RunAsync(CancellationToken.None);
}
catch (StartupException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

await host.RunAsync();
return 0;
