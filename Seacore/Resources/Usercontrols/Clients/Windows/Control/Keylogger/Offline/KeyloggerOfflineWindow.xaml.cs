using Seacore.Resources.Styles.Behaviours;
using System.Windows;
using System.Windows.Input;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Control.Keylogger
{
    public partial class KeyloggerOfflineWindow : Window
    {
        public KeyloggerOfflineWindow()
        {
            InitializeComponent();
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
