using System.Windows;

namespace PlayTimeTracker.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrackingEngine? _engine;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private StatsWindow? _stats;
    private ManageWindow? _manage;
    private ManageWindow? _gameManage;
    private ExclusionWindow? _exclusions;
    private SettingsWindow? _settings;
    private MiniWindow? _mini;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 콘솔 버전과 같은 이름: 기록은 한 번에 하나만
        _mutex = new Mutex(true, "PlayTimeTracker.Recorder", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("PlayTimeTracker가 이미 실행 중입니다. 트레이 아이콘을 확인하세요.", "PlayTimeTracker");
            Shutdown();
            return;
        }

        // --db <경로>: DB 위치 지정, --stats / --manage / --games: 해당 창도 함께 열기
        string? dbPath = null;
        string? detailName = null; // --detail <표시 이름>: 그 프로그램의 상세 창도 함께 열기
        bool openStats = false, openManage = false, openGames = false, openExclusions = false, openSettings = false, openMini = false, testAlert = false;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--db" && i + 1 < e.Args.Length) dbPath = e.Args[++i];
            else if (e.Args[i] == "--stats") openStats = true;
            else if (e.Args[i] == "--manage") openManage = true;
            else if (e.Args[i] == "--games") openGames = true;
            else if (e.Args[i] == "--exclusions") openExclusions = true;
            else if (e.Args[i] == "--settings") openSettings = true;
            else if (e.Args[i] == "--mini") openMini = true;
            else if (e.Args[i] == "--test-alert") testAlert = true;
            else if (e.Args[i] == "--detail" && i + 1 < e.Args.Length) detailName = e.Args[++i];
        }

        _engine = new TrackingEngine(dbPath);
        _engine.Start();
        UiScale.Load(_engine.Db);
        UiTheme.Load(_engine.Db);

        _main = new MainWindow(_engine);
        _main.StatsRequested += ShowStats;
        _main.ManageRequested += ShowManage;
        _main.HiddenToTray += OnHiddenToTray;
        _main.DetailRequested += ShowDetail;
        _main.SettingsRequested += ShowSettings;

        _tray = new TrayIcon(ShowMain, ShowStats, ShowManage, ShowGameManage, ShowSettings, Shutdown);
        _tray.SetCurrentGame(_engine.CurrentGameName);
        _engine.CurrentGameChanged += () => _tray?.SetCurrentGame(_engine.CurrentGameName);
        _engine.AlertRaised += ShowAlert;

        SessionEnding += (_, _) => _engine?.FlushNow(); // 로그오프/시스템 종료 대비

        _main.Show();
        if (openStats) ShowStats();
        if (openManage) ShowManage();
        if (openGames) ShowGameManage();
        if (openExclusions) ShowExclusions();
        if (openSettings) ShowSettings();
        if (openMini) ShowMini();
        if (testAlert) ShowTestAlert();
        if (detailName != null)
        {
            var all = PlayTimeTracker.Core.Stats.Summarize(_engine.Db, new DateTime(2000, 1, 1), DateTime.Now.AddDays(1));
            var match = all.FirstOrDefault(t => t.DisplayName == detailName);
            if (match != null) ShowDetail(match.Key, match.DisplayName);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _engine?.Dispose(); // 세션 마감 + 저장
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void ShowMain()
    {
        if (_main == null) return;
        _mini?.Close(); // 메인 창이 뜨면 작은 창은 필요 없다
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void ShowStats()
    {
        if (_engine == null) return;
        if (_stats == null)
        {
            _stats = new StatsWindow(_engine);
            _stats.Closed += (_, _) => _stats = null;
            _stats.Show();
        }
        if (_stats.WindowState == WindowState.Minimized) _stats.WindowState = WindowState.Normal;
        _stats.Activate();
    }

    private void ShowManage()
    {
        if (_engine == null) return;
        if (_manage == null)
        {
            _manage = new ManageWindow(_engine, gamesMode: false);
            _manage.GameManageRequested += ShowGameManage;
            _manage.ExclusionsRequested += ShowExclusions;
            _manage.Closed += (_, _) => _manage = null;
            _manage.Show();
        }
        if (_manage.WindowState == WindowState.Minimized) _manage.WindowState = WindowState.Normal;
        _manage.Activate();
    }

    private void ShowGameManage()
    {
        if (_engine == null) return;
        if (_gameManage == null)
        {
            _gameManage = new ManageWindow(_engine, gamesMode: true);
            _gameManage.ExclusionsRequested += ShowExclusions;
            _gameManage.Closed += (_, _) => _gameManage = null;
            _gameManage.Show();
        }
        if (_gameManage.WindowState == WindowState.Minimized) _gameManage.WindowState = WindowState.Normal;
        _gameManage.Activate();
    }

    /// <summary>프로그램 하나(또는 묶음 하나)의 상세 기록. 여러 개를 동시에 열 수 있다.</summary>
    private void ShowDetail(string entryKey, string name)
    {
        if (_engine == null) return;
        new DetailWindow(_engine, entryKey, name).Show();
    }

    private void ShowExclusions()
    {
        if (_engine == null) return;
        if (_exclusions == null)
        {
            _exclusions = new ExclusionWindow(_engine);
            _exclusions.Closed += (_, _) => _exclusions = null;
            _exclusions.Show();
        }
        if (_exclusions.WindowState == WindowState.Minimized) _exclusions.WindowState = WindowState.Normal;
        _exclusions.Activate();
    }

    private void ShowSettings()
    {
        if (_engine == null) return;
        if (_settings == null)
        {
            _settings = new SettingsWindow(_engine);
            _settings.TestAlertRequested += ShowTestAlert;
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    /// <summary>사용 시간 알림을 팝업 창으로 띄운다.</summary>
    private void ShowAlert(string title, string message) => new AlertWindow(title, message).Show();

    private void ShowTestAlert() => ShowAlert("사용 시간 알림 (테스트)", "예시 프로그램: 1시간째 실행 중입니다.");

    /// <summary>포커스된 프로그램과 오늘 사용 시간을 보여 주는 작은 창.</summary>
    private void ShowMini()
    {
        if (_engine == null || _mini != null) return;
        _mini = new MiniWindow(_engine);
        _mini.OpenMainRequested += ShowMain;
        _mini.Closed += (_, _) => _mini = null;
        _mini.Show();
    }

    private void OnHiddenToTray()
    {
        // 설정에서 켜 두었으면 메인 창을 닫을 때 작은 창을 띄운다
        // (이때는 트레이 안내 알림을 띄우지 않는다: 알림이 작은 창 자리를 가린다)
        if (_engine != null && SettingsWindow.IsMiniWindowEnabled(_engine.Db))
        {
            ShowMini();
            return;
        }

        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray?.ShowHint("트레이에서 계속 기록 중입니다. 종료하려면 트레이 아이콘을 우클릭하세요.");
    }
}
