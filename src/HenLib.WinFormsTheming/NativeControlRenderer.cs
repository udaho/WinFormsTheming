using System.Drawing;
using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming;

internal static class NativeControlRenderer
{
    internal static void Apply(Control control, ThemeContext context, Action<string, int> diagnostic,
        bool refreshButtonPainter, bool refreshProgressTheme)
    {
        if (!control.IsHandleCreated || control.IsDisposed)
        {
            return;
        }
        // Refreshing a managed button adapter can recreate its HWND. Theme the final handle.
        if (refreshButtonPainter && control is ButtonBase button)
        {
            RefreshButtonPainter(button);
        }

        bool dark = context.IsDark && !context.IsHighContrast;
        string? explorer = dark ? "DarkMode_Explorer" : null;
        void Theme(nint handle, string? app, string? classes = null)
        {
            if (handle == 0)
            {
                return;
            }

            int result = SetWindowTheme(handle, app, classes);
            if (result != 0)
            {
                diagnostic("SetWindowTheme", result);
            }
        }

        if (control is Form && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            int enabled = dark ? 1 : 0;
            int result = DwmSetWindowAttribute(control.Handle, 20, ref enabled, sizeof(int));
            if (result != 0)
            {
                diagnostic("DwmSetWindowAttribute", result);
            }
        }
        else if (control is ComboBox)
        {
            Theme(control.Handle, dark ? "DarkMode_CFD" : null);
            var info = new ComboInfo { Size = Marshal.SizeOf<ComboInfo>() };
            if (GetComboBoxInfo(control.Handle, ref info))
            {
                Theme(info.List, explorer);
                Theme(info.Edit, explorer);
            }
        }
        else if (control is ProgressBar)
        {
            // This path is the explicit Native policy, and restores native drawing on detach.
            if (refreshProgressTheme)
            {
                Theme(control.Handle, dark ? " " : null, dark ? " " : null);
            }

            SendMessage(control.Handle, 0x2001, 0, ColorTranslator.ToWin32(control.BackColor));
            SendMessage(control.Handle, 0x409, 0, ColorTranslator.ToWin32(control.ForeColor));
            if (control is ProgressBar { Style: ProgressBarStyle.Marquee } progress)
            {
                SendMessage(control.Handle, 0x40A, progress.MarqueeAnimationSpeed > 0 ? 1 : 0, progress.MarqueeAnimationSpeed);
            }
        }
        else if (control is TabControl)
        {
            Theme(control.Handle, null, dark ? "DarkMode::FileExplorerBannerContainer" : null);
        }
        else if (control is TextBoxBase or UpDownBase or ButtonBase or ListView or TreeView or GroupBox or TrackBar or ScrollBar or ListBox ||
            ControlThemeState.IsFrameworkSpinnerChild(control))
        {
            Theme(control.Handle, explorer);
        }

        if (control is RichTextBox)
        {
            SendMessage(control.Handle, 0x443, 0, ColorTranslator.ToWin32(control.BackColor));
        }

        if (control is ListView list)
        {
            SendMessage(list.Handle, 0x1001, 0, ColorTranslator.ToWin32(list.BackColor));
            SendMessage(list.Handle, 0x1024, 0, ColorTranslator.ToWin32(list.ForeColor));
            SendMessage(list.Handle, 0x1026, 0, -1);
            nint header = SendMessage(list.Handle, 0x101F, 0, 0);
            Theme(header, dark ? "DarkMode_ItemsView" : null);
            RedrawWindow(header, 0, 0, 0x405);
        }
        if (control is TreeView tree)
        {
            SendMessage(tree.Handle, 0x111D, 0, ColorTranslator.ToWin32(tree.BackColor));
            SendMessage(tree.Handle, 0x111E, 0, ColorTranslator.ToWin32(tree.ForeColor));
        }
        RedrawWindow(control.Handle, 0, 0, 0x405);
    }

    internal static void RefreshButtonPainter(ButtonBase button)
    {
        if (button.FlatStyle == FlatStyle.System)
        {
            return;
        }

        FlatStyle original = button.FlatStyle;
        Control? parent = button.Parent;
        // Public layout properties replace WinForms' cached adapter. The native-text-button
        // policy avoids this path for Standard buttons and its HWND recreation on switches.
        // Avoid laying out the temporary Flat style: GrowOnly buttons can retain its size.
        parent?.SuspendLayout();
        try
        {
            button.FlatStyle = original == FlatStyle.Flat ? FlatStyle.Popup : FlatStyle.Flat;
            button.GetPreferredSize(new Size(12347, 23459));
        }
        finally
        {
            try
            {
                button.FlatStyle = original;
                button.GetPreferredSize(new Size(12349, 23461));
            }
            finally
            {
                parent?.ResumeLayout(performLayout: true);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboInfo
    {
        public int Size; public Rect Item, Button; public uint State; public nint Handle, Edit, List;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint handle, string? app, string? classes);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetComboBoxInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(nint handle, ref ComboInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint handle, nint rect, nint region, uint flags);
}
