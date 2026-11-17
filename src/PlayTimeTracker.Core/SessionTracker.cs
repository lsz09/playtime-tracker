namespace PlayTimeTracker.Core;

/// <summary>프로그램이 켜져 있던 한 구간. 실행 시간 = End - Start, 포커스 시간 = FocusSeconds.</summary>
public sealed class Session
{
    public required ProgramInfo Program { get; init; }
    public required DateTime Start { get; init; }
    public DateTime End { get; set; }
    public double FocusSeconds { get; set; }

    /// <summary>DB에 저장된 뒤의 행 번호. 아직 저장 전이면 0.</summary>
    public long DbId { get; set; }

    public TimeSpan Duration => End - Start;
}

/// <summary>
/// 감지 결과를 받아 세션을 메모리에 누적한다. DB에는 직접 쓰지 않고,
/// 호출하는 쪽이 주기적으로 Flush를 불러 저장한다.
/// </summary>
public sealed class SessionTracker
{
    /// <summary>감지 간격이 이보다 벌어지면 (절전 등) 그 사이는 사용 시간으로 치지 않는다.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, Session> _open = new();
    private readonly List<Session> _closedUnsaved = new();
    private readonly HashSet<string> _seen = new();
    private DateTime? _lastTime;

    public IReadOnlyCollection<Session> OpenSessions => _open.Values;

    public Session? GetOpenSession(ProgramInfo program) => _open.GetValueOrDefault(program.Key);

    public void Update(DetectionSnapshot snapshot)
    {
        var now = snapshot.Time;
        var elapsed = _lastTime is { } last ? now - last : TimeSpan.Zero;
        _lastTime = now;

        if (elapsed < TimeSpan.Zero || elapsed > MaxGap)
        {
            // 절전에서 깨어났거나 시계가 바뀜: 기존 세션은 마지막으로 본 시각에서 끝낸다
            CloseAll(end: null);
            elapsed = TimeSpan.Zero;
        }

        // 포커스: 직전 감지 때도 켜져 있던 활성 프로그램에만, 자리 비움이 아닐 때 누적
        if (snapshot.Active != null && !snapshot.IsIdle && _open.TryGetValue(snapshot.Active.Key, out var active))
            active.FocusSeconds += elapsed.TotalSeconds;

        _seen.Clear();
        foreach (var running in snapshot.Running)
        {
            var key = running.Program.Key;
            _seen.Add(key);
            if (!_open.TryGetValue(key, out var session))
            {
                session = new Session { Program = running.Program, Start = now };
                _open[key] = session;
            }
            session.End = now;
        }

        if (_open.Count > _seen.Count)
        {
            foreach (var key in _open.Keys.Where(k => !_seen.Contains(k)).ToList())
            {
                var session = _open[key];
                session.End = now;
                _open.Remove(key);
                _closedUnsaved.Add(session);
            }
        }
    }

    /// <summary>프로그램의 진행 중이거나 아직 저장 전인 세션을 저장하지 않고 버린다 (기록 삭제 시).</summary>
    public void Discard(string programKey)
    {
        _open.Remove(programKey);
        _closedUnsaved.RemoveAll(s => s.Program.Key == programKey);
    }

    /// <summary>모든 세션을 마감한다. end가 null이면 각 세션을 마지막으로 본 시각에서 끝낸다.</summary>
    public void CloseAll(DateTime? end)
    {
        foreach (var session in _open.Values)
        {
            if (end is { } e && e > session.End) session.End = e;
            _closedUnsaved.Add(session);
        }
        _open.Clear();
    }

    /// <summary>마감된 세션과 진행 중인 세션의 현재 상태를 DB에 저장한다.</summary>
    public void Flush(PlayTimeDatabase db)
    {
        db.Save(_closedUnsaved.Concat(_open.Values));
        _closedUnsaved.Clear();
    }
}
