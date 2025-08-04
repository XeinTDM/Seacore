using Seacore.Resources.Usercontrols.Clients;
using Seacore.Resources.Core.Commands;
using SeacoreCommon.Commands;
using SeacoreCommon.Messages;
using System.Net.Sockets;

namespace Seacore.Resources.Core
{
    public static class MessageProcessor
    {
        public static bool ProcessMessage(MessageBase message, TcpClient client, NetworkStream stream, HeartbeatManager heartbeatManager, ClientInfo clientInfo)
        {
            ICommand command = CommandFactory.GetCommand(message, client, heartbeatManager, clientInfo);
            return command.Execute(stream, message);
        }
    }
}
