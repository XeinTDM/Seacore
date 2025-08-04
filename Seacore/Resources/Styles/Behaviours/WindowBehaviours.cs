using System.Windows;
using System.Windows.Input;

namespace Seacore.Resources.Styles.Behaviours
{
    public static class WindowBehaviours
    {
        public static void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e, Window window)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                window.DragMove();
            }
        }

        public static void MinimizeButton_Click(object sender, RoutedEventArgs e, Window window)
        {
            window.WindowState = WindowState.Minimized;
        }

        public static void CloseButton_Click(object sender, RoutedEventArgs e, Window window, bool isMainWindow = false)
        {
            if (isMainWindow)
            {
                Application.Current.Shutdown();
            }
            else
            {
                window.Close();
            }
        }

        public static void ToggleBlurButton_Click(object sender, RoutedEventArgs e, Window window, ref bool isBlurEnabled)
        {
            isBlurEnabled = !isBlurEnabled;
            BlurWindowEffect.EnableBlur(window, isBlurEnabled);
        }
    }
}