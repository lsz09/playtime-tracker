using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>현재 실행 중인 프로그램과 실시간 타이머. 닫으면 트레이로 숨는다.</summary>
public partial class MainWindow : Window
{
    private readonly TrackingEngine _engine;
    private readonly ObservableCollection<ProgramRow> _rows = new();
    private readonly DispatcherTimer _uiTimer;

    public event Action? StatsRequested;
    public event Action? ManageRequested;
    public event Action? HiddenToTray;
    public event Action? SettingsRequested;

    /// <summary>줄의 "상세 보기"를 눌렀을 때 (줄 키, 표시 이름).</summary>
    public event Action<string, string>? DetailRequested;

    public MainWindow(TrackingEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Icon = AppIcon.Window;
        UiScale.Attach(this);
        ProgramList.ItemsSource = _rows;

        // 화면 타이머는 창이 보일 때만 돈다
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => UpdateTimerState();
        StateChanged += (_, _) => UpdateTimerState();
        _engine.SettingsChanged += () => { if (_uiTimer.IsEnabled) Refresh(); };
    }

    private void UpdateTimerState()
    {
        bool showing = IsVisible && WindowState != WindowState.Minimized;
        if (showing && !_uiTimer.IsEnabled)
        {
            Refresh();
            _uiTimer.Start();
        }
        else if (!showing)
        {
            _uiTimer.Stop();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 창 닫기 = 트레이로 숨기기. 실제 종료는 트레이 메뉴의 "종료"
        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke();
    }

    private void OnStatsClick(object sender, RoutedEventArgs e) => StatsRequested?.Invoke();

    private void OnManageClick(object sender, RoutedEventArgs e) => ManageRequested?.Invoke();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnDetailClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramRow row)
            DetailRequested?.Invoke(row.Key, row.DisplayName);
    }

    private void Refresh()
    {
        var snapshot = _engine.LastSnapshot;
        if (snapshot == null) return;
        var now = DateTime.Now;
        double sinceScan = Math.Clamp((now - snapshot.Time).TotalSeconds, 0, 2);

        // 숨김/묶음/게임 설정이 적용된, 화면에 보일 줄
        var view = _engine.GetLiveView();
        var ordered = view.Entries;

        // 포커스는 2초마다 누적되므로, 그 사이는 활성 프로그램에 한해 화면에서만 이어서 센다
        double LiveFocus(LiveEntry e) => e.FocusSeconds + (e.IsActive && !snapshot.IsIdle ? sinceScan : 0);

        // 목록을 같은 순서로 맞춘다 (있는 줄은 재사용)
        for (int i = 0; i < ordered.Count; i++)
        {
            int found = -1;
            for (int j = i; j < _rows.Count; j++)
            {
                if (_rows[j].Key == ordered[i].Key) { found = j; break; }
            }
            if (found < 0) _rows.Insert(i, new ProgramRow(ordered[i].Key));
            else if (found != i) _rows.Move(found, i);
        }
        while (_rows.Count > ordered.Count) _rows.RemoveAt(_rows.Count - 1);

        for (int i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var run = e.Start is { } start ? now - start : TimeSpan.Zero;
            string detail = string.Join(", ", e.Details);
            if (e.IsGame) detail = "게임 · " + detail;
            _rows[i].Update(e.Name, detail, Icons.ForExe(e.IconPath), e.Start, e.IsActive, run, LiveFocus(e));
        }

        // 지금 플레이 중인 게임
        if (view.CurrentGame is { } game)
        {
            var gameRun = game.Start is { } gs ? now - gs : TimeSpan.Zero;
            var gameFocus = TimeSpan.FromSeconds(Math.Floor(LiveFocus(game)));
            GameBanner.Visibility = Visibility.Visible;
            GameIcon.Source = Icons.ForExe(game.IconPath);
            GameNameText.Text = game.Name;
            GameStateText.Text = game.IsActive ? "지금 플레이 중" : "게임 실행 중 (다른 창 사용 중)";
            GameTimeText.Text = $"실행 {TimeText.Format(gameRun)} · 포커스 {TimeText.Format(gameFocus)}";
        }
        else
        {
            GameBanner.Visibility = Visibility.Collapsed;
        }

        CountText.Text = view.HiddenCount > 0
            ? $"실행 중 {ordered.Count}개 (숨김 {view.HiddenCount}개 제외) · 이번 실행 기준 시간"
            : $"실행 중 {ordered.Count}개 · 이번 실행 기준 시간";
    }
}

public sealed class ProgramRow : INotifyPropertyChanged
{
    private DateTime? _sessionStart;
    private double _shownFocus;

    public ProgramRow(string key) => Key = key;

    public string Key { get; }
    public string DisplayName { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public ImageSource? Icon { get; private set; }
    public string RunText { get; private set; } = "";
    public string FocusText { get; private set; } = "";
    public bool IsActive { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(string name, string detail, ImageSource? icon, DateTime? sessionStart, bool isActive, TimeSpan run, double focusSeconds)
    {
        // 같은 세션 안에서는 포커스 표시가 뒤로 가지 않게 한다
        if (sessionStart != _sessionStart)
        {
            _sessionStart = sessionStart;
            _shownFocus = 0;
        }
        _shownFocus = Math.Max(_shownFocus, focusSeconds);

        Set(DisplayName, name, v => DisplayName = v, nameof(DisplayName));
        Set(Detail, detail, v => Detail = v, nameof(Detail));
        Set(Icon, icon, v => Icon = v, nameof(Icon));
        Set(RunText, TimeText.Format(run), v => RunText = v, nameof(RunText));
        Set(FocusText, TimeText.Format(TimeSpan.FromSeconds(Math.Floor(_shownFocus))), v => FocusText = v, nameof(FocusText));
        Set(IsActive, isActive, v => IsActive = v, nameof(IsActive));
    }

    private void Set<T>(T current, T value, Action<T> assign, string name)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
