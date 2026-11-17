using System.Windows.Threading;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>
/// 2초마다 감지 → 세션 누적, 30초마다 DB 저장. 화면이 떠 있든 말든 항상 돈다.
/// 화면 갱신은 하지 않는다 (각 창이 보일 때만 스스로 갱신).
/// 감지 제외 대상(기본 제외 목록 + 사용자가 삭제한 프로그램)은 기록하지 않는다.
/// 묶음/이름/게임 설정은 보여 줄 때만 적용한다.
/// </summary>
public sealed class TrackingEngine : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly ProcessDetector _detector = new();
    private readonly DispatcherTimer _timer;
    private DateTime _lastFlush = DateTime.Now;
    private bool _disposed;

    public SessionTracker Tracker { get; } = new();
    public PlayTimeDatabase Db { get; }
    public DetectionSnapshot? LastSnapshot { get; private set; }

    /// <summary>마지막으로 DB에 저장한 시각. DB에서 읽은 합계는 이 시각까지의 값이다.</summary>
    public DateTime LastFlushTime => _lastFlush;

    /// <summary>프로그램 키 → 설정. 아직 DB에 저장된 적 없는 프로그램은 들어 있지 않다.</summary>
    public IReadOnlyDictionary<string, ProgramRecord> Settings { get; private set; }

    /// <summary>지금 플레이 중인 게임의 이름. 없으면 null.</summary>
    public string? CurrentGameName { get; private set; }

    public event Action? SettingsChanged;
    public event Action? CurrentGameChanged;

    /// <summary>어떤 프로그램이 게임 관리로 들어가거나 나왔을 때.</summary>
    public event Action? GameListChanged;

    /// <summary>기록된 프로그램 목록이 바뀌었을 때 (새 프로그램이 처음 기록됨, 삭제됨).</summary>
    public event Action? ProgramListChanged;

    private int _programVersion;

    /// <summary>게임 시간 알림 (제목, 내용).</summary>
    public event Action<string, string>? AlertRaised;

    // 줄 키 → (알림 간격(분), 지금까지 지난 간격 수)
    private readonly Dictionary<string, (int Interval, int Count)> _alertState = new();

    public TrackingEngine(string? dbPath)
    {
        Db = new PlayTimeDatabase(dbPath);
        Settings = LoadSettings();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ScanInterval };
        _timer.Tick += (_, _) => Scan();
    }

    public void Start()
    {
        Scan();
        _timer.Start();
    }

    private void Scan()
    {
        // 제외 대상은 여기서 걸러져 기록도 화면 표시도 되지 않는다
        var snapshot = SnapshotFilter.Apply(_detector.Scan(), IsIgnored);
        Tracker.Update(snapshot);
        LastSnapshot = snapshot;

        if (snapshot.Time - _lastFlush >= FlushInterval) FlushNow();

        var view = GetLiveView();
        UpdateCurrentGame(view);
        CheckAlerts(view);
    }

    /// <summary>지금까지의 기록을 바로 DB에 저장한다 (통계 조회 직전 등).</summary>
    public void FlushNow()
    {
        if (_disposed) return;
        Tracker.Flush(Db);
        _lastFlush = DateTime.Now;

        // 처음 보는 프로그램이 방금 DB에 들어갔으면 설정과 관리 창 목록에 바로 반영한다
        if (Db.ProgramVersion != _programVersion)
        {
            _programVersion = Db.ProgramVersion;
            Settings = LoadSettings();
            ProgramListChanged?.Invoke();
        }
    }

    public void SaveSettings(IEnumerable<ProgramSetting> settings)
    {
        Db.SaveProgramSettings(settings);
        Settings = LoadSettings();
        UpdateCurrentGame();
        SettingsChanged?.Invoke();
    }

    /// <summary>감지 제외 대상인지. 한 번도 기록된 적 없는 프로그램은 기본 제외 목록으로 판단한다.</summary>
    public bool IsIgnored(ProgramInfo program) =>
        Settings.TryGetValue(program.Key, out var record)
            ? record.Hidden
            : ExclusionList.IsExcluded(program.ExePath, program.ExeName);

    /// <summary>
    /// 프로그램을 DB에서 완전히 지우고(기록과 프로그램 줄 모두) 앞으로 감지하지 않는다. 되돌릴 수 없다.
    /// 이미 제외 대상이 아니라면 exe 이름을 제외 목록에 넣는다 (제외 목록 창에서 빼면 다시 감지된다).
    /// </summary>
    public void DeleteProgram(string key)
    {
        if (!Settings.TryGetValue(key, out var record)) return;
        Purge(record);
        if (!ExclusionList.IsExcluded(record.ExePath, record.ExeName)) Db.AddExclusion(record.ExeName);
        ApplyExclusions();
    }

    /// <summary>이 exe 이름으로 기록된 프로그램 (제외 목록에 추가하기 전에 확인용).</summary>
    public List<ProgramRecord> FindByExeName(string exeName) =>
        Settings.Values.Where(p => p.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase)).ToList();

    private void Purge(ProgramRecord record)
    {
        Tracker.Discard(record.Key);
        Db.PurgeProgram(record.Key);
    }

    /// <summary>프로그램을 게임으로 지정하거나 해제한다. 바로 저장된다.</summary>
    public void SetGame(string key, bool isGame)
    {
        if (!Settings.TryGetValue(key, out var record)) return;
        // 자동 판별과 같으면 따로 저장하지 않는다
        bool auto = GameDetector.IsGame(record.ExePath, record.ExeName);
        Db.SetGameOverride(key, isGame == auto ? null : isGame);
        Settings = LoadSettings();
        UpdateCurrentGame();
        GameListChanged?.Invoke();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// 실행 중인 프로그램에 숨김/묶음/게임 설정을 적용한, 화면에 보일 줄 목록 (이름순).
    /// </summary>
    public LiveView GetLiveView()
    {
        var view = new LiveView();
        var snapshot = LastSnapshot;
        if (snapshot == null) return view;

        var entries = new Dictionary<string, LiveEntry>();
        foreach (var running in snapshot.Running)
        {
            var program = running.Program;
            Settings.TryGetValue(program.Key, out var setting);
            bool isActive = program == snapshot.Active;
            string ownName = setting?.OwnName ?? program.DisplayName;
            // 설정에 없는(방금 처음 본) 프로그램도 exe 이름과 경로로 바로 판별한다
            bool hidden = setting?.Hidden ?? ExclusionList.IsExcluded(program.ExePath, program.ExeName);
            bool isGame = setting?.IsGame ?? GameDetector.IsGame(program.ExePath, program.ExeName);
            var session = Tracker.GetOpenSession(program);
            string detail = running.ProcessCount > 1 ? $"{program.ExeName} ×{running.ProcessCount}" : program.ExeName;

            bool first = true;
            // 태그가 여러 개인 프로그램은 태그마다 한 줄씩 들어간다
            foreach (var (key, name) in Grouping.Entries(program.Key, ownName, setting?.GroupName))
            {
                if (first && isActive) view.ActiveName = name;
                first = false;
                if (hidden) continue;

                if (!entries.TryGetValue(key, out var entry))
                {
                    entry = new LiveEntry(key, name, program.ExePath);
                    entries[key] = entry;
                }
                entry.Details.Add(detail);
                entry.IsActive |= isActive;
                entry.IsGame |= isGame;
                // 묶음은 속한 프로그램의 알림 간격 중 가장 짧은 것을 쓴다
                if (setting?.AlertMinutes is { } alert && (entry.AlertMinutes == null || alert < entry.AlertMinutes))
                    entry.AlertMinutes = alert;
                if (session != null)
                {
                    // 묶음의 실행 시간은 가장 먼저 켜진 프로그램 기준
                    if (entry.Start == null || session.Start < entry.Start) entry.Start = session.Start;
                    entry.FocusSeconds += session.FocusSeconds;
                }
            }
            if (hidden) view.HiddenCount++;
        }

        view.Entries = entries.Values
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // 플레이 중인 게임: 지금 활성인 게임이 우선, 없으면 가장 최근에 켠 게임
        view.CurrentGame = view.Entries
            .Where(e => e.IsGame)
            .OrderByDescending(e => e.IsActive)
            .ThenByDescending(e => e.Start)
            .FirstOrDefault();
        return view;
    }

    private void UpdateCurrentGame() => UpdateCurrentGame(GetLiveView());

    private void UpdateCurrentGame(LiveView view)
    {
        string? name = view.CurrentGame?.Name;
        if (name == CurrentGameName) return;
        CurrentGameName = name;
        CurrentGameChanged?.Invoke();
    }

    /// <summary>
    /// 알림을 켠 프로그램(게임 포함)이 정한 간격만큼 실행될 때마다 한 번씩 알린다 (1시간이면 1시간, 2시간, 3시간째...).
    /// 기준은 이번에 켠 뒤로의 실행 시간이다.
    /// </summary>
    private void CheckAlerts(LiveView view)
    {
        var now = DateTime.Now;
        var seen = new HashSet<string>();
        foreach (var entry in view.Entries)
        {
            if (entry.AlertMinutes is not { } interval || interval <= 0 || entry.Start is not { } start) continue;
            seen.Add(entry.Key);

            int count = (int)((now - start).TotalMinutes / interval);
            // 처음 보거나 간격을 바꾼 직후에는 알리지 않고 기준만 잡는다
            if (_alertState.TryGetValue(entry.Key, out var state) && state.Interval == interval && count > state.Count)
                AlertRaised?.Invoke(entry.IsGame ? "게임 시간 알림" : "사용 시간 알림", $"{entry.Name}: {DurationText(count * interval)}째 실행 중입니다.");
            _alertState[entry.Key] = (interval, count);
        }
        foreach (var key in _alertState.Keys.Where(k => !seen.Contains(k)).ToList()) _alertState.Remove(key);
    }

    private static string DurationText(int minutes)
    {
        int h = minutes / 60, m = minutes % 60;
        return h > 0 && m > 0 ? $"{h}시간 {m}분" : h > 0 ? $"{h}시간" : $"{m}분";
    }

    /// <summary>제외 목록에 exe 이름을 추가한다. 그 프로그램은 바로 감지되지 않고, 이미 쌓인 기록도 DB에서 지운다.</summary>
    public void AddExclusion(string exeName)
    {
        foreach (var record in FindByExeName(exeName)) Purge(record);
        Db.AddExclusion(exeName);
        ApplyExclusions();
    }

    /// <summary>
    /// 제외 목록에서 뺀다. 그 프로그램이 지금 실행 중이면 바로 감지해서 기록을 시작하고,
    /// 프로그램 관리 목록에도 곧바로 나오게 한다.
    /// </summary>
    public void RemoveExclusion(string exeName)
    {
        Db.RemoveExclusion(exeName);
        ApplyExclusions();
        Scan();
        FlushNow();
    }

    private void ApplyExclusions()
    {
        ExclusionList.SetNames(Db.GetExclusions());
        Settings = LoadSettings();
        if (LastSnapshot != null) LastSnapshot = SnapshotFilter.Apply(LastSnapshot, IsIgnored);
        UpdateCurrentGame();
        SettingsChanged?.Invoke();
    }

    private Dictionary<string, ProgramRecord> LoadSettings() =>
        Db.GetPrograms().ToDictionary(p => p.Key);

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        Tracker.CloseAll(DateTime.Now);
        Tracker.Flush(Db);
        _disposed = true;
        Db.Dispose();
    }
}

public sealed class LiveView
{
    public List<LiveEntry> Entries { get; set; } = new();
    public LiveEntry? CurrentGame { get; set; }
    public string? ActiveName { get; set; }
    public int HiddenCount { get; set; }
}

/// <summary>화면의 한 줄 (프로그램 하나 또는 묶음 하나).</summary>
public sealed class LiveEntry(string key, string name, string? iconPath)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string? IconPath { get; } = iconPath;
    public List<string> Details { get; } = new();
    public bool IsActive { get; set; }
    public bool IsGame { get; set; }
    public int? AlertMinutes { get; set; }
    public DateTime? Start { get; set; }

    /// <summary>마지막 감지까지 누적된 포커스 초.</summary>
    public double FocusSeconds { get; set; }
}

public static class TimeText
{
    public static string Format(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
}
