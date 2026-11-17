using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace PlayTimeTracker.App;

/// <summary>앱 자신의 아이콘 (트레이와 창 제목 표시줄 공용). 파란 원 위의 시계 바늘을 실행 중에 그린다.</summary>
public static class AppIcon
{
    private static readonly Lazy<Drawing.Icon> _tray = new(Create);
    private static readonly Lazy<ImageSource> _window = new(() => Icons.ToImageSource(_tray.Value));

    public static Drawing.Icon Tray => _tray.Value;
    public static ImageSource Window => _window.Value;

    private static Drawing.Icon Create()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x4F, 0x7C, 0xFF));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var pen = new Drawing.Pen(Drawing.Color.White, 3.2f)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
            };
            g.DrawLine(pen, 16, 16, 16, 7);
            g.DrawLine(pen, 16, 16, 22, 19);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

/// <summary>프로그램 아이콘. exe 파일에서 한 번만 읽어 캐시한다 (실행 중인 프로세스는 건드리지 않음).</summary>
public static class Icons
{
    private static readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForExe(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (_cache.TryGetValue(exePath, out var cached)) return cached;

        ImageSource? image = null;
        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon != null) image = ToImageSource(icon);
        }
        catch
        {
            // 파일이 없거나 읽을 수 없으면 아이콘 없이 표시
        }
        _cache[exePath] = image;
        return image;
    }

    public static ImageSource ToImageSource(Drawing.Icon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
