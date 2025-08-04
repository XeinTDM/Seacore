using System.Windows;
using System.Windows.Controls;

namespace Seacore.Resources.Usercontrols.Settings.Styles
{
    public partial class BackButtonControl : UserControl
    {
        public BackButtonControl()
        {
            InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            OnBackClicked();
        }

        public event EventHandler? BackClicked;

        protected virtual void OnBackClicked()
        {
            BackClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}