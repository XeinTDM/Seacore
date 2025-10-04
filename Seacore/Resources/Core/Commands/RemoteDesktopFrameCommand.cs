using System.Net.Sockets;
using System.Windows;
using Seacore.Resources.Usercontrols.Clients;
using Seacore.Resources.Usercontrols.Clients.Windows.Control.RemoteDesktop;
using SeacoreCommon.Commands;
using SeacoreCommon.Messages;

namespace Seacore.Resources.Core.Commands
{
    public class RemoteDesktopFrameCommand : ICommand
    {
        private readonly ClientInfo clientInfo;

        public RemoteDesktopFrameCommand(ClientInfo clientInfo)
        {
            this.clientInfo = clientInfo;
        }

        public bool Execute(NetworkStream stream, MessageBase message)
        {
            if (message is RemoteDesktopFrameMessage frameMessage)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    RemoteDesktopSessionManager.Instance.HandleFrame(clientInfo, frameMessage);
                });

                return true;
            }

            return false;
        }
    }
}
