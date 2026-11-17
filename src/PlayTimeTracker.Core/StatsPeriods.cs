namespace PlayTimeTracker.Core;

/// <summary>통계 창에서 고르는 기간과 비교 기간. 모든 구간은 [From, To) 이고 날짜 경계(00:00)에 맞춘다.</summary>
public static class StatsPeriods
{
    /// <summary>
    /// period: today, yesterday, week, lastweek, month, lastmonth, last7, last30, all, custom.
    /// custom은 고른 시작일~끝일(끝일 포함)이며, 순서가 뒤바뀌어 있으면 바로잡는다.
    /// </summary>
    public static (DateTime From, DateTime To) Range(string period, DateTime now, DateTime? customFrom = null, DateTime? customTo = null)
    {
        var today = now.Date;
        switch (period)
        {
            case "yesterday": return (today.AddDays(-1), today);
            case "week": return Stats.ThisWeek(now);
            case "lastweek":
            {
                var (from, _) = Stats.ThisWeek(now);
                return (from.AddDays(-7), from);
            }
            case "month": return Stats.ThisMonth(now);
            case "lastmonth":
            {
                var (from, _) = Stats.ThisMonth(now);
                return (from.AddMonths(-1), from);
            }
            case "last7": return (today.AddDays(-6), today.AddDays(1));
            case "last30": return (today.AddDays(-29), today.AddDays(1));
            case "all": return (new DateTime(2000, 1, 1), today.AddDays(1));
            case "custom":
            {
                var first = (customFrom ?? today).Date;
                var last = (customTo ?? today).Date;
                if (last < first) (first, last) = (last, first);
                return (first, last.AddDays(1));
            }
            default: return Stats.Today(now);
        }
    }

    /// <summary>
    /// 비교 기간. compare: yesterday(어제 하루), lastweek(지난주 전체), lastmonth(지난달 전체),
    /// custom(고른 날부터 보는 기간과 같은 길이), none(비교 안 함).
    /// </summary>
    public static (DateTime From, DateTime To)? CompareRange(string compare, DateTime now, DateTime from, DateTime to, DateTime? customFrom = null)
    {
        var length = to - from;
        switch (compare)
        {
            case "yesterday":
            case "lastweek":
            case "lastmonth":
                return Range(compare, now);
            case "custom":
            {
                var start = (customFrom ?? from - length).Date;
                return (start, start + length);
            }
            default: return null;
        }
    }

    /// <summary>구간에 이름이 있으면 이름(어제, 지난주 등), 없으면 날짜.</summary>
    public static string Label(DateTime from, DateTime to, DateTime now)
    {
        foreach (var (period, name) in new[]
                 {
                     ("today", "오늘"), ("yesterday", "어제"), ("week", "이번 주"),
                     ("lastweek", "지난주"), ("month", "이번 달"), ("lastmonth", "지난달"),
                 })
        {
            if (Range(period, now) == (from, to)) return name;
        }
        return Dates(from, to, withYear: false);
    }

    /// <summary>"11월 3일" 또는 "11월 3일 ~ 11월 9일".</summary>
    public static string Dates(DateTime from, DateTime to, bool withYear)
    {
        var last = to.AddDays(-1);
        string first = withYear ? $"{from:yyyy년 M월 d일}" : $"{from:M월 d일}";
        if (from.Date == last.Date) return first;
        return from.Year == last.Year ? $"{first} ~ {last:M월 d일}" : $"{first} ~ {last:yyyy년 M월 d일}";
    }
}
