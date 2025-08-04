using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Commands;
using SeacoreCommon.Messages;
using System.Net.Sockets;
using Serilog;

namespace Seacore.Resources.Core.Commands
{
    namespace Seacore.Resources.Core.Commands
    {
        public class HeartbeatCommand(TcpClient client, HeartbeatManager heartbeatManager, ClientInfo clientInfo) : ICommand
        {
            private readonly HeartbeatManager heartbeatManager = heartbeatManager;
            private readonly TcpClient client = client;
            private readonly ClientInfo clientInfo = clientInfo;

            public bool Execute(NetworkStream stream, MessageBase message)
            {
                if (message is HeartbeatMessage heartbeatMessage)
                {
                    var serverTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var ackMessage = new HeartbeatAckMessage
                    {
                        ClientTimestamp = heartbeatMessage.ClientTimestamp,
                        ServerTimestamp = serverTimestamp
                    };

                    TcpConnectionManager.SendMessage(stream, ackMessage);

                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    clientInfo.Ping = now - heartbeatMessage.ClientTimestamp;

                    heartbeatManager.RefreshLastHeartbeat(client);

                    Log.Information("Sent HeartbeatAckMessage to client.");

                    return true;
                }
                return false;
            }
        }
    }
}
