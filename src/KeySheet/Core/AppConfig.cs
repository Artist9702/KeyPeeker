using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeySheet.Core;

/// <summary>可持久化的用户配置（config.json）。</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 2;

    /// <summary>长按修饰键弹出：类 macOS CheatSheet。</summary>
    public HoldToShowConfig HoldToShow { get; set; } = new();

    /// <summary>组合热键开关弹出。</summary>
    public ToggleHotkeyConfig ToggleHotkey { get; set; } = new();

    /// <summary>弹窗外观/行为。</summary>
    public PopupConfig Popup { get; set; } = new();

    /// <summary>DeepSeek AI 配置（用于“AI 补齐快捷键”）。</summary>
    public AiConfig Ai { get; set; } = new();

    /// <summary>启动行为。</summary>
    public StartupConfig Startup { get; set; } = new();

    public sealed class AiConfig
    {
        /// <summary>DeepSeek API Key（明文保存在本机 config.json，仅本机使用）。</summary>
        public string ApiKey { get; set; } = "";
        /// <summary>模型名，默认 deepseek-chat。</summary>
        public string Model { get; set; } = "deepseek-chat";
        /// <summary>API 地址，默认官方地址。</summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com";
    }

    public sealed class StartupConfig
    {
        /// <summary>是否开机自启（写入 HKCU 的 Run 键）。</summary>
        public bool AutoStart { get; set; } = false;
        /// <summary>是否已经弹过“首次运行询问开机自启”（只弹一次）。</summary>
        public bool AutoStartPrompted { get; set; } = false;
    }

    public sealed class HoldToShowConfig
    {
        public bool Enabled { get; set; } = true;
        /// <summary>修饰键名：Ctrl / Alt / Shift / Win（左右任意一侧），或 LeftCtrl / RightCtrl 等精确指定。</summary>
        public string Modifier { get; set; } = "Ctrl";
        /// <summary>按住多少毫秒后弹出。</summary>
        public int HoldMilliseconds { get; set; } = 550;
        /// <summary>按住期间若按了其它键则不弹出（避免误触真实快捷键）。</summary>
        public bool RequireNoOtherKey { get; set; } = true;
    }

    public sealed class ToggleHotkeyConfig
    {
        public bool Enabled { get; set; } = true;
        /// <summary>主键，如 "F1"。支持 A-Z / 0-9 / F1-F24 / 常见功能键名。</summary>
        public string Key { get; set; } = "F1";
        /// <summary>需同时按下的修饰键名列表，如 ["Ctrl","Shift"]；可为空数组表示单独按键。</summary>
        public string[] Modifiers { get; set; } = new[] { "Ctrl", "Shift" };
    }

    public sealed class PopupConfig
    {
        public double MaxWidth { get; set; } = 920;
        public double MaxHeight { get; set; } = 800;
        /// <summary>只显示带快捷键的条目；关闭后无快捷键菜单项也会灰显列出。</summary>
        public bool ShowOnlyWithShortcuts { get; set; } = true;
        public double Opacity { get; set; } = 0.98;
        /// <summary>弹窗空闲多少秒后自动关闭（0 = 不自动关闭）。</summary>
        public int AutoHideSeconds { get; set; } = 20;
        /// <summary>实时读不到时，是否允许“轻点 Alt 激活菜单再读取”（会短暂唤起目标应用的菜单）。实测对 Ribbon 类应用无效，默认关闭。</summary>
        public bool AllowInvasiveScan { get; set; } = false;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOpts);
                if (cfg is not null)
                {
                    // v1 → v2：弹窗默认尺寸加大（避免下翻看全）
                    if (cfg.Version < 2)
                    {
                        cfg.Version = 2;
                        cfg.Popup.MaxWidth = 920;
                        cfg.Popup.MaxHeight = 800;
                    }
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[config] 读取失败({ex.Message})，使用默认配置。");
        }
        return new AppConfig();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[config] 写入失败: {ex.Message}");
        }
    }
}
