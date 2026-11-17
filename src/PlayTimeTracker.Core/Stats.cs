namespace PlayTimeTracker.Core;

/// <summary>통계의 한 줄. 묶음이면 Detail에 속한 exe들이 나열된다.</summary>
public sealed record ProgramTotal(
    string Key,
    string DisplayName,
    string Detail,
    string? IconPath,
    bool IsGame,
    TimeSpan RunTime,
    TimeSpan FocusTime);

/// <summary>하루치 사용 시간.</summary>
public sealed record DayTotal(DateTime Date, TimeSpan RunTime, TimeSpan FocusTime);

/// <summary>합계는 저장하지 않고, 조회할 때 세션에서 계산한다.</summary>
public static class Stats
{
    public static (DateTime From, DateTime To) Today(DateTime now) =>
        (now.Date, now.Date.AddDays(1));

    /// <summary>이번 주 (월요일 시작).</summary>
    public static (DateTime From, DateTime To) ThisWeek(DateTime now)
    {
        int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        var monday = now.Date.AddDays(-daysSinceMonday);
        return (monday, monday.AddDays(7));
    }

    public static (DateTime From, DateTime To) ThisMonth(DateTime now)
    {
        var first = new DateTime(now.Year, now.Month, 1);
        return (first, first.AddMonths(1));
    }

    /// <summary>
    /// [from, to) 구간의 합계. 숨긴 프로그램은 빼고, 같은 묶음 태그의 프로그램은 한 줄로 합친다.
    /// - 태그가 여러 개인 프로그램은 태그마다 들어가므로, 줄끼리 더하면 중복될 수 있다.
    /// - 구간 경계(자정 등)를 넘는 세션은 구간 안쪽 부분만 센다.
    /// - 포커스 초는 세션 단위로만 저장되므로, 잘린 세션의 포커스는 실행 시간 비율로 나눈다.
    /// - 묶음의 실행 시간은 겹치는 시간을 한 번만 센다 (런처와 게임이 동시에 켜져 있어도 두 배가 되지 않음).
    /// </summary>
    public static List<ProgramTotal> Summarize(PlayTimeDatabase db, DateTime from, DateTime to)
    {
        var entries = new Dictionary<string, Entry>();

        foreach (var s in db.QuerySessions(from, to))
        {
            if (s.Hidden) continue;

            double duration = (s.End - s.Start).TotalSeconds;
            if (duration <= 0) continue;

            var start = s.Start > from ? s.Start : from;
            var end = s.End < to ? s.End : to;
            double run = (end - start).TotalSeconds;
            if (run <= 0) continue;

            // 태그가 여러 개인 프로그램은 태그마다 한 번씩 들어간다
            foreach (var (key, name) in Grouping.Entries(s.ProgramKey, s.CustomName ?? s.DisplayName, s.GroupName))
            {
                if (!entries.TryGetValue(key, out var entry))
                {
                    entry = new Entry(key, name, s.ExePath);
                    entries[key] = entry;
                }
                if (!entry.ExeNames.Contains(s.ExeName)) entry.ExeNames.Add(s.ExeName);
                // 묶음은 속한 프로그램 중 하나라도 게임이면 게임
                entry.IsGame |= s.IsGame;
                entry.Intervals.Add((start, end));
                entry.Focus += s.FocusSeconds * (run / duration);
            }
        }

        return entries.Values
            .Select(e =>
            {
                double run = UnionSeconds(e.Intervals);
                return new ProgramTotal(
                    e.Key, e.Name, string.Join(", ", e.ExeNames), e.IconPath, e.IsGame,
                    TimeSpan.FromSeconds(Math.Round(run)),
                    TimeSpan.FromSeconds(Math.Round(Math.Min(e.Focus, run))));
            })
            .OrderByDescending(t => t.RunTime)
            .ThenBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 한 줄(프로그램 하나 또는 묶음 태그 하나)에 속하는 세션들. 최근 것부터.
    /// entryKey는 ProgramTotal.Key와 같은 값이다.
    /// </summary>
    public static List<StoredSession> SessionsOf(PlayTimeDatabase db, string entryKey, DateTime from, DateTime to) =>
        db.QuerySessions(from, to)
            .Where(s => !s.Hidden && s.End > s.Start &&
                        Grouping.Entries(s.ProgramKey, "", s.GroupName).Any(e => e.Key == entryKey))
            .OrderByDescending(s => s.Start)
            .ToList();

    /// <summary>
    /// 한 줄의 날짜별 사용 시간. firstDay부터 lastDay까지 하루도 빠짐없이 돌려준다 (안 쓴 날은 0).
    /// 자정을 넘기는 세션은 날짜별로 나누고, 묶음은 겹치는 시간을 한 번만 센다.
    /// </summary>
    public static List<DayTotal> DailyTotals(PlayTimeDatabase db, string entryKey, DateTime firstDay, DateTime lastDay)
    {
        firstDay = firstDay.Date;
        lastDay = lastDay.Date;
        var sessions = SessionsOf(db, entryKey, firstDay, lastDay.AddDays(1));

        var result = new List<DayTotal>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var next = day.AddDays(1);
            var intervals = new List<(DateTime Start, DateTime End)>();
            double focus = 0;
            foreach (var s in sessions)
            {
                var start = s.Start > day ? s.Start : day;
                var end = s.End < next ? s.End : next;
                double run = (end - start).TotalSeconds;
                if (run <= 0) continue;
                intervals.Add((start, end));
                focus += s.FocusSeconds * (run / (s.End - s.Start).TotalSeconds);
            }

            double total = intervals.Count > 0 ? UnionSeconds(intervals) : 0;
            result.Add(new DayTotal(day,
                TimeSpan.FromSeconds(Math.Round(total)),
                TimeSpan.FromSeconds(Math.Round(Math.Min(focus, total)))));
        }
        return result;
    }

    /// <summary>겹치는 구간을 합친 뒤의 총 길이(초).</summary>
    private static double UnionSeconds(List<(DateTime Start, DateTime End)> intervals)
    {
        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        double total = 0;
        DateTime curStart = intervals[0].Start, curEnd = intervals[0].End;
        for (int i = 1; i < intervals.Count; i++)
        {
            var (s, e) = intervals[i];
            if (s <= curEnd)
            {
                if (e > curEnd) curEnd = e;
            }
            else
            {
                total += (curEnd - curStart).TotalSeconds;
                curStart = s;
                curEnd = e;
            }
        }
        return total + (curEnd - curStart).TotalSeconds;
    }

    private sealed class Entry(string key, string name, string? iconPath)
    {
        public string Key { get; } = key;
        public string Name { get; } = name;
        public string? IconPath { get; } = iconPath;
        public List<string> ExeNames { get; } = new();
        public List<(DateTime Start, DateTime End)> Intervals { get; } = new();
        public double Focus { get; set; }
        public bool IsGame { get; set; }
    }
}
