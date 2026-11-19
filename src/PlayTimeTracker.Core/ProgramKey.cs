using System.Text.RegularExpressions;

namespace PlayTimeTracker.Core;

/// <summary>
/// 프로그램을 구분하는 키. 실행 파일의 전체 경로(소문자)를 쓰되, 폴더 이름 속 버전 번호는 *로 바꾼다.
/// 업데이트할 때마다 설치 폴더가 바뀌는 프로그램도 같은 프로그램으로 보기 위해서다.
///   ...\windowsapps\claude_5.11492.5.0_x64__pzs8sxrjxfjjc\app\claude.exe → ...\claude_*_x64__pzs8sxrjxfjjc\app\claude.exe
///   ...\slack\app-4.41.105\slack.exe → ...\slack\app-*\slack.exe
/// 점이 들어간 숫자(1.2, 4.41.105 등)만 버전으로 보며, 파일 이름은 그대로 둔다.
/// </summary>
public static partial class ProgramKey
{
    [GeneratedRegex(@"(?<![a-z0-9])v?\d+(?:\.\d+)+(?![a-z0-9])")]
    private static partial Regex VersionPattern();

    public static string From(string? path, string exeName)
    {
        if (string.IsNullOrEmpty(path)) return exeName.ToLowerInvariant();

        string lower = path.ToLowerInvariant();
        int slash = lower.LastIndexOf('\\');
        if (slash < 0) return lower;

        string folder = VersionPattern().Replace(lower[..slash], "*");
        return folder + lower[slash..];
    }
}
