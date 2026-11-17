using System.Windows;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>
/// 프로그램 하나(또는 묶음 태그 하나)를 언제 얼마나 썼는지: 기간별 합계, 날짜별 사용 시간, 사용 기록.
/// </summary>
public partial class DetailWindow : Window
{
    private const int Days = 30;         // 날짜별 목록에 보여 줄 기간
    private const int SessionDays = 90;  // 사용 기록에 보여 줄 기간
    private const int MaxSessions = 300;

    private readonly TrackingEngine _engine;
    private readonly string _entryKey;

    public DetailWindow(TrackingEngine engine, string entryKey, string name)
    {
        _engine = engine;
        _entryKey = entryKey;
        InitializeComponent();
        Icon = AppIcon.Window;
        UiScale.Attach(this);
        Title = $"PlayTimeTracker - {name}";
        NameText.Text = name;
        Loaded += (_, _) => Reload();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        // 지금 실행 중인 분량도 들어가도록 먼저 저장한다
        _engine.FlushNow();
        var db = _engine.Db;
        var now = DateTime.Now;
        var tomorrow = now.Date.AddDays(1);

        // 기간별 합계
        ProgramTotal? Total(DateTime from, DateTime to) =>
            Stats.Summarize(db, from, to).FirstOrDefault(t => t.Key == _entryKey);
        var (weekFrom, weekTo) = Stats.ThisWeek(now);
        var (monthFrom, monthTo) = Stats.ThisMonth(now);
        var all = Total(new DateTime(2000, 1, 1), tomorrow);

        SummaryList.ItemsSource = new[]
        {
            Summary("오늘", Total(now.Date, tomorrow)),
            Summary("이번 주", Total(weekFrom, weekTo)),
            Summary("이번 달", Total(monthFrom, monthTo)),
            Summary("전체", all),
        };

        if (all != null)
        {
            NameText.Text = all.DisplayName;
            DetailText.Text = all.IsGame ? "게임 · " + all.Detail : all.Detail;
            IconImage.Source = Icons.ForExe(all.IconPath);
        }
        else
        {
            DetailText.Text = "기록이 없습니다.";
        }

        // 날짜별: 최근 30일, 오늘부터 거꾸로
        var days = Stats.DailyTotals(db, _entryKey, now.Date.AddDays(-(Days - 1)), now.Date);
        double max = days.Count > 0 ? days.Max(d => d.RunTime.TotalSeconds) : 0;
        DayList.ItemsSource = days
            .OrderByDescending(d => d.Date)
            .Select(d => new DayRow(
                d.Date.ToString("M/d (ddd)"),
                d.RunTime > TimeSpan.Zero ? TimeText.Format(d.RunTime) : "-",
                max > 0 ? d.RunTime.TotalSeconds / max : 0,
                max > 0 ? d.FocusTime.TotalSeconds / max : 0,
                d.RunTime > TimeSpan.Zero ? 1.0 : 0.4,
                $"실행 {TimeText.Format(d.RunTime)} · 포커스 {TimeText.Format(d.FocusTime)}"))
            .ToList();

        // 사용 기록: 켠 시각 ~ 끈 시각
        var sessions = Stats.SessionsOf(db, _entryKey, now.Date.AddDays(-SessionDays), tomorrow);
        bool multipleExes = sessions.Select(s => s.ExeName).Distinct().Skip(1).Any();
        SessionList.ItemsSource = sessions.Take(MaxSessions).Select(s =>
        {
            string end = s.End.Date == s.Start.Date ? s.End.ToString("HH:mm") : s.End.ToString("M/d HH:mm");
            string sub = "포커스 " + TimeText.Format(TimeSpan.FromSeconds(s.FocusSeconds));
            if (multipleExes) sub = s.ExeName + " · " + sub;
            return new SessionRow(
                $"{s.Start:M/d (ddd) HH:mm} ~ {end}",
                sub,
                TimeText.Format(s.End - s.Start));
        }).ToList();

        SessionHeading.Text = $"사용 기록 (최근 {SessionDays}일, {sessions.Count}건)";
        EmptyText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        string note = sessions.Count > MaxSessions ? $" 사용 기록은 최근 {MaxSessions}건만 보여 줍니다." : "";
        FooterText.Text = $"{now:HH:mm:ss} 기준 · 큰 숫자는 실행 시간, 진한 막대와 작은 숫자는 포커스 시간입니다.{note}";
    }

    private static SummaryTile Summary(string label, ProgramTotal? total) => new(
        label,
        TimeText.Format(total?.RunTime ?? TimeSpan.Zero),
        "포커스 " + TimeText.Format(total?.FocusTime ?? TimeSpan.Zero));
}

public sealed record SummaryTile(string Label, string RunText, string FocusText);

public sealed record DayRow(string DateText, string RunText, double RunRatio, double FocusRatio, double TextOpacity, string Tip);

public sealed record SessionRow(string WhenText, string SubText, string RunText);
