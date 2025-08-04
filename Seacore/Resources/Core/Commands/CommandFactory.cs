using Seacore.Resources.Core.Commands.Seacore.Resources.Core.Commands;
using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Commands;
using SeacoreCommon.Messages;
using System.Net.Sockets;

namespace Seacore.Resources.Core.Commands
{
    public static class CommandFactory
    {
        public static ICommand GetCommand(MessageBase message, TcpClient client, HeartbeatManager heartbeatManager, ClientInfo clientInfo)
        {
            return message switch
            {
                HeartbeatMessage => new HeartbeatCommand(client, heartbeatManager, clientInfo),
                DisconnectMessage => new DisconnectCommand(),
                ReconnectMessage => new ReconnectCommand(),
                ChromiumRecoveryMessage => new ChromiumRecoveryCommand(),
                ClientIdentificationMessage => new ClientIdentificationCommand(clientInfo),
                _ => new UnknownCommand(),
            };
        }
    }
}
