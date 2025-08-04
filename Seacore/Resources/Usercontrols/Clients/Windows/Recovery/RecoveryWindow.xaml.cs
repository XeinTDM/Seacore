using Seacore.Resources.Usercontrols.Clients.Windows.Recovery.UC;
using Seacore.Resources.Styles.Behaviours;
using System.Windows.Controls;
using System.Windows.Input;
using System.Net.Sockets;
using System.Windows;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Recovery
{
    public partial class RecoveryWindow : Window
    {
        private Dictionary<string, Func<UserControl>> toggleButtonMappings = [];
        private readonly bool isBlurEnabled = true;
        private readonly ClientInfo clientInfo;
        private TcpClient _client;
        public RecoveryWindow(ClientInfo clientInfo)
        {
            InitializeComponent();
            _client = clientInfo.TcpClient;
            Loaded += MainWindow_Loaded;
            this.clientInfo = clientInfo;
            InitializeToggleButtonMappings();
            ToggleButtonBehaviours.InitializeToggleButtons([btnDashboard, btnLogs, btnAnalytics], ToggleButton_Checked, ToggleButton_Unchecked, ToggleButton_PreviewMouseLeftButtonDown);
            btnDashboard.IsChecked = true;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BlurWindowEffect.EnableBlur(this, isBlurEnabled);
        }

        #region ToggleButton
        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            ToggleButtonBehaviours.ToggleButton_Checked(sender, e, toggleButtonMappings, FrameContent);
        }

        private void ToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleButtonBehaviours.ToggleButton_Unchecked(sender, e, FrameContent);
        }

        private void ToggleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleButtonBehaviours.ToggleButton_PreviewMouseLeftButtonDown(sender, e);
        }

        private void InitializeToggleButtonMappings()
        {
            toggleButtonMappings = new Dictionary<string, Func<UserControl>>
            {
                { nameof(btnDashboard), () => new RecoveryDashboardControl() { ClientInfo = clientInfo } },
                { nameof(btnLogs), () => new RecoveryLogsControl() },
                { nameof(btnAnalytics), () => new RecoveryAnalyticsControl() },
            };
        }
        #endregion

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