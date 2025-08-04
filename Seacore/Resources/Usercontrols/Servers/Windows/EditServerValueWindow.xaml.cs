using System.Windows;

namespace Seacore.Resources.Usercontrols.Servers.Windows
{
    public partial class EditServerValueWindow : Window
    {
        public string TitleText { get; }
        public string Prompt { get; }
        public string DefaultValue { get; }

        public string? Result { get; private set; }

        public EditServerValueWindow(string title, string prompt, string defaultValue)
        {
            InitializeComponent();
            TitleText = title;
            Prompt = prompt;
            DefaultValue = defaultValue;

            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ValueTextBox.Text = DefaultValue;
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ValueTextBox.Text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
