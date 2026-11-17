using System.Windows;
using System.Windows.Media;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>
/// 오늘/이번 주/이번 달 프로그램별 사용 시간 (목록 + 막대 그래프)과
/// 바로 앞 기간(어제/지난주/지난달)과의 비교.
/// </summary>
public partial class StatsWindow : Window
{
    private readonly TrackingEngine _engine;

    public StatsWindow(TrackingEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Icon = AppIcon.Window;
        UiScale.Attach(this);
        FromPicker.SelectedDate = ToPicker.SelectedDate = DateTime.Today;
        ComparePicker.SelectedDate = DateTime.Today.AddDays(-1);
        Loaded += (_, _) => Reload();

        // 묶기/제외 설정이 바뀌면 바로 다시 계산
        _engine.SettingsChanged += Reload;
        Closed += (_, _) => _engine.SettingsChanged -= Reload;
    }

    private void OnPeriodChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Reload();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload();

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        // 둘 중 하나만 켜진 상태로 유지한다
        bool ascending = ReferenceEquals(sender, SortAscButton);
        SortAscButton.IsChecked = ascending;
        SortDescButton.IsChecked = !ascending;
        Reload();
    }

    private void Reload()
    {
        // 방금까지의 기록이 합계에 들어가도록 먼저 저장한다
        _engine.FlushNow();

        var now = DateTime.Now;
        string period = TagOf(PeriodBox);
        string compare = TagOf(CompareBox);
        CustomPanel.Visibility = period == "custom" ? Visibility.Visible : Visibility.Collapsed;
        CompareBox.IsEnabled = period != "all"; // 전체 기간은 비교할 앞 기간이 없다
        if (period == "all") compare = "none";
        CompareCustomPanel.Visibility = compare == "custom" ? Visibility.Visible : Visibility.Collapsed;

        var (from, to) = StatsPeriods.Range(period, now, FromPicker.SelectedDate, ToPicker.SelectedDate);
        var compared = StatsPeriods.CompareRange(compare, now, from, to, ComparePicker.SelectedDate);
        string previousLabel = compared is { } c ? StatsPeriods.Label(c.From, c.To, now) : "";

        bool gamesOnly = GamesOnlyOption.IsChecked == true;
        var current = Stats.Summarize(_engine.Db, from, to).Where(t => !gamesOnly || t.IsGame).ToList();
        // 기본은 실행 시간이 많은 순. ▲를 누르면 적은 순
        if (SortAscButton.IsChecked == true) current = current.OrderBy(t => t.RunTime).ToList();
        var previous = compared is { } range
            ? Stats.Summarize(_engine.Db, range.From, range.To).Where(t => !gamesOnly || t.IsGame).ToDictionary(t => t.Key)
            : new Dictionary<string, ProgramTotal>();

        // 이번 기간에는 없고 이전 기간에만 있는 것도 비교를 위해 보여 준다
        var rows = current
            .Select(t => (Total: t, Run: t.RunTime, Focus: t.FocusTime, Previous: previous.GetValueOrDefault(t.Key)?.RunTime))
            .ToList();
        var currentKeys = current.Select(t => t.Key).ToHashSet();
        rows.AddRange(previous.Values
            .Where(t => !currentKeys.Contains(t.Key))
            .OrderByDescending(t => t.RunTime)
            .Select(t => (Total: t, Run: TimeSpan.Zero, Focus: TimeSpan.Zero, Previous: (TimeSpan?)t.RunTime)));

        double max = rows.Count > 0
            ? rows.Max(r => Math.Max(r.Run.TotalSeconds, r.Previous?.TotalSeconds ?? 0))
            : 0;
        double Ratio(TimeSpan t) => max > 0 ? t.TotalSeconds / max : 0;

        StatList.ItemsSource = rows.Select(r => new StatRow(
            r.Total.DisplayName,
            r.Total.IsGame ? "게임 · " + r.Total.Detail : r.Total.Detail,
            Icons.ForExe(r.Total.IconPath),
            TimeText.Format(r.Run),
            TimeText.Format(r.Focus),
            Ratio(r.Run),
            Ratio(r.Focus),
            Ratio(r.Previous ?? TimeSpan.Zero),
            compared == null ? "" : DeltaText(r.Run, r.Previous),
            compared == null ? "" : $"{previousLabel}: {TimeText.Format(r.Previous ?? TimeSpan.Zero)}")).ToList();

        EmptyText.Text = gamesOnly ? "이 기간에는 게임 기록이 없습니다." : "이 기간에는 기록이 없습니다.";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        string rangeText = period == "all" ? "전체 기간" : StatsPeriods.Dates(from, to, withYear: true);
        RangeText.Text = $"{rangeText} · {now:HH:mm:ss} 기준";
        PreviousLegendText.Text = previousLabel;
        PreviousLegendMark.Visibility = PreviousLegendText.Visibility =
            compared == null ? Visibility.Collapsed : Visibility.Visible;

        // 태그가 여러 개인 프로그램은 여러 줄에 들어가므로 줄끼리 더한 합계는 보여 주지 않는다
        TotalText.Text = compared is { } shown
            ? $"{rows.Count}개 · 회색 막대와 ▲▼는 {WithDates(previousLabel, StatsPeriods.Dates(shown.From, shown.To, withYear: false))} 전체의 실행 시간과 비교한 것입니다."
            : $"{rows.Count}개";
    }

    /// <summary>"어제(11월 16일)"처럼 이름 뒤에 날짜를 붙인다. 이름이 이미 날짜면 그대로 둔다.</summary>
    private static string WithDates(string label, string dates) => label == dates ? label : $"{label}({dates})";

    private static string TagOf(System.Windows.Controls.ComboBox box) =>
        (box.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "";

    /// <summary>이전 기간 대비 실행 시간 차이. 이전 기록이 없으면 "신규".</summary>
    private static string DeltaText(TimeSpan run, TimeSpan? previous)
    {
        if (previous is not { } prev || prev == TimeSpan.Zero) return "신규";
        var diff = run - prev;
        if (Math.Abs(diff.TotalSeconds) < 1) return "변화 없음";
        return diff > TimeSpan.Zero
            ? "▲ " + TimeText.Format(diff)
            : "▼ " + TimeText.Format(diff.Negate());
    }
}

public sealed record StatRow(
    string DisplayName,
    string Detail,
    ImageSource? Icon,
    string RunText,
    string FocusText,
    double RunRatio,
    double FocusRatio,
    double PreviousRatio,
    string DeltaText,
    string PreviousTip);
