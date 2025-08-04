using SeacoreCommon.Messages;
using SeacoreClient.Core;

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
            Task.Run(() =>
            {
                while (true)
                {
                    string command = Console.ReadLine();
                    if (string.IsNullOrEmpty(command))
                        continue;

                    MessageBase message = command.ToUpper() switch
                    {
                        "HEARTBEAT" => new HeartbeatMessage { ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                        "DISCONNECT" => new DisconnectMessage(),
                        "RECONNECT" => new ReconnectMessage(),
                        "CHROMIUM_RECOVERY" => new ChromiumRecoveryMessage(),
                        _ => new UnknownMessage { RawMessage = command },
                    };

                    clientManager.SendMessage(message);
                }
            });
        }
    }
}