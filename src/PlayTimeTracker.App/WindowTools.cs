using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PlayTimeTracker.App;

public static class WindowTools
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    /// <summary>창을 도구 창으로 표시해 Alt+Tab 목록에 나오지 않게 한다. 창을 띄우기 전에 부른다.</summary>
    public static void HideFromAltTab(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow));
        };
    }
}
