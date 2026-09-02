using System.Windows;
using System.Windows.Threading;
using KeySheet.Core;
using KeySheet.UI;

namespace KeySheet;

public partial class App : Application
{
    public const string AppName = "KeyPeeker";
    private readonly Mutex _singleInstance = new(true, @"Local\KeySheet_SingleInstance");

    private AppConfig _config = new();
    private PresetStore _presets = null!;
    private OverrideStore _overrides = null!;
    private ShortcutAggregator _aggregator = null!;
    private KeyboardHook _hook = null!;
    private OverlayWindow _overlay = null!;
    private TrayIcon? _tray;

    private DispatcherTimer _holdTimer = null!;
    private DispatcherTimer? _autoHideTimer;

    // 长按触发状态
    private (int vk1, int vk2)? _holdVks;
    private char _holdFamily;
    private DateTime? _holdSince;
    private bool _otherDuringHold;
    private bool _modDownPrev;

    // 组合热键
    private int _toggleVk = -1;
    private int[] _toggleMods = Array.Empty<int>();

    private ForegroundInfo? _lastInfo;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            Log($"启动异常: {ex}");
            try
            {
                MessageBox.Show($"启动失败：{ex.Message}\n\n详细见日志。", "KeyPeeker 错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* 消息框也失败则直接退出 */ }
            Shutdown(1);
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        Log($"OnStartup 进入，args=[{string.Join(" | ", e.Args ?? Array.Empty<string>())}]");
        var args = e.Args ?? Array.Empty<string>();
        string? dataOverride = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--datadir" && i + 1 < args.Length) dataOverride = args[i + 1];
        }
        Log($"datadir 参数: {(dataOverride ?? "(无)")}  开始初始化 AppPaths");
        AppPaths.Initialize(dataOverride);
        Log($"数据目录: {AppPaths.DataRoot}");

        if (args.Contains("--diag") || args.Contains("--apps"))
        {
            int code = DiagRunner.Run(args, AppPaths.PresetsDir);
            Shutdown(code);
            return;
        }

        if (!_singleInstance.WaitOne(0, false))
        {
            Log("已有实例在运行，退出。");
            MessageBox.Show($"{AppName} 已经在运行了。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        _config = AppConfig.Load(AppPaths.ConfigFile);
        if (!File.Exists(AppPaths.ConfigFile))
        {
            _config.Save(AppPaths.ConfigFile);
            Log($"已生成默认配置: {AppPaths.ConfigFile}");
        }

        _presets = new PresetStore(AppPaths.PresetsDir);
        _presets.SeedBuiltins();
        _overrides = new OverrideStore(AppPaths.PresetsDir);
        _aggregator = new ShortcutAggregator(_presets, _config.Popup.AllowInvasiveScan, _overrides);
        _overlay = new OverlayWindow();

        ResolveTriggers();

        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
        if (!_hook.Install())
        {
            Log("全局键盘钩子安装失败。");
            MessageBox.Show("无法安装全局键盘钩子（可能被其它程序占用），程序将退出。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Log("键盘钩子已安装。");

        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _holdTimer.Tick += (_, _) => TickHoldCheck();
        if (_config.HoldToShow.Enabled) _holdTimer.Start();

        _tray = new TrayIcon(AppName, OpenSettings, ExitApp);
        _tray.UpdateHoverText($"{AppName}\n长按 Ctrl 或按 {ToggleDisplay()} 查看当前应用快捷键");
        _tray.ShowStartupTip($"已启动：在任意应用里按住 Ctrl 约 0.6 秒，或按 {ToggleDisplay()}，即可查看该应用的快捷键。");
        Log("托盘就绪，进入运行。");
        EnsureAutoStartConsistent(); // 文件夹移动后自动修正/清理自启入口
        TryFirstRunAutoStartPrompt();
    }

    /// <summary>自启自愈：若配置要求自启但入口丢失/路径已变（文件夹被移动），自动改写为当前 exe 路径；否则清理失效入口。</summary>
    private void EnsureAutoStartConsistent()
    {
        try
        {
            bool entry = AutoStart.HasEntry();
            if (_config.Startup.AutoStart)
            {
                if (!entry || !AutoStart.IsEnabled())
                {
                    bool ok = AutoStart.SetEnabled(true);
                    Log(ok ? "自启自愈：入口已更新到当前 exe 位置" : "自启自愈失败（系统拒绝写入）");
                }
            }
            else
            {
                if (entry)
                {
                    AutoStart.SetEnabled(false);
                    Log("自启自愈：清理了失效/多余的启动入口（可在 设置③ 重新开启）");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"自启自愈异常: {ex.Message}");
        }
    }

    /// <summary>首次运行弹一次“是否开机自启”（之后可在设置 ③ 修改）。</summary>
    private void TryFirstRunAutoStartPrompt()
    {
        try
        {
            if (_config.Startup.AutoStartPrompted || AutoStart.IsEnabled()) return;
            _config.Startup.AutoStartPrompted = true; // 先落标记：无论选什么都只问一次
            var r = MessageBox.Show(
                "是否让 KeyPeeker 开机自动启动？\n\n选择“是”后，每次开机都会自动在后台运行，无需再双击。\n（之后随时可在 托盘→设置→③ 开机自启 中修改）",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            bool want = r == MessageBoxResult.Yes;
            bool ok = AutoStart.SetEnabled(want);
            _config.Startup.AutoStart = want;
            _config.Save(AppPaths.ConfigFile);
            Log($"首次运行自启询问: 选择={(want ? "是" : "否")} 设置={(ok ? "成功" : "失败")}");
        }
        catch (Exception ex)
        {
            Log($"首次运行自启询问异常: {ex.Message}");
        }
    }

    /// <summary>把启动关键步骤写入日志：优先 {数据目录}\startup.log，数据目录就绪前写 %TEMP%\keysheet-startup.log。</summary>
    internal static void Log(string msg)
    {
        try
        {
            string dest = string.IsNullOrEmpty(AppPaths.DataRoot)
                ? Path.Combine(Path.GetTempPath(), "keysheet-startup.log")
                : Path.Combine(AppPaths.DataRoot, "startup.log");
            File.AppendAllText(dest, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
        }
        catch { /* 日志失败忽略 */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _holdTimer?.Stop();
        _autoHideTimer?.Stop();
        _hook?.Dispose();
        _tray?.Dispose();
        _singleInstance.ReleaseMutex();
        base.OnExit(e);
    }

    // ---------------- 触发配置解析 ----------------

    private void ResolveTriggers()
    {
        var h = _config.HoldToShow;
        var res = h.Enabled ? KeyNames.ResolveModifier(h.Modifier) : null;
        _holdVks = res;
        _holdFamily = res.HasValue ? KeyNames.Family(res.Value.vk1) : '\0';

        var t = _config.ToggleHotkey;
        _toggleVk = -1;
        if (t.Enabled)
        {
            int? main = KeyNames.ResolveKey(t.Key);
            if (main.HasValue)
            {
                var mods = new List<int>();
                bool allOk = true;
                foreach (var m in t.Modifiers)
                {
                    var r = KeyNames.ResolveModifier(m);
                    if (r.HasValue) mods.Add(r.Value.vk1);
                    else { allOk = false; break; }
                }
                if (allOk && (t.Modifiers.Length == 0 || mods.Count == t.Modifiers.Length))
                {
                    _toggleVk = main.Value;
                    _toggleMods = mods.ToArray();
                }
            }
        }
    }

    private string ToggleDisplay()
    {
        if (_toggleVk <= 0) return "(热键未配置)";
        string name = VkToName(_toggleVk);
        var head = string.Concat(_toggleMods.Select(m => ModName(m) + "+"));
        return head + name;
    }

    private static string ModName(int vk) => KeyNames.Family(vk) switch
    {
        'C' => "Ctrl", 'A' => "Alt", 'S' => "Shift", 'W' => "Win", _ => vk.ToString()
    };

    private static string VkToName(int vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x6F}";
        return vk switch
        {
            KeyNames.VK_ESCAPE => "Esc",
            KeyNames.VK_RETURN => "Enter",
            KeyNames.VK_SPACE => "Space",
            KeyNames.VK_TAB => "Tab",
            KeyNames.VK_BACK => "Backspace",
            KeyNames.VK_DELETE => "Del",
            KeyNames.VK_INSERT => "Ins",
            KeyNames.VK_HOME => "Home",
            KeyNames.VK_END => "End",
            _ => vk.ToString()
        };
    }

    // ---------------- 钩子事件 ----------------

    private void OnKeyDown(int vk)
    {
        if (vk == KeyNames.VK_ESCAPE)
        {
            if (_overlay.IsVisible) HideOverlay();
            return;
        }

        // 组合热键：修饰键按“实时物理状态”判断（不依赖可能丢事件的内部状态表）
        if (_toggleVk > 0 && vk == _toggleVk && _toggleMods.All(PhysDown))
        {
            ToggleOverlay();
            return;
        }

        bool modHeld = _holdVks.HasValue && (PhysDown(_holdVks.Value.vk1) || PhysDown(_holdVks.Value.vk2));
        // 长按模式下：按住修饰键期间按了其它键 → 本次不弹（防止误触真实快捷键）
        if (modHeld && _config.HoldToShow.RequireNoOtherKey && KeyNames.Family(vk) != _holdFamily)
            _otherDuringHold = true;

        // 长按弹窗已显示时，用户开始按真实快捷键（非修饰键、非 Esc）→ 收起面板
        if (_overlay.IsVisible && _overlay.TriggeredByHold && !KeyNames.IsModifier(vk) && vk != _toggleVk)
            HideOverlay();
    }

    private void OnKeyUp(int vk)
    {
        // 松开修饰键即关闭：这里快速响应；Tick 的下降沿也会兜底
        if (_overlay.IsVisible && _overlay.TriggeredByHold && KeyNames.Family(vk) == _holdFamily)
            HideOverlay();
    }

    /// <summary>用 GetAsyncKeyState 读真实物理键状态（高位为 1 = 按下），不受丢事件影响。</summary>
    private static bool PhysDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void TickHoldCheck()
    {
        if (!_config.HoldToShow.Enabled || !_holdVks.HasValue) return;
        bool down = PhysDown(_holdVks.Value.vk1) || PhysDown(_holdVks.Value.vk2);

        if (!down)
        {
            if (_modDownPrev)
            {
                _modDownPrev = false;
                _holdSince = null;
                if (_overlay.IsVisible && _overlay.TriggeredByHold) HideOverlay();
            }
            return;
        }

        if (!_modDownPrev)
        {
            // 上升沿：开始计时
            _modDownPrev = true;
            _holdSince = DateTime.Now;
            _otherDuringHold = false;
            return;
        }

        if (_overlay.IsVisible || _holdSince is null) return;
        if (_config.HoldToShow.RequireNoOtherKey && _otherDuringHold) return;

        if (DateTime.Now - _holdSince.Value >= TimeSpan.FromMilliseconds(_config.HoldToShow.HoldMilliseconds))
            ShowOverlay(triggeredByHold: true);
    }

    // ---------------- 显示/隐藏 ----------------

    private void ToggleOverlay()
    {
        if (_overlay.IsVisible) HideOverlay();
        else ShowOverlay(triggeredByHold: false);
    }

    private void ShowOverlay(bool triggeredByHold)
    {
        var info = ShortcutAggregator.GetForeground();
        if (info is null || info.Pid == Environment.ProcessId) return;

        _lastInfo = info;
        var set = _aggregator.Collect(info);
        _overlay.ShowFor(set, _config, triggeredByHold);
        Log($"弹窗显示: app={set.ProcessName} 来源={set.Source} 快捷键数={set.TotalWithKeys} 标题='{set.AppDisplayName}' hold={triggeredByHold}");

        // 空闲自动关闭（仅开关模式；长按模式靠松开修饰键关闭）
        if (!triggeredByHold && _config.Popup.AutoHideSeconds > 0)
            RestartAutoHide(TimeSpan.FromSeconds(_config.Popup.AutoHideSeconds));
    }

    private void HideOverlay()
    {
        _autoHideTimer?.Stop();
        _overlay.Hide();
        Log("弹窗关闭");
    }

    private void RestartAutoHide(TimeSpan delay)
    {
        _autoHideTimer?.Stop();
        _autoHideTimer = new DispatcherTimer { Interval = delay };
        _autoHideTimer.Tick += (_, _) => HideOverlay();
        _autoHideTimer.Start();
    }

    // ---------------- 设置 / AI ----------------

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_config, SaveAndApplyConfig,
            GetForegroundApp, AiGenerateCoreAsync, _presets, _overrides);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>设置窗口“取当前前台应用”用。</summary>
    private static (string ProcessName, string WindowTitle)? GetForegroundApp()
    {
        var info = ShortcutAggregator.GetForeground();
        if (info is null || info.Pid == Environment.ProcessId) return null;
        return (info.ProcessName, info.WindowTitle);
    }

    /// <summary>设置窗口“AI 补齐”用：生成 + 校验安装，返回成败与说明（不弹框，状态在设置里显示）。
    /// showOverlay=true 时（单条补齐）成功后把快捷键浮层弹给用户看；批量补齐传 false 只静默入库。</summary>
    private async Task<(bool Ok, string Message)> AiGenerateCoreAsync(
        string processName, string windowTitle, IProgress<string>? progress, bool showOverlay)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.Ai.ApiKey))
                return (false, "未配置 DeepSeek API Key：请在上方 ① 里填写并点“保存”。");

            progress?.Report("正在连接 DeepSeek…");
            // 仅支持 DeepSeek 官方 API：模型与地址固定
            var result = await DeepSeekClient.GeneratePresetAsync(
                _config.Ai.ApiKey, DeepSeekClient.DefaultBaseUrl, "deepseek-chat",
                processName, windowTitle, "zh-CN", s => progress?.Report(s));

            if (!result.Ok)
                return (false, result.Error);

            progress?.Report("正在校验并写入预设…");
            var (ok, message, set) = _presets.InstallGenerated(processName, result.Content);

            if (ok && set is not null)
            {
                if (showOverlay)
                {
                    // 单条补齐：把结果立刻弹给用户看
                    _overlay.ShowFor(set, _config, triggeredByHold: false);
                    RestartAutoHide(TimeSpan.FromSeconds(_config.Popup.AutoHideSeconds > 0
                        ? _config.Popup.AutoHideSeconds : 30));
                }
                Log($"AI 补齐成功: app={processName} 快捷键数={set.TotalWithKeys} showOverlay={showOverlay}");
                return (true, $"添加成功：{message}（已安装，可在该软件里直接触发查看）");
            }
            return (false, $"添加失败：{message}");
        }
        catch (Exception ex)
        {
            Log($"AI 补齐异常: {ex}");
            return (false, $"发生错误：{ex.Message}");
        }
    }

    /// <summary>设置保存后：写盘 + 立即应用触发配置。</summary>
    private void SaveAndApplyConfig(AppConfig cfg)
    {
        _config = cfg;
        _config.Save(AppPaths.ConfigFile);
        ApplyRuntimeConfig();
    }

    private void ApplyRuntimeConfig()
    {
        ResolveTriggers();
        _holdTimer.Stop();
        if (_config.HoldToShow.Enabled) _holdTimer.Start();
        _tray?.UpdateHoverText($"{AppName}\n长按 Ctrl 或按 {ToggleDisplay()} 查看当前应用快捷键");
        Log("设置已保存并应用。");
    }

    private void ExitApp()
    {
        Shutdown(0);
    }
}
