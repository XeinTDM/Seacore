using Seacore.Resources.Usercontrols.Servers;
using System.Windows.Threading;
using System.Windows.Controls;
using Seacore.Resources.Core;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;

namespace Seacore.Resources.Usercontrols
{
    public partial class Server : UserControl
    {
        private DispatcherTimer uptimeTimer;

        public Server()
        {
            InitializeComponent();
            Loaded += Server_Loaded;
            serverListView.ContextMenuOpening += Server_ContextMenuOpening;
        }

        private void Server_Loaded(object sender, RoutedEventArgs e)
        {
            serverListView.ItemsSource = ServerStateManager.Instance.ServerInfoList;
            StartUptimeTimer();
        }

        private void OnStartListeningButtonClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(portInput.Text, out var parsedPort))
            {
                ServerStateManager.Instance.StartListeningOnPort(parsedPort);
                portInput.Clear();
            }
        }

        private void Server_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (serverListView.SelectedItem is ServerInfo selectedClient)
            {
                menuItemListenInactive.Header = selectedClient.Status == "Listening" ? "Inactivate" : "Listen";
            }
            else
            {
                menuItemListenInactive.Header = "Listen";
            }
        }

        private void OnEditNameMenuClick(object sender, RoutedEventArgs e)
        {
            if (serverListView.SelectedItem is ServerInfo selectedServer)
            {
                var window = new Servers.Windows.EditServerValueWindow(
                    title: "Rename Server",
                    prompt: "Enter new server name:",
                    defaultValue: selectedServer.Name
                )
                {
                    Owner = Application.Current.MainWindow
                };

                if (window.ShowDialog() == true && !string.IsNullOrEmpty(window.Result))
                {
                    selectedServer.Name = window.Result;
                    ServerStateManager.Instance.SaveServersToConfig(force: true);
                }
            }
        }

        private void OnEditPortMenuClick(object sender, RoutedEventArgs e)
        {
            if (serverListView.SelectedItem is ServerInfo selectedServer)
            {
                var window = new Servers.Windows.EditServerValueWindow(
                    title: "Change Server Port",
                    prompt: "Enter new server port:",
                    defaultValue: selectedServer.Port
                )
                {
                    Owner = Application.Current.MainWindow
                };

                if (window.ShowDialog() == true && !string.IsNullOrEmpty(window.Result))
                {
                    if (int.TryParse(window.Result, out int newPort) && newPort >= 1024 && newPort <= 65535)
                    {
                        var oldPort = int.Parse(selectedServer.Port);
                        if (newPort != oldPort)
                        {
                            var manager = TcpConnectionManager.Instance;
                            bool wasListening = selectedServer.Status == "Listening";

                            if (wasListening && manager.IsPortActive(oldPort))
                                manager.Stop(oldPort);

                            selectedServer.Port = newPort.ToString();

                            if (wasListening && !manager.IsPortActive(newPort))
                                manager.Start(newPort);

                            ServerStateManager.Instance.SaveServersToConfig(force: true);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please enter a valid port number (1024-65535).", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void OnToggleListenInactiveMenuClick(object sender, RoutedEventArgs e)
        {
            if (serverListView.SelectedItem is ServerInfo selectedClient)
            {
                ServerStateManager.Instance.ToggleServerStatus(selectedClient);
            }
        }

        private void OnDeleteMenuClick(object sender, RoutedEventArgs e)
        {
            if (serverListView.SelectedItem is ServerInfo selectedClient)
            {
                ServerStateManager.Instance.DeleteServer(selectedClient);
            }
        }

        private void StartUptimeTimer()
        {
            uptimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            uptimeTimer.Tick += (s, _) => ServerStateManager.Instance.UpdateUptime();
            uptimeTimer.Start();
        }

        private void PortInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void PortInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(portInput.Text, out int parsedPort) || parsedPort < 1024 || parsedPort > 65535)
            {
                portInput.ToolTip = "Port must be a number between 1024 and 65535.";
                portInput.BorderBrush = Brushes.Red;
                portInput.BorderThickness = new Thickness(2);
            }
            else
            {
                portInput.ToolTip = null;
                portInput.ClearValue(BorderBrushProperty);
                portInput.ClearValue(BorderThicknessProperty);
            }
        }
    }
}
