using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Messages;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Control.RemoteDesktop
{
    public interface IRemoteDesktopSubscriber
    {
        void OnFrameReceived(ClientInfo clientInfo, RemoteDesktopFrameMessage frame);
        void OnSessionStarted(ClientInfo clientInfo);
        void OnSessionStopped(ClientInfo clientInfo, string reason);
    }
}
