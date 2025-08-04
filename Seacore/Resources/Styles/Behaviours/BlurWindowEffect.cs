using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;

namespace Seacore.Resources.Styles.Behaviours
{
    public static class BlurWindowEffect
    {
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_ENABLE_HOSTBACKDROP = 5,
            ACCENT_ENABLE_HOSTBACKDROPACRYLIC = 6,
            ACCENT_ENABLE_BLURREGION = 7,
            ACCENT_ENABLE_EXTENDED_DARK = 8,
            ACCENT_ENABLE_EXTENDED_ACYRLIC = 9,
            ACCENT_ENABLE_HOSTBACKDROPACRYLICLIGHT = 10,
            ACCENT_ENABLE_HOSTBACKDROPACRYLICDARK = 11,
            ACCENT_ENABLE_EXCLUDED_FROM_LIGHT_DARK = 12,
            ACCENT_ENABLE_TRANSPARENTGRADIENTBRUSH = 13,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState nAccentState;
            public int nFlags;
            public int nColor;
            public int nAnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        public static void EnableBlur(Window window, bool enable)
        {
            var windowHelper = new WindowInteropHelper(window);
            var accent = new AccentPolicy
            {
                nAccentState = enable ? AccentState.ACCENT_ENABLE_BLURBEHIND : AccentState.ACCENT_DISABLED,
                nFlags = 2,
                nColor = 0,
                nAnimationId = 0
            };

            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}