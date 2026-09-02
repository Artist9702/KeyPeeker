using System.Drawing;
using System.Windows.Forms;

namespace KeySheet.UI;

/// <summary>托盘图标：右键菜单只保留 设置 / 退出。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly string _appName;

    public TrayIcon(string appName, Action openSettings, Action exit)
    {
        _appName = appName;
        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = appName,
            Visible = true
        };

        var menu = new ContextMenuStrip();
        var settings = new ToolStripMenuItem("设置…");
        settings.Click += (_, _) => openSettings();
        var quit = new ToolStripMenuItem("退出");
        quit.Click += (_, _) => exit();

        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quit);

        _icon.ContextMenuStrip = menu;
    }

    /// <summary>优先用应用自带图标，读取失败退回系统默认。</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return new Icon(stream);
            }
        }
        catch { /* 回退系统图标 */ }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void ShowStartupTip(string tipText)
    {
        try
        {
            _icon.BalloonTipTitle = _appName;
            _icon.BalloonTipText = tipText;
            _icon.ShowBalloonTip(4000);
        }
        catch { /* 通知被系统禁用等场景忽略 */ }
    }

    public void UpdateHoverText(string text)
    {
        if (text.Length > 63) text = text[..63];
        _icon.Text = text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
