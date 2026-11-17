namespace PlayTimeTracker.Core;

/// <summary>
/// exe의 설치 경로와 파일 이름만 보고 게임인지 추정한다 (프로세스는 건드리지 않음).
/// 틀릴 수 있으므로 사용자가 프로그램 관리에서 고친 값이 항상 우선한다.
/// </summary>
public static class GameDetector
{
    // 게임 플랫폼이 게임을 설치하는 폴더 (소문자, 경로 중간에 포함되는지로 판단)
    private static readonly string[] GameFolders =
    {
        @"\steamapps\common\",
        @"\epic games\",
        @"\riot games\",
        @"\hoyoplay\games\",
        @"\mihoyo\",
        @"\_retail_\",                     // 블리자드 게임 (오버워치 등)
        @"\origin games\",
        @"\ea games\",
        @"\ubisoft game launcher\games\",
        @"\gog galaxy\games\",
        @"\gog games\",
        @"\xboxgames\",
        @"\games\",
    };

    // 게임 폴더 안에 있어도 게임이 아닌 것 (런처)
    private static readonly string[] LauncherFolders =
    {
        @"\epic games\launcher\",
        @"\riot games\riot client\",
        @"\battle.net\",
    };

    private static readonly HashSet<string> LauncherExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "EpicGamesLauncher.exe",
        "Riot Client.exe", "RiotClientServices.exe", "RiotClientUx.exe",
        "Battle.net.exe", "HYP.exe", "launcher.exe",
        "GalaxyClient.exe", "upc.exe", "UbisoftConnect.exe", "EADesktop.exe", "Origin.exe",
    };

    // 폴더로는 알 수 없는 곳에 설치되기도 하는 게임
    private static readonly HashSet<string> GameExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "League of Legends.exe", "Overwatch.exe", "StarRail.exe", "GenshinImpact.exe",
        "ZenlessZoneZero.exe", "CivilizationVI.exe", "CivilizationVI_DX12.exe", "VALORANT.exe",
    };

    private static readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsGame(string? exePath, string exeName)
    {
        string cacheKey = exePath ?? exeName;
        lock (_cache)
        {
            if (_cache.TryGetValue(cacheKey, out bool cached)) return cached;
            bool result = Detect(exePath, exeName);
            _cache[cacheKey] = result;
            return result;
        }
    }

    /// <summary>알려진 런처 exe 이름 (기본 제외 목록에 들어간다).</summary>
    public static IEnumerable<string> LauncherExeNames => LauncherExes;

    /// <summary>런처가 설치되는 폴더 안에 있는지.</summary>
    public static bool IsLauncherFolder(string? exePath)
    {
        if (exePath == null) return false;
        string path = exePath.ToLowerInvariant();
        return LauncherFolders.Any(path.Contains);
    }

    /// <summary>Steam, Riot Client 같은 게임 런처인지.</summary>
    public static bool IsLauncher(string? exePath, string exeName) =>
        LauncherExes.Contains(exeName) || IsLauncherFolder(exePath);

    private static bool Detect(string? exePath, string exeName)
    {
        string path = (exePath ?? "").ToLowerInvariant();

        if (IsLauncher(exePath, exeName)) return false;

        if (GameExes.Contains(exeName)) return true;
        if (exeName.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)) return true; // 언리얼 엔진 게임
        return GameFolders.Any(path.Contains);
    }
}
