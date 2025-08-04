using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Seacore.Resources.Usercontrols
{
    public partial class Dashboard : UserControl
    {
        private Frame mainContentFrame;
        private ToggleButton clientToggleButton;
        private ToggleButton serverToggleButton;

        public Dashboard(Frame contentFrame, ToggleButton clientToggleButton, ToggleButton serverToggleButton)
        {
            InitializeComponent();
            mainContentFrame = contentFrame;
            this.clientToggleButton = clientToggleButton;
            this.serverToggleButton = serverToggleButton;
        }

        private void DBclientsBtn_Click(object sender, RoutedEventArgs e)
        {
            var clientControl = new Client();
            mainContentFrame.Content = clientControl;

            clientToggleButton.IsChecked = true;
        }

        private void DBserverBtn_Click(object sender, RoutedEventArgs e)
        {
            var serverControl = new Server();
            mainContentFrame.Content = serverControl;

            serverToggleButton.IsChecked = true;
        }
    }
}