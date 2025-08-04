using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Seacore.Resources.Styles.Behaviours
{
    public static class ToggleButtonBehaviours
    {
        public static void InitializeToggleButtons(ToggleButton[] toggleButtons, Action<object, RoutedEventArgs> checkedAction, Action<object, RoutedEventArgs> uncheckedAction, Action<object, System.Windows.Input.MouseButtonEventArgs> previewMouseLeftButtonDownAction)
        {
            foreach (var toggleButton in toggleButtons)
            {
                toggleButton.Checked += new RoutedEventHandler(checkedAction);
                toggleButton.Unchecked += new RoutedEventHandler(uncheckedAction);
                toggleButton.PreviewMouseLeftButtonDown += new System.Windows.Input.MouseButtonEventHandler(previewMouseLeftButtonDownAction);
            }
        }

        public static void InitializeToggleButtonMappings(Dictionary<string, Func<UserControl>> toggleButtonMappings, Frame ContentFrame, Dictionary<string, ToggleButton> buttons)
        {
            foreach (var button in toggleButtonMappings.Keys)
            {
                if (buttons.TryGetValue(button, out ToggleButton toggleButton))
                {
                    toggleButton.IsChecked = false;
                }
            }
        }

        public static void ToggleButton_Checked(object sender, RoutedEventArgs e, Dictionary<string, Func<UserControl>> toggleButtonMappings, Frame ContentFrame)
        {
            if (sender is ToggleButton checkedButton)
            {
                UncheckAllToggleButtons(checkedButton, toggleButtonMappings, ContentFrame);

                if (toggleButtonMappings.TryGetValue(checkedButton.Name, out var createControl))
                {
                    ContentFrame.Content = createControl();
                }
            }
        }

        public static void ToggleButton_Unchecked(object sender, RoutedEventArgs e, Frame ContentFrame)
        {
            ContentFrame.Content = null;
        }

        public static void ToggleButton_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.IsChecked == true)
            {
                e.Handled = true;
            }
        }

        private static void UncheckAllToggleButtons(ToggleButton? except, Dictionary<string, Func<UserControl>> toggleButtonMappings, Frame ContentFrame)
        {
            foreach (var button in toggleButtonMappings.Keys)
            {
                if (ContentFrame.FindName(button) is ToggleButton toggleButton && toggleButton != except)
                {
                    toggleButton.IsChecked = false;
                }
            }
        }
    }
}