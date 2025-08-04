using SeacoreCommon.Messages;
using System.Net.Sockets;
using MessagePack;
using SeacoreCommon.Utilities;

namespace SeacoreCommon.Commands
{
    public class DisconnectCommand : ICommand
    {
        private static readonly MessagePackSerializerOptions SerializerOptions = SerializerOptionsProvider.Options;

        public bool Execute(NetworkStream stream, MessageBase message)
        {
            var disconnectMessage = new DisconnectMessage();
            MessagePackSerializer.Serialize(stream, disconnectMessage, SerializerOptions);
            return false;
        }
    }
}
