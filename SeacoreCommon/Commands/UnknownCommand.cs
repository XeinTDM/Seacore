using SeacoreCommon.Messages;
using System.Net.Sockets;

namespace SeacoreCommon.Commands
{
    public class UnknownCommand : ICommand
    {
        public bool Execute(NetworkStream stream, MessageBase message)
        {
            return true;
        }
    }
}
