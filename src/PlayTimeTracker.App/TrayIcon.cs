using Forms = System.Windows.Forms;

namespace PlayTimeTracker.App;

/// <summary>트레이 아이콘: 더블클릭으로 메인 창, 우클릭 메뉴로 열기/통계/프로그램 관리/게임 관리/종료.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action showMain, Action showStats, Action showManage, Action showGames, Action showSettings, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => showMain());
        menu.Items.Add("통계", null, (_, _) => showStats());
        menu.Items.Add("프로그램 관리", null, (_, _) => showManage());
        menu.Items.Add("게임 관리", null, (_, _) => showGames());
        menu.Items.Add("설정", null, (_, _) => showSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = AppIcon.Tray,
            Text = "PlayTimeTracker - 기록 중",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => showMain();
    }

    /// <summary>툴팁에 플레이 중인 게임을 보여 준다 (툴팁은 127자 제한).</summary>
    public void SetCurrentGame(string? gameName)
    {
        string text = gameName != null ? $"PlayTimeTracker - 플레이 중: {gameName}" : "PlayTimeTracker - 기록 중";
        _icon.Text = text.Length > 120 ? text[..120] : text;
    }

    public void ShowAlert(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);

    public void ShowHint(string message) =>
        _icon.ShowBalloonTip(3000, "PlayTimeTracker", message, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
