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

    /// <summary>
    /// 창이 놓인 모니터의 작업 영역(작업 표시줄 제외)을 WPF 단위로 돌려준다.
    /// 모니터가 여러 개일 때 창이 걸친 면적이 가장 큰 모니터를 고르고, 위치를 아직 모르면 주 모니터를 쓴다.
    /// </summary>
    public static Rect WorkAreaOf(Window window)
    {
        if (double.IsNaN(window.Left) || double.IsNaN(window.Top)) return SystemParameters.WorkArea;
        var (sx, sy) = Dpi(window);
        var pixels = new System.Drawing.Rectangle(
            (int)Math.Round(window.Left * sx), (int)Math.Round(window.Top * sy),
            Math.Max(1, (int)Math.Round(window.ActualWidth * sx)), Math.Max(1, (int)Math.Round(window.ActualHeight * sy)));
        return ToDip(System.Windows.Forms.Screen.FromRectangle(pixels).WorkingArea, sx, sy);
    }

    /// <summary>이 점(WPF 단위)이 어느 모니터의 작업 영역 안에 있는지. 모니터를 뺐을 때 저장된 위치를 버리기 위해 쓴다.</summary>
    public static bool IsOnAnyScreen(Window window, Point point)
    {
        var (sx, sy) = Dpi(window);
        return System.Windows.Forms.Screen.AllScreens.Any(s => ToDip(s.WorkingArea, sx, sy).Contains(point));
    }

    private static (double X, double Y) Dpi(Window window)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        return (dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private static Rect ToDip(System.Drawing.Rectangle r, double sx, double sy) =>
        new(r.X / sx, r.Y / sy, r.Width / sx, r.Height / sy);

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
