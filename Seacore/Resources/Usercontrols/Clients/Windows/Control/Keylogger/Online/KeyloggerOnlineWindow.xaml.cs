using Seacore.Resources.Styles.Behaviours;
using System.Windows.Input;
using System.Windows;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Control.Keylogger.Online
{
    public partial class KeyloggerOnlineWindow : Window
    {
        public KeyloggerOnlineWindow()
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
