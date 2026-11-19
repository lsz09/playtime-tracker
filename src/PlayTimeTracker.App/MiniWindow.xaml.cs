using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>
/// 항상 위에 떠 있는 작은 창: "포커스된 프로그램 이름 - 오늘 사용 시간".
/// 끌어서 옮기고, 두 번 누르면 메인 창을 연다.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly TrackingEngine _engine;
    private readonly DispatcherTimer _timer;

    // 지금 보여 주는 프로그램의 "오늘 합계"를 DB에서 읽어 둔 값과, 그 값이 유효한 시각
    private string? _key;
    private TimeSpan _storedToday;
    private DateTime _storedAt;
    private bool _moved; // 사용자가 끌어서 옮겼는지

    private const string ScaleKey = "mini_scale";
    private const string PositionKey = "mini_position";
    private const double MinScale = 0.6, MaxScale = 3.0;
    private double _scale = 1;
    private double _resizeStartScale;
    private Size _resizeStartSize;
    private Point _resizeStartCursor;
    private string? _resizeEdge;            // 지금 끌고 있는 변 (W/E/N/S/SE)
    private double? _anchorRight, _anchorBottom;


    public event Action? OpenMainRequested;

    public MiniWindow(TrackingEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        UiScale.Attach(this, sizeWindow: false);
        _scale = LoadScale();
        UiScale.SetExtra(this, _scale);

        WindowTools.HideFromAltTab(this);
        RestorePosition();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        // 내용(프로그램 이름)에 따라 폭이 달라지므로, 크기가 바뀔 때마다 자리를 다시 잡는다
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// 사용자가 옮기기 전에는 주 모니터 오른쪽 아래에 붙여 두고, 옮긴 뒤에는 지금 놓인 모니터 밖으로 나가지만 않게 한다.
    /// </summary>
    private void Place()
    {
        if (_resizeEdge != null)
        {
            // 크기를 바꾸는 동안에는 고정한 쪽만 맞춘다 (끝나면 화면 안으로 다시 넣는다)
            if (_anchorRight is { } right) Left = right - ActualWidth;
            if (_anchorBottom is { } bottom) Top = bottom - ActualHeight;
            return;
        }
        if (!_moved)
        {
            var primary = SystemParameters.WorkArea;
            Left = primary.Right - ActualWidth - 24;
            Top = primary.Bottom - ActualHeight - 24;
            return;
        }
        var area = WindowTools.WorkAreaOf(this);
        Left = Math.Max(area.Left, Math.Min(Left, area.Right - ActualWidth));
        Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - ActualHeight));
    }

    /// <summary>옮긴 위치(왼쪽 위)를 저장해 두었다가, 그 자리가 아직 화면 안이면 다음에도 거기에 띄운다.</summary>
    private void RestorePosition()
    {
        var parts = _engine.Db.GetSetting(PositionKey)?.Split(',');
        if (parts is not { Length: 2 } ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double left) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double top) ||
            !WindowTools.IsOnAnyScreen(this, new Point(left + 1, top + 1)))
            return; // 저장된 위치가 없거나, 그 모니터를 뺐으면 기본 위치
        Left = left;
        Top = top;
        _moved = true;
    }

    private void SavePosition() =>
        _engine.Db.SetSetting(PositionKey, string.Create(CultureInfo.InvariantCulture, $"{Left:0.#},{Top:0.#}"));

    private void Refresh()
    {
        var active = _engine.GetLiveView().Entries.FirstOrDefault(e => e.IsActive);
        if (active == null)
        {
            _key = null;
            IconImage.Source = null;
            NameText.Text = "포커스된 프로그램 없음";
            TimeTextBlock.Text = "";
            return;
        }

        var now = DateTime.Now;
        // DB의 오늘 합계는 마지막 저장 시각까지의 값이다. 프로그램이 바뀌었거나 새로 저장됐거나 날짜가 넘어가면 다시 읽는다.
        if (active.Key != _key || _engine.LastFlushTime != _storedAt || _storedAt.Date != now.Date)
        {
            _key = active.Key;
            _storedAt = _engine.LastFlushTime;
            _storedToday = Stats.DailyTotals(_engine.Db, active.Key, now.Date, now.Date)[0].RunTime;
        }

        // 저장 이후(또는 저장 뒤에 켰다면 켠 뒤)부터 지금까지를 더한다
        var since = _storedAt;
        if (active.Start is { } start && start > since) since = start;
        if (since < now.Date) since = now.Date;
        var total = _storedToday + (now > since ? now - since : TimeSpan.Zero);

        IconImage.Source = Icons.ForExe(active.IconPath);
        NameText.Text = active.Name;
        TimeTextBlock.Text = "- " + TimeText.Format(total);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        _moved = true;
        Place();
        SavePosition();
    }

    // ── 크기 조절: 오른쪽 아래 모서리를 끌거나 마우스 휠 ──

    private double LoadScale() =>
        double.TryParse(_engine.Db.GetSetting(ScaleKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp(value, MinScale, MaxScale)
            : 1;

    private void SetScale(double scale, bool save)
    {
        _scale = Math.Clamp(scale, MinScale, MaxScale);
        UiScale.SetExtra(this, _scale);
        if (save) _engine.Db.SetSetting(ScaleKey, _scale.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private Point CursorPosition()
    {
        var point = PointToScreen(Mouse.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }

    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        _moved = true;
        _resizeEdge = (string)((Thumb)sender).Tag;
        _resizeStartScale = _scale;
        _resizeStartSize = new Size(ActualWidth, ActualHeight);
        _resizeStartCursor = CursorPosition();
        // 끄는 변의 반대쪽을 고정한다: 왼쪽 변을 끌면 오른쪽이, 위쪽 변을 끌면 아래쪽이 제자리에 남는다
        _anchorRight = _resizeEdge == "W" ? Left + ActualWidth : null;
        _anchorBottom = _resizeEdge == "N" ? Top + ActualHeight : null;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizeEdge == null || _resizeStartSize.Width <= 0 || _resizeStartSize.Height <= 0) return;
        var cursor = CursorPosition();
        double ratio = _resizeEdge switch
        {
            "W" => (_resizeStartSize.Width - (cursor.X - _resizeStartCursor.X)) / _resizeStartSize.Width,
            "N" => (_resizeStartSize.Height - (cursor.Y - _resizeStartCursor.Y)) / _resizeStartSize.Height,
            "S" => (_resizeStartSize.Height + cursor.Y - _resizeStartCursor.Y) / _resizeStartSize.Height,
            _ => (_resizeStartSize.Width + cursor.X - _resizeStartCursor.X) / _resizeStartSize.Width,
        };
        SetScale(_resizeStartScale * Math.Max(0.1, ratio), save: false);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        SetScale(_scale, save: true);
        _resizeEdge = null;
        _anchorRight = _anchorBottom = null;
        Place();
        SavePosition();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e) =>
        SetScale(Math.Round(_scale + (e.Delta > 0 ? 0.1 : -0.1), 2), save: true);

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenMainRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
