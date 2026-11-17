using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>
/// 창 크기를 고정하고, 설정한 배율로 모든 창의 내용과 크기를 함께 키우거나 줄인다.
/// "auto"는 모니터 해상도에 맞춰 배율을 정한다 (1080p 높이를 100%로 본다).
/// </summary>
public static class UiScale
{
    public const string Auto = "auto";
    private const string SettingKey = "ui_scale";

    // 가장 큰 창(프로그램 관리, 상세)이 화면 밖으로 나가지 않게 할 때 쓰는 기준 크기
    private const double LargestWidth = 1000;
    private const double LargestHeight = 700;

    private static readonly Dictionary<Window, (double Width, double Height, bool SizeWindow)> _windows = new();
    private static readonly Dictionary<Window, double> _extra = new();

    /// <summary>"auto" 또는 배율 숫자(예: "1.25").</summary>
    public static string Mode { get; private set; } = Auto;

    public static void Load(PlayTimeDatabase db) => Mode = Normalize(db.GetSetting(SettingKey));

    public static void Set(PlayTimeDatabase db, string mode)
    {
        Mode = Normalize(mode);
        db.SetSetting(SettingKey, Mode);
        foreach (var window in _windows.Keys.ToList()) Apply(window);
    }

    /// <summary>해상도에 맞춘 배율: 화면 높이가 1080이면 100%, 1440이면 약 135%.</summary>
    public static double AutoScale()
    {
        double scale = SystemParameters.PrimaryScreenHeight / 1080.0;
        return Math.Clamp(Math.Round(scale / 0.05) * 0.05, 0.8, 2.0);
    }

    /// <summary>지금 실제로 적용되는 배율 (화면에 다 들어오도록 줄인 값).</summary>
    public static double Current
    {
        get
        {
            double wanted = Mode == Auto
                ? AutoScale()
                : double.Parse(Mode, CultureInfo.InvariantCulture);
            var area = SystemParameters.WorkArea;
            double fit = Math.Min(area.Width / LargestWidth, area.Height / LargestHeight);
            return Math.Max(0.6, Math.Min(wanted, fit));
        }
    }

    /// <summary>
    /// 창을 배율 관리 대상으로 등록한다. XAML에 적힌 크기를 100% 기준으로 삼고,
    /// 사용자가 테두리를 끌어 크기를 바꾸지 못하게 한다.
    /// sizeWindow가 false면 내용만 배율을 적용한다 (내용에 맞춰 크기가 정해지는 창).
    /// </summary>
    public static void Attach(Window window, bool sizeWindow = true)
    {
        _windows[window] = (window.Width, window.Height, sizeWindow);
        if (sizeWindow) window.ResizeMode = ResizeMode.CanResize; // 테두리를 끌어 크기를 바꿀 수 있다
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            _extra.Remove(window);
        };
        Apply(window);
    }

    /// <summary>창 하나에만 추가로 곱하는 배율 (작은 창의 크기 조절).</summary>
    public static void SetExtra(Window window, double factor)
    {
        _extra[window] = factor;
        Apply(window);
    }

    private static void Apply(Window window)
    {
        if (!_windows.TryGetValue(window, out var size)) return;
        double scale = Current * (_extra.TryGetValue(window, out double extra) ? extra : 1);

        if (window.Content is FrameworkElement root)
            root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);

        if (size.SizeWindow)
        {
            // 배율을 바꾸면 기본 크기로 돌아간다. 마우스로는 기본 크기의 60%까지 줄일 수 있다.
            if (window.WindowState == WindowState.Maximized) window.WindowState = WindowState.Normal;
            window.MinWidth = size.Width * scale * 0.6;
            window.MinHeight = size.Height * scale * 0.6;
            window.Width = size.Width * scale;
            window.Height = size.Height * scale;
        }
    }

    private static string Normalize(string? mode)
    {
        if (mode != null && mode != Auto &&
            double.TryParse(mode, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
            value is >= 0.5 and <= 3)
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        return Auto;
    }
}
