using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Commands;
using SeacoreCommon.Messages;
using System.Net.Sockets;

namespace Seacore.Resources.Core.Commands
{
    public class ClientIdentificationCommand : ICommand
    {
        private readonly ClientInfo clientInfo;

        public ClientIdentificationCommand(ClientInfo clientInfo)
        {
            this.clientInfo = clientInfo;
        }

        public bool Execute(NetworkStream stream, MessageBase message)
        {
            if (message is ClientIdentificationMessage identificationMessage)
            {
                clientInfo.Username = identificationMessage.Username;
                clientInfo.OS = identificationMessage.OS;
                clientInfo.PublicIP = identificationMessage.PublicIP;

                clientInfo.OSImg = identificationMessage.OS switch
                {
                    "Win 10" => "pack://application:,,,/Resources/Assets/Icons/win10.png",
                    "Win 11" => "pack://application:,,,/Resources/Assets/Icons/win11.png",
                    "Linux" => "pack://application:,,,/Resources/Assets/Icons/linux.png",
                    "Mac" => "pack://application:,,,/Resources/Assets/Icons/mac.png",
                    _ => "",
                };

                Task.Run(() => TcpConnectionManager.Instance.FetchClientLocationAsync(clientInfo));

                return true;
            }
            return false;
        }
    }
}
