using Microsoft.Extensions.Configuration;
using SeacoreClient.Core;

class Program
{
    static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("settings.json", optional: false, reloadOnChange: true)
            .Build();

        var heartbeatConfig = configuration.GetSection("HeartbeatConfig").Get<HeartbeatConfig>() ?? new HeartbeatConfig();
        var serverConfig = configuration.GetSection("Server").Get<ServerConfig>() ?? new ServerConfig();
        serverConfig.Validate();

        using var clientManager = new TcpClientManager(serverConfig.Host, serverConfig.Port)
        {
            HeartbeatConfig = heartbeatConfig
        };

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            clientManager.Stop();
        };

        try
        {
            await clientManager.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Client shutdown requested.");
        }
        finally
        {
            Console.WriteLine("Client stopped.");
        }
    }
}
