using System.Threading.Tasks;
using SeacoreClient.Core;
using SeacoreCommon.Messages;

namespace SeacoreClient.Handlers
{
    public class CommandHandler
    {
        private readonly TcpClientManager clientManager;

        public CommandHandler(TcpClientManager clientManager)
        {
            this.clientManager = clientManager;
            HandleCommands();
        }

        private void HandleCommands()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    string? command = Console.ReadLine();
                    if (string.IsNullOrEmpty(command))
                    {
                        continue;
                    }

                    MessageBase message = command.ToUpperInvariant() switch
                    {
                        "HEARTBEAT" => new HeartbeatMessage { ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                        "DISCONNECT" => new DisconnectMessage(),
                        "RECONNECT" => new ReconnectMessage(),
                        "CHROMIUM_RECOVERY" => new ChromiumRecoveryMessage(),
                        _ => new UnknownMessage { RawMessage = command },
                    };

                    try
                    {
                        await clientManager.SendMessageAsync(message);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send command '{command}': {ex.Message}");
                    }
                }
            });
        }
    }
}
