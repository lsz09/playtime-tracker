using System.Windows;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>배경 테마: 윈도우 설정을 따르거나(system), 화이트(light) 또는 블랙(dark)으로 고정한다.</summary>
public static class UiTheme
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
    private const string SettingKey = "theme";

    public static string Mode { get; private set; } = System;

    public static void Load(PlayTimeDatabase db)
    {
        Mode = Normalize(db.GetSetting(SettingKey));
        Apply();
    }

    public static void Set(PlayTimeDatabase db, string mode)
    {
        Mode = Normalize(mode);
        db.SetSetting(SettingKey, Mode);
        Apply();
    }

    private static void Apply()
    {
        if (Application.Current == null) return;
        Application.Current.ThemeMode = Mode switch
        {
            Light => ThemeMode.Light,
            Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    private static string Normalize(string? mode) => mode is Light or Dark ? mode : System;
}
