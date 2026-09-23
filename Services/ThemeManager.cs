using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tally.Services;

/// <summary>Switches between dark and light theme (colors + window title bar).</summary>
public static class ThemeManager
{
    public static string Current { get; private set; } = "dark";

    public static void Apply(string theme)
    {
        Current = theme == "light" ? "light" : "dark";
        var app = Application.Current;
        if (app == null) return;

        var file = Current == "light" ? "LightTheme" : "DarkTheme";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{file}.xaml", UriKind.Absolute)
        };

        // The color dictionary is the one that contains "BgBrush" – swap it, all DynamicResources follow.
        var merged = app.Resources.MergedDictionaries;
        var replaced = false;
        for (var i = 0; i < merged.Count; i++)
        {
            if (!merged[i].Contains("BgBrush")) continue;
            merged[i] = dict;
            replaced = true;
            break;
        }
        if (!replaced) merged.Insert(0, dict);

        foreach (Window window in app.Windows) ApplyTitleBar(window);
    }

    /// <summary>Makes sure a window's title bar matches the theme.</summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private static void ApplyTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var dark = Current == "dark" ? 1 : 0;
            // 20 = Windows 11 / Windows 10 (20H1 and later), 19 = older Windows 10 versions
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));

            // For an already-visible window (switching theme), some Windows 10 versions
            // only redraw the title bar once the window is nudged.
            if (window.IsVisible)
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch
        {
            // Purely cosmetic – must never cause an error.
        }
    }
}
