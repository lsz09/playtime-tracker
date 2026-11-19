using Microsoft.Data.Sqlite;

namespace PlayTimeTracker.Core;

/// <summary>DB에서 읽어 온 세션 한 건 (시각은 로컬 시간)과 그 프로그램의 설정.</summary>
public sealed record StoredSession(
    string ProgramKey,
    string ExeName,
    string? ExePath,
    string DisplayName,
    string? CustomName,
    string? GroupName,
    bool? HiddenOverride,
    bool? IsGameOverride,
    DateTime Start,
    DateTime End,
    long FocusSeconds)
{
    public bool Hidden => HiddenOverride ?? ExclusionList.IsExcluded(ExePath, ExeName);
    public bool IsGame => IsGameOverride ?? GameDetector.IsGame(ExePath, ExeName);
}

/// <summary>기록된 적 있는 프로그램과 사용자가 정한 설정 (표시 이름, 묶음 태그, 숨김).</summary>
public sealed record ProgramRecord(
    string Key,
    string ExeName,
    string? ExePath,
    string DisplayName,
    string? CustomName,
    string? GroupName,
    bool? HiddenOverride,
    bool? IsGameOverride,
    int? AlertMinutes)
{
    /// <summary>묶음 태그 목록. GroupName에 쉼표로 구분해 저장한다.</summary>
    public IReadOnlyList<string> Tags => Grouping.ParseTags(GroupName);

    /// <summary>
    /// 감지 제외 대상인지. 제외된 프로그램은 기록하지 않고 목록과 통계에도 나오지 않는다.
    /// 사용자가 정한 값이 있으면 그 값, 없으면 기본 제외 대상(시스템, 런처, 메신저, 오피스)인지.
    /// </summary>
    public bool Hidden => HiddenOverride ?? ExclusionList.IsExcluded(ExePath, ExeName);

    /// <summary>사용자가 정한 값이 있으면 그 값, 없으면 설치 경로로 추정한 값.</summary>
    public bool IsGame => IsGameOverride ?? GameDetector.IsGame(ExePath, ExeName);

    /// <summary>묶음에 속하지 않았을 때의 이름: 사용자 지정 이름 → exe에서 읽은 이름.</summary>
    public string OwnName => CustomName ?? DisplayName;
}

/// <summary>GroupName은 쉼표로 구분한 태그들. 게임 여부와 감지 제외는 각각 SetGameOverride, SetIgnored로 따로 바꾼다.</summary>
/// <summary>AlertMinutes: 게임이 이만큼 실행될 때마다 알림. null이면 알림 없음.</summary>
public sealed record ProgramSetting(string Key, string? CustomName, string? GroupName, int? AlertMinutes = null);

public static class Grouping
{
    /// <summary>"A, B" → ["A", "B"]. 빈 항목과 중복은 버린다.</summary>
    public static IReadOnlyList<string> ParseTags(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return Array.Empty<string>();
        return groupName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
    }

    /// <summary>저장용으로 정리한 태그 문자열. 태그가 없으면 null.</summary>
    public static string? NormalizeTags(string? groupName)
    {
        var tags = ParseTags(groupName);
        return tags.Count == 0 ? null : string.Join(", ", tags);
    }

    /// <summary>
    /// 프로그램이 화면과 통계에서 속하는 줄들. 태그가 없으면 자기 자신 한 줄,
    /// 태그가 여러 개면 태그마다 한 줄씩 (같은 태그를 가진 프로그램끼리 합쳐진다).
    /// </summary>
    public static IEnumerable<(string Key, string Name)> Entries(string programKey, string ownName, string? groupName)
    {
        var tags = ParseTags(groupName);
        if (tags.Count == 0)
        {
            yield return (programKey, ownName);
            yield break;
        }
        foreach (var tag in tags) yield return ("group:" + tag, tag);
    }
}

/// <summary>
/// SQLite 저장소. 세션(프로그램, 시작 시각, 종료 시각, 포커스 초)만 저장하고 합계는 저장하지 않는다.
/// 시각은 UTC 유닉스 초로 저장한다. 묶음/숨김/이름 설정은 프로그램 단위로 따로 저장하고
/// 조회할 때 적용하므로, 설정을 바꿔도 원본 기록은 그대로다.
/// </summary>
public sealed class PlayTimeDatabase : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly Dictionary<string, long> _programIds = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlayTimeTracker", "playtime.db");

    public string FilePath { get; }

    /// <summary>프로그램이 새로 기록되거나 삭제될 때마다 늘어난다 (목록을 다시 읽어야 하는지 판단용).</summary>
    public int ProgramVersion { get; private set; }

    public PlayTimeDatabase(string? path = null)
    {
        FilePath = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath }.ToString());
        _conn.Open();
        bool hadExclusions = TableExists("exclusions");
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS exclusions (
                exe_name TEXT PRIMARY KEY COLLATE NOCASE
            );
            CREATE TABLE IF NOT EXISTS programs (
                id           INTEGER PRIMARY KEY,
                key          TEXT NOT NULL UNIQUE,
                exe_name     TEXT NOT NULL,
                exe_path     TEXT,
                display_name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                id            INTEGER PRIMARY KEY,
                program_id    INTEGER NOT NULL REFERENCES programs(id),
                start_time    INTEGER NOT NULL,
                end_time      INTEGER NOT NULL,
                focus_seconds INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_sessions_start ON sessions(start_time);
            """);
        AddMissingColumns();
        MergeVersionedPrograms();

        // 제외 목록은 처음 한 번만 기본값으로 채우고, 이후에는 사용자가 고친 그대로 쓴다
        if (!hadExclusions)
        {
            foreach (var name in ExclusionList.DefaultNames) AddExclusion(name);
        }
        ExclusionList.SetNames(GetExclusions());
    }

    private bool TableExists(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>감지하지 않을 exe 이름 목록.</summary>
    public List<string> GetExclusions()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT exe_name FROM exclusions ORDER BY exe_name COLLATE NOCASE";
        var result = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public void AddExclusion(string exeName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO exclusions (exe_name) VALUES ($name)";
        cmd.Parameters.AddWithValue("$name", exeName);
        cmd.ExecuteNonQuery();
    }

    public void RemoveExclusion(string exeName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM exclusions WHERE exe_name = $name";
        cmd.Parameters.AddWithValue("$name", exeName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>나중 단계에서 추가된 설정 열. 이전에 만든 DB에도 그대로 붙인다.</summary>
    private void AddMissingColumns()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM pragma_table_info('programs')";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(0));
        }
        if (!existing.Contains("custom_name")) Execute("ALTER TABLE programs ADD COLUMN custom_name TEXT");
        if (!existing.Contains("group_name")) Execute("ALTER TABLE programs ADD COLUMN group_name TEXT");
        if (!existing.Contains("is_game")) Execute("ALTER TABLE programs ADD COLUMN is_game INTEGER"); // NULL = 자동 판별
        if (!existing.Contains("alert_minutes")) Execute("ALTER TABLE programs ADD COLUMN alert_minutes INTEGER"); // NULL = 알림 없음
        if (!existing.Contains("hidden_override"))
        {
            // NULL = 기본 제외 목록을 따름. 예전 hidden 열에서 직접 숨긴 것만 옮겨 온다.
            Execute("ALTER TABLE programs ADD COLUMN hidden_override INTEGER");
            if (existing.Contains("hidden")) Execute("UPDATE programs SET hidden_override = 1 WHERE hidden = 1");
        }
    }

    /// <summary>
    /// 예전에는 경로를 그대로 키로 써서, 업데이트로 설치 폴더의 버전이 바뀐 프로그램이 여러 줄로 나뉘어 있다.
    /// 키를 <see cref="ProgramKey"/> 방식으로 바꾸고, 같은 키가 된 줄은 하나로 합친다.
    /// 남기는 줄은 가장 최근에 쓴 줄(경로와 아이콘이 최신)이고, 그 줄에 없는 사용자 설정은 다른 줄에서 가져온다.
    /// 이미 바뀐 DB에서는 아무것도 하지 않는다.
    /// </summary>
    private void MergeVersionedPrograms()
    {
        var rows = new List<(long Id, string Key, string NewKey, long LastUsed)>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT p.id, p.key, p.exe_name, p.exe_path,
                       COALESCE((SELECT MAX(end_time) FROM sessions s WHERE s.program_id = p.id), 0)
                FROM programs p
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string newKey = ProgramKey.From(NullableString(reader, 3), reader.GetString(2));
                rows.Add((reader.GetInt64(0), reader.GetString(1), newKey, reader.GetInt64(4)));
            }
        }
        if (rows.All(r => r.Key == r.NewKey)) return;

        using var tx = _conn.BeginTransaction();
        void Run(string sql, params (string Name, object Value)[] args)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            cmd.ExecuteNonQuery();
        }

        foreach (var group in rows.GroupBy(r => r.NewKey))
        {
            // 최근에 쓴 순서. 남길 줄이 맨 앞
            var ordered = group.OrderByDescending(r => r.LastUsed).ThenByDescending(r => r.Id).ToList();
            long keep = ordered[0].Id;

            foreach (var other in ordered.Skip(1))
            {
                // 남길 줄에 비어 있는 설정만 채운다 (더 최근 줄의 설정이 우선)
                foreach (var column in new[] { "custom_name", "group_name", "alert_minutes", "is_game", "hidden_override" })
                {
                    Run($"""
                        UPDATE programs SET {column} = (SELECT {column} FROM programs WHERE id = $other)
                        WHERE id = $keep AND {column} IS NULL
                        """, ("$keep", keep), ("$other", other.Id));
                }
                Run("UPDATE sessions SET program_id = $keep WHERE program_id = $other", ("$keep", keep), ("$other", other.Id));
                Run("DELETE FROM programs WHERE id = $other", ("$other", other.Id));
            }
            if (ordered[0].Key != group.Key)
                Run("UPDATE programs SET key = $key WHERE id = $keep", ("$key", group.Key), ("$keep", keep));
        }
        tx.Commit();
    }

    /// <summary>새 세션은 추가하고, 이미 저장된 세션은 종료 시각과 포커스 초를 갱신한다 (한 트랜잭션).</summary>
    public void Save(IEnumerable<Session> sessions)
    {
        using var tx = _conn.BeginTransaction();

        using var insert = _conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO sessions (program_id, start_time, end_time, focus_seconds)
            VALUES ($program, $start, $end, $focus) RETURNING id
            """;
        var iProgram = insert.Parameters.Add("$program", SqliteType.Integer);
        var iStart = insert.Parameters.Add("$start", SqliteType.Integer);
        var iEnd = insert.Parameters.Add("$end", SqliteType.Integer);
        var iFocus = insert.Parameters.Add("$focus", SqliteType.Integer);

        using var update = _conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE sessions SET end_time = $end, focus_seconds = $focus WHERE id = $id";
        var uEnd = update.Parameters.Add("$end", SqliteType.Integer);
        var uFocus = update.Parameters.Add("$focus", SqliteType.Integer);
        var uId = update.Parameters.Add("$id", SqliteType.Integer);

        foreach (var s in sessions)
        {
            long start = ToUnix(s.Start);
            long end = Math.Max(ToUnix(s.End), start);
            long focus = Math.Clamp((long)Math.Round(s.FocusSeconds), 0, end - start);

            if (s.DbId == 0)
            {
                iProgram.Value = GetProgramId(s.Program, tx);
                iStart.Value = start;
                iEnd.Value = end;
                iFocus.Value = focus;
                s.DbId = (long)insert.ExecuteScalar()!;
            }
            else
            {
                uEnd.Value = end;
                uFocus.Value = focus;
                uId.Value = s.DbId;
                update.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>[from, to) 구간과 겹치는 세션을 모두 가져온다.</summary>
    public List<StoredSession> QuerySessions(DateTime from, DateTime to)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.key, p.exe_name, p.exe_path, p.display_name, p.custom_name, p.group_name,
                   p.hidden_override, p.is_game, s.start_time, s.end_time, s.focus_seconds
            FROM sessions s JOIN programs p ON p.id = s.program_id
            WHERE s.end_time > $from AND s.start_time < $to
            """;
        cmd.Parameters.AddWithValue("$from", ToUnix(from));
        cmd.Parameters.AddWithValue("$to", ToUnix(to));

        var result = new List<StoredSession>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new StoredSession(
                reader.GetString(0), reader.GetString(1), NullableString(reader, 2), reader.GetString(3),
                NullableString(reader, 4), NullableString(reader, 5), NullableBool(reader, 6), NullableBool(reader, 7),
                FromUnix(reader.GetInt64(8)), FromUnix(reader.GetInt64(9)), reader.GetInt64(10)));
        }
        return result;
    }

    /// <summary>기록된 적 있는 모든 프로그램과 설정.</summary>
    public List<ProgramRecord> GetPrograms()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT key, exe_name, exe_path, display_name, custom_name, group_name, hidden_override, is_game, alert_minutes
            FROM programs
            """;

        var result = new List<ProgramRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ProgramRecord(
                reader.GetString(0), reader.GetString(1), NullableString(reader, 2), reader.GetString(3),
                NullableString(reader, 4), NullableString(reader, 5), NullableBool(reader, 6), NullableBool(reader, 7),
                reader.IsDBNull(8) ? null : (int)reader.GetInt64(8)));
        }
        return result;
    }

    public void SaveProgramSettings(IEnumerable<ProgramSetting> settings)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE programs
            SET custom_name = $name, group_name = $group, alert_minutes = $alert
            WHERE key = $key
            """;
        var name = cmd.Parameters.Add("$name", SqliteType.Text);
        var group = cmd.Parameters.Add("$group", SqliteType.Text);
        var alert = cmd.Parameters.Add("$alert", SqliteType.Integer);
        var key = cmd.Parameters.Add("$key", SqliteType.Text);

        foreach (var s in settings)
        {
            name.Value = string.IsNullOrWhiteSpace(s.CustomName) ? DBNull.Value : s.CustomName.Trim();
            group.Value = (object?)Grouping.NormalizeTags(s.GroupName) ?? DBNull.Value;
            alert.Value = s.AlertMinutes is > 0 ? s.AlertMinutes.Value : DBNull.Value;
            key.Value = s.Key;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>프로그램 하나의 게임 여부를 직접 정한다. null이면 자동 판별로 되돌린다.</summary>
    public void SetGameOverride(string key, bool? isGame)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE programs SET is_game = $game WHERE key = $key";
        cmd.Parameters.AddWithValue("$game", ToDbBool(isGame));
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    /// <summary>감지 제외 여부를 직접 정한다. null이면 기본 제외 목록을 따른다.</summary>
    public void SetIgnored(string key, bool? ignored)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE programs SET hidden_override = $ignored WHERE key = $key";
        cmd.Parameters.AddWithValue("$ignored", ToDbBool(ignored));
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 프로그램의 기록(세션)과 프로그램 줄 자체를 DB에서 완전히 지운다. 되돌릴 수 없다.
    /// 다시 감지되지 않게 하려면 호출하는 쪽에서 exe 이름을 제외 목록에 넣어야 한다.
    /// </summary>
    public void PurgeProgram(string key)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            DELETE FROM sessions WHERE program_id = (SELECT id FROM programs WHERE key = $key);
            DELETE FROM programs WHERE key = $key;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
        tx.Commit();
        _programIds.Remove(key);
        ProgramVersion++;
    }

    /// <summary>앱 설정 값 (화면 크기, 작은 창 사용 여부 등). 없으면 null.</summary>
    public string? GetSetting(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private static object ToDbBool(bool? value) => value is { } v ? (v ? 1 : 0) : DBNull.Value;

    private static bool? NullableBool(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetInt64(index) != 0;

    private static string? NullableString(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);

    private long GetProgramId(ProgramInfo program, SqliteTransaction tx)
    {
        if (_programIds.TryGetValue(program.Key, out long id)) return id;

        // 이미 있는 프로그램이면 exe 정보만 갱신한다 (사용자 설정 열은 건드리지 않음)
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO programs (key, exe_name, exe_path, display_name)
            VALUES ($key, $exe, $path, $name)
            ON CONFLICT(key) DO UPDATE SET
                exe_name = excluded.exe_name, exe_path = excluded.exe_path, display_name = excluded.display_name
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("$key", program.Key);
        cmd.Parameters.AddWithValue("$exe", program.ExeName);
        cmd.Parameters.AddWithValue("$path", (object?)program.ExePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", program.DisplayName);
        id = (long)cmd.ExecuteScalar()!;
        _programIds[program.Key] = id;
        ProgramVersion++;
        return id;
    }

    private void Execute(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long ToUnix(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    private static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;

    public void Dispose() => _conn.Dispose();
}
