using Seacore.Resources.Styles.Behaviours;
using Seacore.Resources.Usercontrols;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using Serilog;

namespace Seacore
{
    public partial class MainWindow : Window
    {
        private Dictionary<string, Func<UserControl>> toggleButtonMappings = [];
        private bool isBlurEnabled = true;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            InitializeToggleButtonMappings();
            ToggleButtonBehaviours.InitializeToggleButtons([btnDashboard, btnClient, btnServer, btnPlugins, btnSetting], ToggleButton_Checked, ToggleButton_Unchecked, ToggleButton_PreviewMouseLeftButtonDown);
            btnDashboard.IsChecked = true;

            Log.Information("MainWindow initialized.");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BlurWindowEffect.EnableBlur(this, isBlurEnabled);
        }

        #region ToggleButton
        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            ToggleButtonBehaviours.ToggleButton_Checked(sender, e, toggleButtonMappings, ContentFrame);
        }

        private void ToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleButtonBehaviours.ToggleButton_Unchecked(sender, e, ContentFrame);
        }

        private void ToggleButton_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ToggleButtonBehaviours.ToggleButton_PreviewMouseLeftButtonDown(sender, e);
        }

        private void InitializeToggleButtonMappings()
        {
            toggleButtonMappings = new Dictionary<string, Func<UserControl>>
            {
                { nameof(btnDashboard), () => new Dashboard(ContentFrame, btnClient, btnServer) },
                { nameof(btnClient), () => new Client() },
                { nameof(btnServer), () => UserControlCache.GetOrCreate(() => new Server()) },
                { nameof(btnPlugins), () => new Plugins() },
                { nameof(btnSetting), () => new Setting() },
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
            WindowBehaviours.CloseButton_Click(sender, e, this, isMainWindow: true);
        }

        private void ToggleBlurButton_Click(object sender, RoutedEventArgs e)
        {
            WindowBehaviours.ToggleBlurButton_Click(sender, e, this, ref isBlurEnabled);
        }
        #endregion
    }
}