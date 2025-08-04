using Seacore.Resources.Usercontrols.Settings.Pages;
using System.Windows.Controls;
using System.Windows;

namespace Seacore.Resources.Usercontrols
{
    public partial class Setting : UserControl
    {
        public Setting()
        {
            InitializeComponent();
            BackButtonControl.Visibility = Visibility.Collapsed;
            BackButtonControl.BackClicked += BackButtonControl_BackClicked;
        }

        private void NavigateToUserControl(UserControl control)
        {
            ContentFrame.Content = control;
            ContentFrame.Visibility = Visibility.Visible;
            SettingsButtonsPanel.Visibility = Visibility.Collapsed;
            BackButtonControl.Visibility = Visibility.Visible;
        }

        private void BackButtonControl_BackClicked(object? sender, EventArgs e)
        {
            ContentFrame.Content = null;
            ContentFrame.Visibility = Visibility.Collapsed;
            SettingsButtonsPanel.Visibility = Visibility.Visible;
            BackButtonControl.Visibility = Visibility.Collapsed;
        }

        private void AccountButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUserControl(new AccountPage());
        }

        private void GeneralButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUserControl(new GeneralPage());
        }

        private void SecurityButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUserControl(new SecurityPage());
        }

        private void AppearanceButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUserControl(new AppearancePage());
        }

        private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUserControl(new AdvancedPage());
        }

        private void FeedbackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUserControl(new FeedbackPage());
        }
    }
}