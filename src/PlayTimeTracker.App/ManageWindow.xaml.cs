using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>
/// 프로그램별 표시 이름, 묶음 태그 설정과 삭제(기록 삭제 + 감지 제외). 같은 창을 두 가지로 쓴다:
/// 프로그램 관리(게임이 아닌 것)와 게임 관리(게임만). 줄 오른쪽 버튼으로 서로 옮긴다.
/// </summary>
public partial class ManageWindow : Window
{
    private readonly TrackingEngine _engine;
    private readonly bool _gamesMode;
    private readonly ObservableCollection<ManageRow> _visible = new();
    private List<ManageRow> _rows = new();
    private bool _movingRow;

    /// <summary>프로그램 관리의 "게임관리" 버튼을 눌렀을 때.</summary>
    public event Action? GameManageRequested;

    /// <summary>"제외 목록" 버튼을 눌렀을 때.</summary>
    public event Action? ExclusionsRequested;

    public ManageWindow(TrackingEngine engine, bool gamesMode)
    {
        _engine = engine;
        _gamesMode = gamesMode;
        InitializeComponent();
        Icon = AppIcon.Window;
        UiScale.Attach(this);
        ProgramList.ItemsSource = _visible;

        if (gamesMode)
        {
            Title = "PlayTimeTracker - 게임 관리";
            HeadingText.Text = "게임 관리";
            IntroText.Text = "게임으로 분류된 프로그램입니다. 통계의 \"게임만\"과 메인 창의 플레이 중 표시에 쓰입니다. 게임이 아닌 것은 \"게임 해제\"를 누르면 프로그램 관리로 돌아갑니다. 알림 칸에 시간을 넣으면 그 게임이 그만큼 실행될 때마다 알림이 옵니다 (예: 1 = 1시간마다). \"삭제\"는 목록과 DB에서 완전히 지우고 앞으로 감지하지 않습니다.";
            GameManageButton.Visibility = Visibility.Collapsed;
            AlertHeaderColumn.Width = new GridLength(ManageRow.AlertWidth);
        }
        else
        {
            IntroText.Text = "같은 묶음 태그를 가진 프로그램은 한 줄로 합쳐지고, 태그는 쉼표로 여러 개 넣을 수 있습니다. 게임은 \"게임\" 버튼을 누르면 게임 관리로 옮겨집니다. 알림 칸에 시간을 넣으면 그 프로그램이 그만큼 실행될 때마다 알림이 옵니다 (예: 1 = 1시간마다). \"삭제\"는 그 프로그램을 목록과 DB에서 완전히 지우고 제외 목록에 넣어 앞으로 감지하지 않습니다.";
        }

        Loaded += (_, _) => Load();

        // 다른 창에서 게임 지정이 바뀌면 목록을 다시 읽는다 (이 창에서 누른 경우는 줄만 뺀다)
        _engine.GameListChanged += OnGameListChanged;
        // 새 프로그램이 기록되거나(제외 목록에서 뺀 직후 포함) 삭제되면 목록에 바로 반영한다
        _engine.ProgramListChanged += OnGameListChanged;
        Closed += (_, _) =>
        {
            _engine.GameListChanged -= OnGameListChanged;
            _engine.ProgramListChanged -= OnGameListChanged;
        };
    }

    private void OnGameListChanged()
    {
        if (!_movingRow && IsLoaded) Load();
    }

    private void Load()
    {
        // 방금 처음 감지된 프로그램도 목록에 나오도록 먼저 저장한다
        _engine.FlushNow();
        var programs = _engine.Db.GetPrograms();

        var tags = programs.Where(p => !p.Hidden).SelectMany(p => p.Tags).Distinct().Order().ToList();
        TagsText.Text = tags.Count > 0
            ? "사용 중인 태그: " + string.Join(", ", tags)
            : "사용 중인 태그: 없음";

        // 이미 떠 있는 줄은 그대로 재사용해서, 입력 중이던 이름/태그/알림이 지워지지 않게 한다
        var existing = _rows.ToDictionary(r => r.Key);

        _rows = programs
            .Where(p => p.IsGame == _gamesMode)
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.ExeName, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => existing.TryGetValue(p.Key, out var shown) ? shown.WithIgnored(p.Hidden) : new ManageRow
            {
                Key = p.Key,
                DefaultName = p.DisplayName,
                ExeName = p.ExeName,
                ExePath = p.ExePath,
                Icon = Icons.ForExe(p.ExePath),
                MoveText = _gamesMode ? "게임 해제" : "게임",
                Name = p.OwnName,
                GroupName = p.GroupName ?? "",
                Ignored = p.Hidden,
                ShowAlert = true,
                AlertText = p.AlertMinutes is { } minutes
                    ? (minutes / 60.0).ToString("0.##", CultureInfo.InvariantCulture)
                    : "",
            })
            .ToList();

        ShowRows();
    }

    /// <summary>감지 제외된 프로그램은 "제외한 프로그램도 보기"를 켰을 때만 보여 준다. 입력 중인 내용은 유지된다.</summary>
    private void ShowRows()
    {
        bool showIgnored = ShowHiddenOption.IsChecked == true;
        _visible.Clear();
        foreach (var row in _rows.Where(r => showIgnored || !r.Ignored)) _visible.Add(row);

        // 예전 버전이 남긴, 제외 대상인데 DB에 기록이 남아 있는 프로그램
        int leftover = _rows.Count(r => r.Ignored);
        ShowHiddenOption.Content = $"제외 대상인데 기록이 남은 프로그램 보기 ({leftover}개)";
        ShowHiddenOption.Visibility = leftover > 0 || ShowHiddenOption.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        EmptyText.Text = _gamesMode
            ? "게임으로 분류된 프로그램이 없습니다. 프로그램 관리에서 \"게임\" 버튼으로 추가하세요."
            : "표시할 프로그램이 없습니다.";
        EmptyText.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShowHiddenChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ShowRows();
    }

    /// <summary>게임 관리로 보내기 / 프로그램 관리로 돌려보내기. 바로 적용된다.</summary>
    private void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ManageRow row) return;

        _movingRow = true;
        try
        {
            _engine.SetGame(row.Key, isGame: !_gamesMode);
        }
        finally
        {
            _movingRow = false;
        }

        // 다른 줄에 입력 중인 내용은 그대로 두고 이 줄만 뺀다
        _rows.Remove(row);
        ShowRows();
    }

    /// <summary>
    /// "삭제": 이 목록과 DB에서 프로그램과 기록을 완전히 지우고, 앞으로 감지하지 않는다. 바로 적용된다.
    /// 다시 감지하려면 제외 목록 창에서 그 exe 이름을 뺀다.
    /// </summary>
    private void OnDeleteOrRestoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ManageRow row) return;

        string message = row.Ignored
            ? $"\"{row.DefaultName}\" ({row.ExeName})은(는) 이미 감지 제외 대상입니다. 남아 있는 기록을 DB에서 완전히 삭제합니다."
            : $"\"{row.DefaultName}\" ({row.ExeName})을(를) 목록과 DB에서 완전히 삭제하고, 제외 목록에 넣어 앞으로 감지하지 않습니다.";
        var answer = MessageBox.Show(this,
            message + "\n\n삭제한 기록은 되돌릴 수 없습니다. 계속할까요?",
            "프로그램 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _engine.DeleteProgram(row.Key);

        // 다른 줄에 입력 중인 내용은 그대로 두고 이 줄만 뺀다
        _rows.Remove(row);
        ShowRows();
    }

    private void OnGameManageClick(object sender, RoutedEventArgs e) => GameManageRequested?.Invoke();

    private void OnExclusionsClick(object sender, RoutedEventArgs e) => ExclusionsRequested?.Invoke();

    /// <summary>"1.5" → 90분. 비었거나 숫자가 아니거나 0 이하면 알림 없음.</summary>
    private static int? ParseAlertMinutes(string text)
    {
        text = text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hours) || hours <= 0) return null;
        return Math.Max(1, (int)Math.Round(hours * 60));
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _engine.SaveSettings(_rows.Select(r =>
        {
            string name = r.Name.Trim();
            // 이름을 비우거나 원래 이름 그대로면 사용자 지정 이름 없음
            string? custom = name.Length == 0 || name == r.DefaultName ? null : name;
            return new ProgramSetting(r.Key, custom, r.GroupName, ParseAlertMinutes(r.AlertText));
        }));

        Close();
    }
}

public sealed class ManageRow
{
    public required string Key { get; init; }
    public required string DefaultName { get; init; }
    public required string ExeName { get; init; }
    public required string MoveText { get; init; }
    public string? ExePath { get; init; }
    public ImageSource? Icon { get; init; }

    public string Name { get; set; } = "";
    public string GroupName { get; set; } = "";

    /// <summary>감지 제외 상태인지 (기본 제외 대상이거나 사용자가 삭제한 프로그램).</summary>
    public bool Ignored { get; set; }

    public string ActionText => "삭제";

    public ManageRow WithIgnored(bool ignored)
    {
        Ignored = ignored;
        return this;
    }

    /// <summary>알림 간격(시간). 비우면 알림 없음.</summary>
    public string AlertText { get; set; } = "";
    public bool ShowAlert { get; init; }

    public const double AlertWidth = 110;
    public GridLength AlertColumnWidth => new(ShowAlert ? AlertWidth : 0);
    public Visibility AlertVisibility => ShowAlert ? Visibility.Visible : Visibility.Collapsed;
}
