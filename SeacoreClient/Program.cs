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

        var heartbeatConfig = configuration.GetSection("HeartbeatConfig").Get<HeartbeatConfig>();

        var clientManager = new TcpClientManager("127.0.0.1", 2332)
        {
            HeartbeatConfig = heartbeatConfig
        };
        await clientManager.RunAsync();

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}