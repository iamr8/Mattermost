using System.IO.Pipelines;
using MattermostMcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MattermostMcp.Tests.Fakes;

/// <summary>The real server pipeline over in-memory pipes, with the fake Mattermost behind it.</summary>
internal sealed class TestHost : IAsyncDisposable
{
    public const string Token = "tok-SENTINEL-8f3a1c";

    private readonly Pipe _clientToServer;
    private readonly Pipe _serverToClient;

    private TestHost(IHost host, FakeMattermostHandler handler, CapturingLoggerProvider logs, Pipe clientToServer, Pipe serverToClient)
    {
        Host = host;
        Handler = handler;
        Logs = logs;
        _clientToServer = clientToServer;
        _serverToClient = serverToClient;
    }

    public IHost Host { get; }
    public FakeMattermostHandler Handler { get; }
    public CapturingLoggerProvider Logs { get; }
    public McpClient? Client { get; private set; }

    public async Task<McpClient> StartAsync(CancellationToken cancellationToken = default)
    {
        await BootstrapAsync(cancellationToken);
        return await ConnectAsync(cancellationToken);
    }

    public T Service<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    /// <summary>Calls a tool; returns whether it failed and the first text block.</summary>
    public async Task<(bool IsError, string Text)> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        var result = await Client!.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        return (result.IsError == true, string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text)));
    }

    public static MattermostOptions DefaultOptions(string allowlist = "*", string url = "https://chat.example.com")
        => MattermostOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["MATTERMOST_URL"] = url,
            ["MATTERMOST_TOKEN"] = Token,
            ["MATTERMOST_ALLOWLIST"] = allowlist,
        }.GetValueOrDefault);

    public ManualTimeProvider Time { get; private init; } = null!;

    /// <summary>Me, teams, channels and user lookups, as a user in teams dev and ops would see them.</summary>
    public static FakeMattermostHandler StandardMattermost()
        => new FakeMattermostHandler()
            .RespondWithFixture(HttpMethod.Get, "/api/v4/users/me", "me.json")
            .RespondWithFixture(HttpMethod.Get, $"/api/v4/users/{Ids.Alice}/teams", "teams.json")
            .RespondWithFixture(HttpMethod.Get, $"/api/v4/users/{Ids.Alice}/channels", "channels.json")
            .RespondUsersByIds()
            .Respond(HttpMethod.Get, "/api/v4/users/username/bob", _ => Mm.Response(System.Net.HttpStatusCode.OK, Mm.Json(new { id = Ids.Bob, username = "bob" })));

    public static TestHost Create(FakeMattermostHandler handler, MattermostOptions? options = null)
        => Create(handler, new ManualTimeProvider(), options);

    public static TestHost Create(FakeMattermostHandler handler, ManualTimeProvider time, MattermostOptions? options = null)
    {
        options ??= DefaultOptions();
        var logs = new CapturingLoggerProvider();
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        builder.Services
            .AddMattermostMcp(options)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());

        // Added after the production registration, so these replace the real handler and clock.
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddHttpClient(MattermostHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.Services.AddHttpClient(MattermostHttp.BootstrapClientName).ConfigurePrimaryHttpMessageHandler(() => handler);

        return new TestHost(builder.Build(), handler, logs, clientToServer, serverToClient) { Time = time };
    }

    public Task BootstrapAsync(CancellationToken cancellationToken = default)
        => Host.Services.GetRequiredService<Bootstrapper>().RunAsync(cancellationToken);

    public async Task<McpClient> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await Host.StartAsync(cancellationToken);
        var transport = new StreamClientTransport(_clientToServer.Writer.AsStream(), _serverToClient.Reader.AsStream());
        Client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        return Client;
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }

        await Host.StopAsync();
        Host.Dispose();
    }
}
