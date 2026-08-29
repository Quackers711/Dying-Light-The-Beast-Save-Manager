using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DLBeastSaveManager.Views;

public class ThemedWindow : Window
{
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "AppWindow");
        SnapsToDevicePixels = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var dark = 1;
        DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));
    }
}
