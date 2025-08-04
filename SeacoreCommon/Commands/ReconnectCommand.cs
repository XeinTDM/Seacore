using SeacoreCommon.Messages;
using System.Net.Sockets;
using MessagePack;
using SeacoreCommon.Utilities;

namespace SeacoreCommon.Commands
{
    public class ReconnectCommand : ICommand
    {
        private static readonly MessagePackSerializerOptions SerializerOptions = SerializerOptionsProvider.Options;

        public bool Execute(NetworkStream stream, MessageBase message)
        {
            var reconnectMessage = new ReconnectMessage();
            MessagePackSerializer.Serialize(stream, reconnectMessage, SerializerOptions);
            return true;
        }
    }
}
