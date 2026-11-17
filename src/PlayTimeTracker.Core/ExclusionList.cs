namespace PlayTimeTracker.Core;

/// <summary>
/// 감지하지 않는 프로그램. 게임과 작업 프로그램만 남기기 위한 것이다.
/// - exe 이름 목록: DB에 저장되고 앱의 "제외 목록" 창에서 고칠 수 있다. 처음에는 DefaultNames로 채워진다.
/// - 항상 제외: 이 앱 자신, 윈도우 폴더 안의 프로그램(작업 관리자, 탐색기 등), 게임 런처 폴더.
/// exe 이름과 설치 경로만 본다. 제외된 프로그램은 세션을 만들지 않으므로 기록이 남지 않는다.
/// </summary>
public static class ExclusionList
{
    /// <summary>처음 DB를 만들 때 넣는 기본 제외 목록.</summary>
    public static IReadOnlyList<string> DefaultNames { get; } = new[]
    {
        // 윈도우 구성 요소 (윈도우 폴더 밖에 있는 것)
        "ApplicationFrameHost.exe", "SystemSettings.exe", "TextInputHost.exe",
        "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe", "LockApp.exe",

        // 메신저
        "KakaoTalk.exe", "LINE.exe", "Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe",
        "Telegram.exe", "Slack.exe", "WhatsApp.exe", "Signal.exe", "Skype.exe",
        "ms-teams.exe", "Teams.exe", "Zoom.exe", "WeChat.exe", "Weixin.exe", "QQ.exe",
        "Viber.exe", "Messenger.exe", "NateOnMain.exe", "KakaoWork.exe", "JANDI.exe",
        "Mattermost.exe", "Element.exe",

        // 오피스
        "EXCEL.EXE", "WINWORD.EXE", "POWERPNT.EXE", "OUTLOOK.EXE", "olk.exe",
        "ONENOTE.EXE", "MSACCESS.EXE", "MSPUB.EXE", "VISIO.EXE",
        "Hwp.exe", "Hword.exe", "HCell.exe", "HShow.exe",
        "soffice.bin", "soffice.exe", "swriter.exe", "scalc.exe", "simpress.exe",
    }.Concat(GameDetector.LauncherExeNames).ToList();

    private static readonly HashSet<string> AlwaysExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlayTimeTracker.exe", "PlayTimeTracker.ConsoleApp.exe",
    };

    private static readonly string WindowsFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

    private static volatile HashSet<string> _names = new(DefaultNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>현재 제외 목록을 바꾼다 (DB를 열 때와 목록을 고칠 때 불린다).</summary>
    public static void SetNames(IEnumerable<string> names) =>
        _names = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    public static bool IsExcluded(string? exePath, string exeName)
    {
        if (AlwaysExcluded.Contains(exeName) || _names.Contains(exeName)) return true;
        if (GameDetector.IsLauncherFolder(exePath)) return true;

        // 작업 관리자, 탐색기, 설정 등 윈도우 폴더 안의 프로그램
        return exePath != null && WindowsFolder.Length > 1 &&
               exePath.StartsWith(WindowsFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>입력한 이름을 exe 파일 이름으로 정리한다. 확장자가 없으면 .exe를 붙인다.</summary>
    public static string? NormalizeName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string name = Path.GetFileName(input.Trim().Trim('"'));
        if (name.Length == 0) return null;
        return Path.HasExtension(name) ? name : name + ".exe";
    }
}

public static class SnapshotFilter
{
    /// <summary>감지 결과에서 제외 대상 프로그램을 뺀다. 이후 단계(기록, 화면)는 이 프로그램들을 아예 보지 못한다.</summary>
    public static DetectionSnapshot Apply(DetectionSnapshot snapshot, Func<ProgramInfo, bool> isIgnored)
    {
        var running = snapshot.Running.Where(r => !isIgnored(r.Program)).ToList();
        if (running.Count == snapshot.Running.Count) return snapshot;

        var active = snapshot.Active != null && isIgnored(snapshot.Active) ? null : snapshot.Active;
        return snapshot with { Running = running, Active = active };
    }
}
