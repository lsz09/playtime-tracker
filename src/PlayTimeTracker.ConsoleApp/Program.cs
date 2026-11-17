using System.Text;
using PlayTimeTracker.Core;

// 2단계: 감지 결과를 세션으로 기록하고 (30초마다 DB 저장), 오늘/이번 주/이번 달 합계를 보여 준다.
//
//   (인자 없음)   기록 시작. S 키로 합계 화면 전환, Ctrl+C로 종료
//   stats         합계만 출력하고 종료 (기록 중인 창과 별개로 실행 가능)
//   --db <경로>   DB 파일 위치 지정 (기본: %LOCALAPPDATA%\PlayTimeTracker\playtime.db)
//   <숫자>        그 횟수만큼만 감지하고 종료 (테스트용)

Console.OutputEncoding = Encoding.UTF8;

string? dbPath = null;
bool statsOnly = false;
int maxScans = int.MaxValue;
for (int a = 0; a < args.Length; a++)
{
    if (args[a] == "--db" && a + 1 < args.Length) dbPath = args[++a];
    else if (args[a].Equals("stats", StringComparison.OrdinalIgnoreCase)) statsOnly = true;
    else if (int.TryParse(args[a], out int n)) maxScans = n;
}

using var db = new PlayTimeDatabase(dbPath);

if (statsOnly)
{
    Console.Write(BuildStats(db, DateTime.Now));
    return;
}

using var mutex = new Mutex(true, "PlayTimeTracker.Recorder", out bool isFirstInstance);
if (!isFirstInstance)
{
    Console.WriteLine("이미 기록 중인 PlayTimeTracker가 있습니다. 합계만 보려면 'stats' 인자를 붙여 실행하세요.");
    return;
}

bool interactive = !Console.IsOutputRedirected && !Console.IsInputRedirected;
var scanInterval = TimeSpan.FromSeconds(2);
var flushInterval = TimeSpan.FromSeconds(30);

var detector = new ProcessDetector();
// 감지 제외: 기본 제외 목록 + 앱에서 삭제한 프로그램 (시작할 때 한 번 읽는다)
var known = db.GetPrograms().ToDictionary(p => p.Key);
bool IsIgnored(ProgramInfo p) =>
    known.TryGetValue(p.Key, out var record) ? record.Hidden : ExclusionList.IsExcluded(p.ExePath, p.ExeName);
var tracker = new SessionTracker();
var lastFlush = DateTime.Now;
string? statsView = null; // null이면 실시간 화면, 아니면 합계 화면

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

for (int i = 0; i < maxScans && !cts.IsCancellationRequested; i++)
{
    var snapshot = SnapshotFilter.Apply(detector.Scan(), IsIgnored);
    tracker.Update(snapshot);

    if (snapshot.Time - lastFlush >= flushInterval)
    {
        tracker.Flush(db);
        lastFlush = snapshot.Time;
    }

    Render(snapshot);

    // 다음 감지까지 대기하면서 키 입력 확인
    var waitUntil = DateTime.UtcNow + scanInterval;
    while (DateTime.UtcNow < waitUntil && !cts.IsCancellationRequested)
    {
        if (interactive && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.S)
        {
            if (statsView == null)
            {
                tracker.Flush(db); // 합계에 방금까지의 기록이 들어가도록
                lastFlush = DateTime.Now;
                statsView = BuildStats(db, DateTime.Now);
            }
            else
            {
                statsView = null;
            }
            Render(snapshot);
        }
        Thread.Sleep(100);
    }
}

// 정상 종료: 모든 세션을 지금 시각으로 마감하고 저장
tracker.CloseAll(DateTime.Now);
tracker.Flush(db);
Console.WriteLine();
Console.WriteLine("기록을 저장하고 종료했습니다.");

void Render(DetectionSnapshot snapshot)
{
    var sb = new StringBuilder();
    if (statsView != null)
    {
        sb.Append(statsView);
        sb.AppendLine();
        sb.AppendLine("(S: 실시간 화면으로 돌아가기 / 기록은 계속 진행 중)");
    }
    else
    {
        sb.AppendLine($"[{snapshot.Time:HH:mm:ss}] PlayTimeTracker 2단계 - 기록 (S: 합계 보기, Ctrl+C: 저장 후 종료)");
        sb.AppendLine($"DB: {db.FilePath}");
        sb.AppendLine();
        sb.AppendLine($"실행 중인 프로그램 ({snapshot.Running.Count}개) - 이번 세션 기준");
        sb.AppendLine("       실행     포커스  프로그램");
        foreach (var r in snapshot.Running)
        {
            var session = tracker.GetOpenSession(r.Program);
            string mark = r.Program == snapshot.Active ? "▶" : " ";
            string count = r.ProcessCount > 1 ? $" x{r.ProcessCount}" : "";
            string run = Format(session?.Duration ?? TimeSpan.Zero);
            string focus = Format(TimeSpan.FromSeconds(session?.FocusSeconds ?? 0));
            sb.AppendLine($" {mark} {run,9}  {focus,9}  {r.Program.DisplayName}  [{r.Program.ExeName}{count}]");
        }
        sb.AppendLine();
        sb.AppendLine($"활성 프로그램: {(snapshot.Active != null ? $"{snapshot.Active.DisplayName} [{snapshot.Active.ExeName}]" : "없음 (바탕화면/작업 표시줄 등)")}");
        sb.AppendLine($"자리 비움: {(snapshot.IsIdle ? "예 (포커스 집계 멈춤)" : "아니오")} (마지막 입력 후 {Format(snapshot.IdleFor)})");
        sb.AppendLine($"마지막 DB 저장: {lastFlush:HH:mm:ss} (30초마다)");
    }

    if (interactive) Console.Clear();
    Console.Write(sb.ToString());
    if (!interactive) Console.WriteLine(new string('-', 40));
}

static string BuildStats(PlayTimeDatabase db, DateTime now)
{
    var sb = new StringBuilder();
    sb.AppendLine($"[{now:yyyy-MM-dd HH:mm:ss}] 프로그램별 사용 시간 (DB에 저장된 기록 기준)");
    AppendPeriod(sb, db, "오늘", Stats.Today(now));
    AppendPeriod(sb, db, "이번 주", Stats.ThisWeek(now));
    AppendPeriod(sb, db, "이번 달", Stats.ThisMonth(now));
    return sb.ToString();
}

static void AppendPeriod(StringBuilder sb, PlayTimeDatabase db, string title, (DateTime From, DateTime To) range)
{
    sb.AppendLine();
    sb.AppendLine($"■ {title} ({range.From:M/d} ~ {range.To.AddDays(-1):M/d})");
    var totals = Stats.Summarize(db, range.From, range.To);
    if (totals.Count == 0)
    {
        sb.AppendLine("   (기록 없음)");
        return;
    }
    sb.AppendLine("        실행     포커스  프로그램");
    foreach (var t in totals)
        sb.AppendLine($"   {Format(t.RunTime),9}  {Format(t.FocusTime),9}  {(t.IsGame ? "(게임) " : "")}{t.DisplayName}  [{t.Detail}]");
}

static string Format(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
