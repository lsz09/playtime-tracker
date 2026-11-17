using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>화면 크기(배율)와 작은 창 사용 여부. 바꾸는 즉시 저장되고 적용된다.</summary>
public partial class SettingsWindow : Window
{
    private const string MiniKey = "mini_window";
    private static readonly double[] Scales = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0 };

    private readonly TrackingEngine _engine;
    private bool _loading = true;

    public static bool IsMiniWindowEnabled(PlayTimeDatabase db) => db.GetSetting(MiniKey) == "1";

    public SettingsWindow(TrackingEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Icon = AppIcon.Window;
        UiScale.Attach(this);

        ScaleBox.Items.Add(new ComboBoxItem { Content = "해상도에 맞춤 (자동)", Tag = UiScale.Auto });
        foreach (double scale in Scales)
        {
            ScaleBox.Items.Add(new ComboBoxItem
            {
                Content = $"{scale * 100:0}%",
                Tag = scale.ToString("0.##", CultureInfo.InvariantCulture),
            });
        }
        ScaleBox.SelectedItem = ScaleBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == UiScale.Mode)
                                ?? ScaleBox.Items[0];
        MiniOption.IsChecked = IsMiniWindowEnabled(_engine.Db);
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == UiTheme.Mode);

        _loading = false;
        Loaded += (_, _) => UpdateScaleInfo();
    }

    private void OnScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ScaleBox.SelectedItem is not ComboBoxItem item) return;
        UiScale.Set(_engine.Db, (string)item.Tag);
        UpdateScaleInfo();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        UiTheme.Set(_engine.Db, (string)item.Tag);
    }

    public event Action? TestAlertRequested;

    private void OnTestAlertClick(object sender, RoutedEventArgs e) => TestAlertRequested?.Invoke();

    private void OnMiniChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _engine.Db.SetSetting(MiniKey, MiniOption.IsChecked == true ? "1" : "0");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateScaleInfo()
    {
        // 실제 픽셀 해상도 = 윈도우가 알려 주는 크기 × 디스플레이 배율
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY);

        string applied = $"{UiScale.Current * 100:0}%";
        string info = $"현재 화면: {width}×{height} (윈도우 배율 {dpi.DpiScaleX * 100:0}%) · 적용 중인 크기: {applied}";
        if (UiScale.Mode == UiScale.Auto)
            info += $"\n자동은 화면 높이 1080을 100%로 보고 해상도에 비례해 정합니다.";
        else if (Math.Abs(UiScale.Current - double.Parse(UiScale.Mode, CultureInfo.InvariantCulture)) > 0.001)
            info += "\n고른 크기가 화면보다 커서, 화면에 들어오는 크기로 줄였습니다.";
        ScaleInfoText.Text = info;
    }
}
