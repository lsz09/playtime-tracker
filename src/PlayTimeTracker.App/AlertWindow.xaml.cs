using System.Windows;
using System.Windows.Threading;

namespace PlayTimeTracker.App;

/// <summary>
/// 사용 시간 알림 팝업. 화면 오른쪽 아래에 항상 위로 뜨고, 포커스를 빼앗지 않는다 (게임 입력이 끊기지 않게).
/// 닫기 전까지 남아 있다가 일정 시간이 지나면 스스로 닫힌다. 여러 개가 뜨면 위로 쌓인다.
/// </summary>
public partial class AlertWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
    private static readonly List<AlertWindow> Open = new();
    private readonly DispatcherTimer _timer;

    public AlertWindow(string title, string message)
    {
        InitializeComponent();
        UiScale.Attach(this, sizeWindow: false);
        WindowTools.HideFromAltTab(this);

        TitleText.Text = title;
        MessageText.Text = message;
        TimeText.Text = DateTime.Now.ToString("HH:mm");

        _timer = new DispatcherTimer { Interval = Lifetime };
        _timer.Tick += (_, _) => Close();

        Open.Add(this);
        SizeChanged += (_, _) => PlaceAll();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Open.Remove(this);
            PlaceAll();
        };
    }

    /// <summary>오른쪽 아래에서 위로 차례대로 쌓는다. 맨 아래는 작은 창 자리를 비워 둔다.</summary>
    private static void PlaceAll()
    {
        var area = SystemParameters.WorkArea;
        double bottom = area.Bottom - 24 - 70 * UiScale.Current;
        foreach (var window in Open)
        {
            if (window.ActualHeight <= 0) continue;
            window.Left = area.Right - window.ActualWidth - 24;
            window.Top = Math.Max(area.Top, bottom - window.ActualHeight);
            bottom = window.Top - 8;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
