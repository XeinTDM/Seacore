using SeacoreCommon.Messages;
using System.Net.Sockets;

namespace SeacoreCommon.Commands
{
    public interface ICommand
    {
        bool Execute(NetworkStream stream, MessageBase message);
    }
}
