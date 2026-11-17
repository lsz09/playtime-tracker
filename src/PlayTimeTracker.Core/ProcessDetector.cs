using System.Diagnostics;
using System.Runtime.InteropServices;
using static PlayTimeTracker.Core.NativeMethods;

namespace PlayTimeTracker.Core;

/// <summary>프로그램 하나(exe 단위). Key는 exe 전체 경로(없으면 exe 이름)의 소문자.</summary>
public sealed record ProgramInfo(string Key, string ExeName, string? ExePath, string DisplayName);

/// <summary>현재 창을 띄우고 있는 프로그램과, 그 프로그램에 속한 (창이 있는) 프로세스 수.</summary>
public sealed record RunningProgram(ProgramInfo Program, int ProcessCount);

public sealed record DetectionSnapshot(
    DateTime Time,
    IReadOnlyList<RunningProgram> Running,
    ProgramInfo? Active,
    bool IsIdle,
    TimeSpan IdleFor);

/// <summary>
/// 창이 있는 프로세스만 훑어서 실행 중인 프로그램, 활성 프로그램, 자리 비움 여부를 알아낸다.
/// 전체 프로세스 목록은 순회하지 않고, PID별 exe 정보는 캐시한다.
/// </summary>
public sealed class ProcessDetector
{
    private const string UwpHostExe = "ApplicationFrameHost.exe";

    private readonly TimeSpan _idleThreshold;
    private readonly Dictionary<uint, ProgramInfo> _pidCache = new();
    private readonly Dictionary<string, ProgramInfo> _programsByKey = new();
    private readonly HashSet<uint> _seenPids = new();
    private readonly Dictionary<string, int> _counts = new();
    private readonly EnumWindowsProc _enumCallback;
    private readonly EnumWindowsProc _childCallback;
    private uint _childHostPid;
    private uint _childFoundPid;

    public ProcessDetector(TimeSpan? idleThreshold = null)
    {
        _idleThreshold = idleThreshold ?? TimeSpan.FromMinutes(5);
        _enumCallback = OnWindow;
        _childCallback = OnChildWindow;
    }

    public DetectionSnapshot Scan()
    {
        _seenPids.Clear();
        _counts.Clear();
        _countedPids.Clear();
        EnumWindows(_enumCallback, IntPtr.Zero);

        // 활성 창: 대화상자 등 소유된 창이면 주인 창 기준으로 프로그램을 찾는다.
        ProgramInfo? active = null;
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            var root = GetAncestor(fg, GA_ROOTOWNER);
            var fgProgram = ResolveProgram(root != IntPtr.Zero ? root : fg, out _);
            if (fgProgram != null && _counts.ContainsKey(fgProgram.Key))
                active = fgProgram;
        }

        // 더 이상 창이 없는 PID는 캐시에서 제거 (PID 재사용 대비)
        if (_pidCache.Count > _seenPids.Count)
        {
            foreach (var pid in _pidCache.Keys.Where(p => !_seenPids.Contains(p)).ToList())
                _pidCache.Remove(pid);
        }

        var running = _counts
            .Select(kv => new RunningProgram(_programsByKey[kv.Key], kv.Value))
            .OrderBy(r => r.Program.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var idleFor = GetIdleTime();
        return new DetectionSnapshot(DateTime.Now, running, active, idleFor >= _idleThreshold, idleFor);
    }

    private bool OnWindow(IntPtr hWnd, IntPtr lParam)
    {
        if (!IsAppWindow(hWnd)) return true;

        var program = ResolveProgram(hWnd, out uint pid);
        if (program == null) return true;

        // 같은 PID가 창을 여러 개 띄워도 프로세스는 한 번만 센다
        if (_countedPids.Add(pid))
            _counts[program.Key] = _counts.GetValueOrDefault(program.Key) + 1;
        return true;
    }

    private readonly HashSet<uint> _countedPids = new();

    /// <summary>Alt+Tab에 나오는 종류의 창인지 (보이고, 도구 창이 아니고, 숨김 처리되지 않은 최상위 창).</summary>
    private static bool IsAppWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd)) return false;

        long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        bool appWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;
        if (!appWindow && (exStyle & WS_EX_NOACTIVATE) != 0) return false;
        if (!appWindow && GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return false;

        // 다른 가상 데스크톱이 아닌데 숨겨진(cloaked) 창 = 일시 중단된 스토어 앱 등
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        return true;
    }

    /// <summary>창의 주인 프로그램을 찾는다. 스토어(UWP) 앱은 호스트 창 안의 실제 앱 프로세스를 찾는다.</summary>
    private ProgramInfo? ResolveProgram(IntPtr hWnd, out uint pid)
    {
        GetWindowThreadProcessId(hWnd, out pid);
        if (pid == 0) return null;

        var program = GetProgram(pid);
        if (program != null && program.ExeName.Equals(UwpHostExe, StringComparison.OrdinalIgnoreCase))
        {
            _childHostPid = pid;
            _childFoundPid = 0;
            EnumChildWindows(hWnd, _childCallback, IntPtr.Zero);
            if (_childFoundPid == 0) return null; // 앱 본체가 아직 없거나 중단됨
            pid = _childFoundPid;
            program = GetProgram(pid);
        }
        return program;
    }

    private bool OnChildWindow(IntPtr hWnd, IntPtr lParam)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid != 0 && pid != _childHostPid)
        {
            _childFoundPid = pid;
            return false;
        }
        return true;
    }

    private ProgramInfo? GetProgram(uint pid)
    {
        _seenPids.Add(pid);
        if (_pidCache.TryGetValue(pid, out var cached)) return cached;

        string? path = QueryExePath(pid);
        string exeName;
        if (path != null)
        {
            exeName = Path.GetFileName(path);
        }
        else
        {
            // 핸들을 열 수 없는 프로세스: 이름만이라도 얻는다
            try
            {
                using var p = Process.GetProcessById((int)pid);
                exeName = p.ProcessName + ".exe";
            }
            catch
            {
                return null; // 그 사이에 종료됨
            }
        }

        string key = (path ?? exeName).ToLowerInvariant();
        if (!_programsByKey.TryGetValue(key, out var program))
        {
            program = new ProgramInfo(key, exeName, path, ReadDisplayName(path, exeName));
            _programsByKey[key] = program;
        }
        _pidCache[pid] = program;
        return program;
    }

    private static string? QueryExePath(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// 표시 이름: exe의 제품명 → 파일 설명 → exe 이름 순.
    /// 제품명이 "Microsoft® Windows® Operating System"처럼 프로그램을 구분하지 못하면 파일 설명을 쓴다.
    /// </summary>
    private static string ReadDisplayName(string? path, string exeName)
    {
        if (path != null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                string? product = CleanName(info.ProductName);
                string? description = CleanName(info.FileDescription);
                bool genericProduct = product != null &&
                    product.Contains("Operating System", StringComparison.OrdinalIgnoreCase);

                if (product != null && !genericProduct) return product;
                if (description != null) return description;
                if (product != null) return product;
            }
            catch
            {
                // 파일을 읽을 수 없으면 exe 이름으로 대체
            }
        }
        return Path.GetFileNameWithoutExtension(exeName);
    }

    /// <summary>상표 기호(®, ™, (TM), (R))를 떼고 공백을 정리한다.</summary>
    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var mark in new[] { "®", "™", "©", "(TM)", "(tm)", "(R)", "(r)" })
            name = name.Replace(mark, " ");
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return name.Length > 0 ? name : null;
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
