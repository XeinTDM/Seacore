using Seacore.Resources.Styles.Behaviours;
using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Messages;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Control.RemoteDesktop
{
    public partial class RemoteDesktopWindow : Window, IRemoteDesktopSubscriber
    {
        private readonly ClientInfo clientInfo;

        public RemoteDesktopWindow(ClientInfo clientInfo)
        {
            this.clientInfo = clientInfo ?? throw new ArgumentNullException(nameof(clientInfo));

            InitializeComponent();

            Loaded += RemoteDesktopWindow_Loaded;
            Closed += RemoteDesktopWindow_Closed;

            clientNameTextBlock.Text = string.IsNullOrWhiteSpace(clientInfo.Username)
                ? "Remote Desktop"
                : $"Remote Desktop - {clientInfo.Username}";
        }

        private void RemoteDesktopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            statusTextBlock.Text = "Requesting remote desktop stream...";
            RemoteDesktopSessionManager.Instance.Register(clientInfo, this);
            RemoteDesktopSessionManager.Instance.StartSession(clientInfo);
        }

        private void RemoteDesktopWindow_Closed(object? sender, EventArgs e)
        {
            RemoteDesktopSessionManager.Instance.Unregister(clientInfo, this, "Window closed");
        }

        public void OnFrameReceived(ClientInfo client, RemoteDesktopFrameMessage frame)
        {
            if (!ReferenceEquals(client, clientInfo))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (frame.ImageData?.Length > 0)
                {
                    try
                    {
                        using var stream = new MemoryStream(frame.ImageData);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        remoteDesktopImage.Source = bitmap;

                        DateTime timestamp = frame.Timestamp > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(frame.Timestamp).LocalDateTime
                            : DateTime.Now;
                        statusTextBlock.Text = $"Resolution: {frame.Width}x{frame.Height} | Updated: {timestamp:HH:mm:ss}";
                    }
                    catch (Exception ex)
                    {
                        statusTextBlock.Text = $"Failed to render frame: {ex.Message}";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(frame.StatusMessage))
                {
                    remoteDesktopImage.Source = null;
                    statusTextBlock.Text = frame.StatusMessage;
                }
            });
        }

        public void OnSessionStarted(ClientInfo client)
        {
            if (!ReferenceEquals(client, clientInfo))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                statusTextBlock.Text = "Awaiting first frame...";
            });
        }

        public void OnSessionStopped(ClientInfo client, string reason)
        {
            if (!ReferenceEquals(client, clientInfo))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    statusTextBlock.Text = reason;
                }
            });
        }

        #region Behaviours
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowBehaviours.Border_MouseLeftButtonDown(sender, e, this);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowBehaviours.MinimizeButton_Click(sender, e, this);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            WindowBehaviours.CloseButton_Click(sender, e, this);
        }
        #endregion
    }
}
