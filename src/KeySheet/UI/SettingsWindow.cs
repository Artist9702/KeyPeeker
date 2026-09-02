using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KeySheet.Core;

namespace KeySheet.UI;

/// <summary>AI 生成执行器：返回是否成功与结果说明（由 App 实现，不弹框）。showOverlay=false 用于批量（不弹快捷键浮层）。</summary>
public delegate Task<(bool Ok, string Message)> AiGenerator(
    string processName, string windowTitle, IProgress<string>? progress, bool showOverlay);

/// <summary>取当前前台应用（进程名, 窗口标题），由 App 提供；自身窗口返回 null。</summary>
public delegate (string ProcessName, string WindowTitle)? ForegroundProvider();

/// <summary>设置窗口：左侧边栏一级菜单 + 右侧内容页。</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onSaved;
    private readonly ForegroundProvider _getForeground;
    private readonly AiGenerator _aiGenerate;
    private readonly PresetStore _presets;
    private readonly OverrideStore _overrides;
    private readonly Func<bool> _overlayVisible;
    private readonly Action _closeOverlay;

    // ---------- 侧边栏页 ----------
    private ListBox _nav;
    private List<UIElement> _pages;

    // DeepSeek API 页
    private PasswordBox _apiKey;

    // AI 补齐页
    private TextBox _aiAppName;
    private TextBlock _aiProgress;
    private Button _aiRun;
    private bool _aiRunning;
    private string _aiTitle = "";
    private bool _picking;
    private DispatcherTimer? _pickTimer;

    // 触发按键页
    private CheckBox _chkHold;
    private ComboBox _holdModifier;
    private TextBox _holdMs;
    private CheckBox _chkToggle;
    private CheckBox _chkTCtrl;
    private CheckBox _chkTAlt;
    private CheckBox _chkTShift;
    private CheckBox _chkTWin;
    private ComboBox _toggleKey;

    // 启动页
    private CheckBox _chkAutoStart;

    // 已添加软件页
    // 已装软件一键补齐页
    private List<InstalledApp> _installedApps = new();
    private readonly HashSet<string> _checkedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (byte State, string Extra)> _appResults = new(StringComparer.OrdinalIgnoreCase);
    private StackPanel _appRowsHost;
    private TextBox _appFilter;
    private TextBlock _appCount;
    private Button _appRun;
    private bool _batchRunning;
    private string _scanError = "";

    private ListBox _presetList;
    private Button _presetDelete;
    private TextBlock _presetHeader;
    private StackPanel _presetRows;

    private TextBlock _status;

    private static readonly string[] ModifierNames =
        { "Ctrl", "Alt", "Shift", "Win", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "LeftShift", "RightShift" };

    private static readonly string[] MainKeys =
        Enumerable.Range(1, 12).Select(i => "F" + i)
        .Concat(Enumerable.Range((int)'A', 26).Select(i => ((char)i).ToString()))
        .ToArray();

    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA0));
    private static readonly Brush TertGray = new SolidColorBrush(Color.FromRgb(0xAE, 0xAE, 0xB2));

    public SettingsWindow(AppConfig config, Action<AppConfig> onSaved,
                          ForegroundProvider getForeground, AiGenerator aiGenerate,
                          PresetStore presets, OverrideStore overrides,
                          Func<bool> overlayVisible, Action closeOverlay)
    {
        _config = config;
        _onSaved = onSaved;
        _getForeground = getForeground;
        _aiGenerate = aiGenerate;
        _presets = presets;
        _overrides = overrides;
        _overlayVisible = overlayVisible;
        _closeOverlay = closeOverlay;

        Title = "KeyPeeker 设置";
        Width = 960;
        Height = 800;
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        PreviewKeyDown += WindowPreviewKeyDown;

        // ---------- 各页内容（①②⑥ 合并为一页） ----------
        BuildMergedAiPage(out var pageAi);
        BuildTriggerPage(out var pageTrigger);
        BuildStartupPage(out var pageStartup);
        BuildPresetsPage(out var pagePresets);
        _pages = new List<UIElement> { pageAi, pageTrigger, pageStartup, pagePresets };

        // ---------- 侧边栏 ----------
        _nav = new ListBox
        {
            Width = 212,
            Margin = new Thickness(10, 12, 6, 12),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0)
        };
        string[] items = { "① DeepSeek AI 补齐", "② 触发按键", "③ 开机自启", "④ 已添加的软件" };
        for (int i = 0; i < items.Length; i++)
        {
            var item = new ListBoxItem { Content = items[i], Tag = i, FontSize = 13.5, Padding = new Thickness(14, 9, 10, 9) };
            _nav.Items.Add(item);
        }
        _nav.SelectionChanged += (_, _) => ShowPage(_nav.SelectedIndex);
        _nav.ItemContainerStyle = (Style)Application.Current.TryFindResource("RoundedListItem")!;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var divider = new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)), Margin = new Thickness(0, 8, 0, 8) };
        Grid.SetColumn(_nav, 0); grid.Children.Add(_nav);
        Grid.SetColumn(divider, 1); grid.Children.Add(divider);

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var pageHost = new Grid { Margin = new Thickness(14, 10, 14, 4) };
        foreach (var p in _pages)
        {
            p.Visibility = Visibility.Collapsed;
            pageHost.Children.Add(p);
        }
        Grid.SetRow(pageHost, 0); host.Children.Add(pageHost);

        // 底部按钮栏
        var bar = new DockPanel { Margin = new Thickness(14, 4, 14, 12) };
        var left = new TextBlock
        {
            Text = "保存后立即生效 · Key 仅存本机 config.json",
            Foreground = Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(left, Dock.Left); bar.Children.Add(left);
        var btnSave = new Button { Content = "保存", Width = 90, Height = 30, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        btnSave.Style = (Style)Application.Current.TryFindResource("PrimaryButton")!;
        btnSave.Click += (_, _) => Save();
        var btnClose = new Button { Content = "关闭", Width = 90, Height = 30, IsCancel = false }; // 只能手动关闭，Esc 不关设置
        btnClose.Click += (_, _) => Close();
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(btnSave);
        right.Children.Add(btnClose);
        DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right);
        _status = new TextBlock
        {
            Foreground = Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 300
        };
        bar.Children.Add(_status);
        Grid.SetRow(bar, 1);
        host.Children.Add(bar);

        Grid.SetColumn(host, 2);
        grid.Children.Add(host);
        Content = grid;

        _nav.SelectedIndex = 0;
        RefreshPresetList();
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        if (index == 3) RefreshPresetList();
        if (index == 0 && !_batchRunning) ScanInstalledNow(); // 每次进 AI 页都自动体检已装软件（批量中不打断）
    }

    // ================= ① API 页 =================
    private void BuildApiPage(out UIElement page, out PasswordBox key)
    {
        var sp = PageHost();
        var kw = new PasswordBox { Margin = FieldMargin(), PasswordChar = '●' };
        kw.Password = _config.Ai.ApiKey;
        sp.Children.Add(Field("API Key", kw));
        sp.Children.Add(Note("仅支持 DeepSeek 官方 API。Key 形如 sk-…，明文保存在本机 config.json（仅本机使用）。" +
                             "\n提示：填好 Key 并点底部“保存”，再到 ② AI 补齐 使用。"));
        key = kw; page = sp;
    }

    // ================= ① 合并页：API Key + 单个补齐 + 已装软件批量 =================
    private void BuildMergedAiPage(out UIElement page)
    {
        var root = new Grid { Margin = new Thickness(2, 4, 2, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ---------- 上半：API Key + 单个软件补齐 ----------
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        _apiKey = new PasswordBox { Margin = FieldMargin(), PasswordChar = '●' };
        _apiKey.Password = _config.Ai.ApiKey;
        top.Children.Add(Field("DeepSeek API Key", _apiKey));
        top.Children.Add(Note("仅支持 DeepSeek 官方 API（Key 形如 sk-…，明文存本机 config.json）。填好后点底部“保存”再使用。"));

        var singleHead = new TextBlock
        {
            Text = "单个软件补齐（扫不到时）",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F))
        };
        top.Children.Add(singleHead);

        _aiAppName = new TextBox { Margin = FieldMargin(), ToolTip = "进程名，如 chrome（不带 .exe）" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        _aiAppName.Width = 220; row.Children.Add(_aiAppName);
        var pick = new Button { Content = "取当前前台应用", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
        pick.Click += (_, _) => StartPickForeground();
        row.Children.Add(pick);
        top.Children.Add(Field("软件（进程名）", row));

        _aiRun = new Button
        {
            Content = "让 DeepSeek 生成并安装该软件的快捷键",
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 4)
        };
        _aiRun.Style = (Style)Application.Current.TryFindResource("PrimaryButton")!;
        _aiRun.Click += async (_, _) => await RunAiAsync();
        top.Children.Add(_aiRun);

        _aiProgress = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 2), Foreground = Gray };
        top.Children.Add(_aiProgress);

        var hr = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xEA)), Margin = new Thickness(0, 6, 0, 8) };
        top.Children.Add(hr);
        Grid.SetRow(top, 0); root.Children.Add(top);

        // ---------- 下半：已装软件批量补齐 ----------
        var inst = new Grid();
        inst.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inst.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inst.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var instHead = new TextBlock
        {
            Text = "已装软件一键补齐（进入本页自动体检）",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F))
        };
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        _appFilter = new TextBox { Width = 220, ToolTip = "按名称筛选" };
        _appFilter.TextChanged += (_, _) => RenderInstalledList();
        filterRow.Children.Add(_appFilter);
        filterRow.Children.Add(new TextBlock { Text = "  筛选（如 微信 / chrome）", VerticalAlignment = VerticalAlignment.Center, Foreground = Gray });

        var btnRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        _appRun = new Button { Content = "AI 补齐选中（0）", Style = (Style)Application.Current.TryFindResource("PrimaryButton")!, Margin = new Thickness(0, 0, 8, 4) };
        _appRun.Click += async (_, _) => await RunBatchAiAsync();
        var rescan = new Button { Content = "重新扫描", Margin = new Thickness(0, 0, 8, 4) };
        rescan.Click += (_, _) => ScanInstalledNow();
        var all = new Button { Content = "全选", Margin = new Thickness(0, 0, 8, 4) };
        all.Click += (_, _) => { _checkedApps.Clear(); foreach (var a in _installedApps) _checkedApps.Add(a.ProcessName); RenderInstalledList(); };
        var none = new Button { Content = "清空", Margin = new Thickness(0, 0, 8, 4) };
        none.Click += (_, _) => { _checkedApps.Clear(); RenderInstalledList(); };
        var missing = new Button { Content = "只选无预设的", Margin = new Thickness(0, 0, 8, 4) };
        missing.Click += (_, _) =>
        {
            _checkedApps.Clear();
            foreach (var a in _installedApps)
                if (!HasPresetFile(a.ProcessName)) _checkedApps.Add(a.ProcessName);
            RenderInstalledList();
        };
        _appCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Gray, Margin = new Thickness(4, 0, 0, 0) };
        btnRow.Children.Add(_appRun);
        btnRow.Children.Add(rescan);
        btnRow.Children.Add(all);
        btnRow.Children.Add(none);
        btnRow.Children.Add(missing);
        btnRow.Children.Add(_appCount);

        var topBox = new StackPanel();
        topBox.Children.Add(instHead);
        topBox.Children.Add(filterRow);
        topBox.Children.Add(btnRow);
        topBox.Children.Add(Note("勾选想补齐的软件后点“AI 补齐选中”。逐个调用 AI（消耗额度）；默认已勾选“还没有预设”的。"));
        Grid.SetRow(topBox, 0); inst.Children.Add(topBox);

        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 4, 0) };
        _appRowsHost = new StackPanel();
        listScroll.Content = _appRowsHost;
        Grid.SetRow(listScroll, 1); inst.Children.Add(listScroll);

        _appScanStatus = new TextBlock { Text = "", TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Gray, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(_appScanStatus, 2); inst.Children.Add(_appScanStatus);

        Grid.SetRow(inst, 1); root.Children.Add(inst);
        page = root;
    }

    // ================= ② AI 补齐页 =================
    private void BuildAiPage(out UIElement page)
    {
        var sp = PageHost();
        sp.Children.Add(Note("作用：对扫不到快捷键的软件，让 DeepSeek 按官方常见快捷键生成清单并自动安装为预设。"));

        var steps = new TextBlock
        {
            Text = "用法：\n1) 点“取当前前台应用”（窗口会自动让开，去点那个软件窗口即可自动取回）；\n   或直接在下框输入进程名，如 chrome / notepad；\n2) 点生成按钮，等待状态显示 ✅ 成功或 ❌ 失败原因。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x4A, 0x5C)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        sp.Children.Add(steps);

        _aiAppName = new TextBox { Margin = FieldMargin(), ToolTip = "进程名，如 chrome（不带 .exe）" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        _aiAppName.Width = 220; row.Children.Add(_aiAppName);
        var pick = new Button { Content = "取当前前台应用", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
        pick.Click += (_, _) => StartPickForeground();
        row.Children.Add(pick);
        sp.Children.Add(Field("软件（进程名）", row));

        _aiRun = new Button { Content = "让 DeepSeek 生成并安装该软件的快捷键", Height = 34, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 6) };
        _aiRun.Style = (Style)Application.Current.TryFindResource("PrimaryButton")!;
        _aiRun.Click += async (_, _) => await RunAiAsync();
        sp.Children.Add(_aiRun);

        _aiProgress = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4), Foreground = Gray };
        sp.Children.Add(_aiProgress);
        page = sp;
    }

    // ================= ③ 触发按键页 =================
    private void BuildTriggerPage(out UIElement page)
    {
        var sp = PageHost();
        _chkHold = new CheckBox { Content = "长按修饰键弹出", Margin = FieldMargin(), IsChecked = _config.HoldToShow.Enabled };
        _holdModifier = Combo(ModifierNames, _config.HoldToShow.Modifier);
        _holdMs = Text(_config.HoldToShow.HoldMilliseconds.ToString());

        var holdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        _holdModifier.Width = 150; holdRow.Children.Add(_holdModifier);
        holdRow.Children.Add(new TextBlock { Text = "长按 ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        _holdMs.Width = 64; holdRow.Children.Add(_holdMs);
        holdRow.Children.Add(new TextBlock { Text = " ms", VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(_chkHold);
        sp.Children.Add(holdRow);
        sp.Children.Add(Note("按住设定修饰键超过设定毫秒即弹出（期间没按别的键才生效）。" +
                             "默认：按住 Ctrl 550ms 弹出，松开即关。"));

        _chkToggle = new CheckBox { Content = "组合热键开关弹出", Margin = FieldMargin(), IsChecked = _config.ToggleHotkey.Enabled };
        var modRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        _chkTCtrl = ModBox("Ctrl", modRow);
        _chkTAlt = ModBox("Alt", modRow);
        _chkTShift = ModBox("Shift", modRow);
        _chkTWin = ModBox("Win", modRow);
        foreach (string m in _config.ToggleHotkey.Modifiers ?? Array.Empty<string>())
        {
            switch (m.ToLowerInvariant())
            {
                case "ctrl": _chkTCtrl.IsChecked = true; break;
                case "alt": _chkTAlt.IsChecked = true; break;
                case "shift": _chkTShift.IsChecked = true; break;
                case "win": _chkTWin.IsChecked = true; break;
            }
        }
        _toggleKey = Combo(MainKeys, string.IsNullOrWhiteSpace(_config.ToggleHotkey.Key) ? "F1" : _config.ToggleHotkey.Key.ToUpperInvariant());
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        keyRow.Children.Add(new TextBlock { Text = "主键：", VerticalAlignment = VerticalAlignment.Center });
        _toggleKey.Width = 90; keyRow.Children.Add(_toggleKey);
        sp.Children.Add(_chkToggle);
        sp.Children.Add(modRow);
        sp.Children.Add(keyRow);
        sp.Children.Add(Note("示例：Ctrl+Shift+F1 显示/隐藏，Esc 关闭。"));
        page = sp;
    }

    // ================= ④ 开机自启页 =================
    private void BuildStartupPage(out UIElement page)
    {
        var sp = PageHost();
        _chkAutoStart = new CheckBox
        {
            Content = "开机自动启动（写入当前用户启动项，登录后自动驻留托盘）",
            Margin = FieldMargin(),
            IsChecked = AutoStart.IsEnabled()
        };
        sp.Children.Add(_chkAutoStart);
        sp.Children.Add(Note("勾选后立即写入注册表 HKCU\\...\\Run；取消勾选则移除。无需管理员权限。"));
        page = sp;
    }

    // ================= ⑤ 已添加的软件页 =================
    private void BuildPresetsPage(out UIElement page)
    {
        // 页面本体用 Grid：说明行(auto) + 主区(star)，保证右侧详情区拿到有限高度，滚动条可用
        var root = new Grid { Margin = new Thickness(2, 4, 2, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var note = Note("这里列出的是你已经添加/安装的软件预设（含 AI 补齐生成的），可查看其快捷键列表、删除。" +
                        "\n左侧点选软件 → 右侧显示该软件的分组快捷键；内容多时可拖动右侧滚动条。");
        Grid.SetRow(note, 0); root.Children.Add(note);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid, 1); root.Children.Add(grid);

        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var refresh = new Button { Content = "刷新", Padding = new Thickness(10, 1, 10, 1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        refresh.Click += (_, _) => RefreshPresetList();
        DockPanel.SetDock(refresh, Dock.Top);
        left.Children.Add(refresh);
        _presetList = new ListBox { Background = new SolidColorBrush(Colors.Transparent) };
        _presetList.ItemContainerStyle = (Style)Application.Current.TryFindResource("RoundedListItem")!;
        _presetList.SelectionChanged += (_, _) => ShowPresetDetail();
        left.Children.Add(_presetList);
        Grid.SetColumn(left, 0); grid.Children.Add(left);

        var right = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };
        _presetDelete = new Button { Content = "删除该软件预设", Padding = new Thickness(12, 2, 12, 2) };
        _presetDelete.Style = (Style)Application.Current.TryFindResource("DangerButton")!;
        _presetDelete.IsEnabled = false;
        _presetDelete.Click += (_, _) => DeleteSelectedPreset();
        var help = new TextBlock
        {
            Text = "改键：点击蓝色键位或功能行 → 直接按键盘组合（如先按 Ctrl 再按 S）· 琥珀色=已自定义 · 可还原官方",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Gray
        };
        btnRow.Children.Add(help);
        btnRow.Children.Add(_presetDelete);
        DockPanel.SetDock(btnRow, Dock.Top);
        right.Children.Add(btnRow);
        _presetHeader = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3F)),
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(_presetHeader, Dock.Top);
        right.Children.Add(_presetHeader);
        _presetRows = new StackPanel();
        var rowsScroll = new ScrollViewer
        {
            Content = _presetRows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        right.Children.Add(rowsScroll);
        Grid.SetColumn(right, 1); grid.Children.Add(right);

        page = root;
    }

    private void RefreshPresetList()
    {
        _presetList.Items.Clear();
        foreach (var file in _presets.ListPresets())
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("overrides-", StringComparison.OrdinalIgnoreCase)) continue;
            var set = _presets.GetForProcess(name);
            var item = new ListBoxItem
            {
                Content = set is null ? name : $"{set.AppDisplayName}（{set.TotalWithKeys} 个）",
                Tag = name
            };
            _presetList.Items.Add(item);
        }
        _presetHeader.Text = _presetList.Items.Count == 0
            ? "还没有添加任何软件预设。\n可到 ② AI 补齐 用 AI 生成。"
            : "← 点左侧软件查看/改键";
        _presetRows.Children.Clear();
        _presetDelete.IsEnabled = _presetList.Items.Count > 0;
    }

    private void ShowPresetDetail()
    {
        _presetRows.Children.Clear();
        if (_presetList.SelectedItem is not ListBoxItem li || li.Tag is not string proc)
        {
            _presetHeader.Text = "";
            _presetDelete.IsEnabled = false;
            return;
        }
        var set = _presets.GetForProcess(proc);
        if (set is null || !set.HasData)
        {
            _presetHeader.Text = $"{proc}.exe —（预设为空或解析失败）";
            _presetDelete.IsEnabled = true;
            return;
        }
        var map = _overrides.Load(proc);
        _presetDelete.IsEnabled = true;
        _presetHeader.Text = $"{set.AppDisplayName}  {proc}.exe · 共 {set.TotalWithKeys} 个快捷键" +
                             (map.Count > 0 ? $" · 你已自定义 {map.Count} 处（琥珀色）" : "");

        foreach (var g in set.Groups)
        {
            _presetRows.Children.Add(new TextBlock
            {
                Text = $"── {g.Name} ──",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
                Margin = new Thickness(0, 8, 0, 3)
            });
            foreach (var item in g.Items)
            {
                string itemKey = OverrideStore.ItemKey(g.Name, item.Description);
                bool isOver = map.TryGetValue(itemKey, out string? custom);
                string shown = isOver ? custom! : (item.Keys ?? "");
                _presetRows.Children.Add(BuildEditRow(proc, itemKey, item.Description, shown, isOver));
            }
        }
    }

    /// <summary>改键捕获：按下的组合直接成为新键位，无需手打。</summary>
    private string? _captureProc;
    private string? _captureItemKey;
    private Button? _captureButton;
    private DispatcherTimer? _captureTimeout;

    /// <summary>一行：点键位按钮后在键盘上直接按下新组合；只有改过的行才显示“还原官方”。</summary>
    private UIElement BuildEditRow(string proc, string itemKey, string desc, string keys, bool isOver)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btn = new Button
        {
            Content = isOver ? keys : (string.IsNullOrWhiteSpace(keys) ? "（无）" : keys),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Padding = new Thickness(6, 1, 6, 1),
            MinWidth = 148,
            ToolTip = "点我，然后在键盘上按下新键位（如先按住 Ctrl 再按 S），Esc 取消",
            Foreground = isOver
                ? new SolidColorBrush(Color.FromRgb(0xB2, 0x8A, 0x2E))
                : new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
        };
        btn.Click += (_, _) => BeginCapture(proc, itemKey, desc, btn);
        Grid.SetColumn(btn, 0); row.Children.Add(btn);

        var descText = new TextBlock
        {
            Text = (isOver ? "⚙ " : "") + desc,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = isOver
                ? new SolidColorBrush(Color.FromRgb(0xB2, 0x8A, 0x2E))
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3F))
        };
        descText.MouseLeftButtonUp += (_, _) => BeginCapture(proc, itemKey, desc, btn);
        Grid.SetColumn(descText, 1); row.Children.Add(descText);

        if (isOver)
        {
            var restore = new Button { Content = "还原官方", Padding = new Thickness(8, 1, 8, 1), HorizontalAlignment = HorizontalAlignment.Right };
            restore.Click += (_, _) =>
            {
                CancelCapture("已取消改键");
                var m = _overrides.Load(proc);
                m.Remove(itemKey);
                _overrides.Save(proc, m);
                _status.Text = $"已还原官方键位：{desc}";
                ShowPresetDetail();
            };
            Grid.SetColumn(restore, 2); row.Children.Add(restore);
        }
        return row;
    }

    private void BeginCapture(string proc, string itemKey, string desc, Button btn)
    {
        if (_captureItemKey is not null) CancelCapture("已取消上次改键");
        _captureProc = proc;
        _captureItemKey = itemKey;
        _captureButton = btn;
        var original = btn.Content?.ToString() ?? "";
        btn.Content = original.Length > 0 ? $"原 {original} —— 请按新键（Esc 取消）…" : "请按新键（Esc 取消）…";
        _status.Text = $"改键中：{desc} —— 现在直接按下新键位（可组合，如先按住 Ctrl 再按 S）；Esc 取消";
        _captureTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _captureTimeout.Tick += (_, _) => CancelCapture("改键已超时取消");
        _captureTimeout.Start();
    }

    private void CancelCapture(string message)
    {
        _captureTimeout?.Stop();
        if (_captureButton is not null)
        {
            // 复位按钮文字：重读当前键位
            ShowPresetDetail();
        }
        _captureProc = null;
        _captureItemKey = null;
        _captureButton = null;
        _status.Text = message;
    }

    private void WindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

        // 双保险：设置窗口自身也能用 Esc 关闭“AI 补齐后弹出”的浮层（避免 IME 吞掉全局钩子的 Esc）
        if (key == System.Windows.Input.Key.Escape && _captureItemKey is null)
        {
            if (_overlayVisible())
            {
                _closeOverlay();
                e.Handled = true;
            }
            return;
        }

        if (_captureItemKey is null) return;
        if (key == System.Windows.Input.Key.Escape)
        {
            CancelCapture("已取消改键");
            e.Handled = true;
            return;
        }
        if (IsModifier(key))
        {
            e.Handled = true; // 等主键
            return;
        }

        e.Handled = true;
        string combo = ComposeKeyText(key);
        if (combo.Length == 0)
        {
            _status.Text = "不支持的键，请重试（或按 Esc 取消）。";
            return;
        }

        // 保存捕获结果（直接用键盘按出的组合，无需手动输入）
        string proc = _captureProc!;
        string itemKey = _captureItemKey!;
        _captureTimeout?.Stop();
        _captureProc = null;
        _captureItemKey = null;
        _captureButton = null;

        var m = _overrides.Load(proc);
        m[itemKey] = combo;
        _overrides.Save(proc, m);
        _status.Text = $"已记录：{combo}";
        ShowPresetDetail();
    }

    private static string ComposeKeyText(System.Windows.Input.Key key)
    {
        var parts = new List<string>();
        var m = System.Windows.Input.Keyboard.Modifiers;
        if ((m & System.Windows.Input.ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((m & System.Windows.Input.ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((m & System.Windows.Input.ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((m & System.Windows.Input.ModifierKeys.Windows) != 0) parts.Add("Win");

        string main = key switch
        {
            _ when key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z
                => ((char)('A' + (key - System.Windows.Input.Key.A))).ToString(),
            _ when key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9
                => ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString(),
            System.Windows.Input.Key.Space => "Space",
            System.Windows.Input.Key.Enter => "Enter",
            System.Windows.Input.Key.Tab => "Tab",
            System.Windows.Input.Key.Back => "Backspace",
            System.Windows.Input.Key.Delete => "Del",
            System.Windows.Input.Key.Insert => "Ins",
            System.Windows.Input.Key.Home => "Home",
            System.Windows.Input.Key.End => "End",
            System.Windows.Input.Key.PageUp => "PgUp",
            System.Windows.Input.Key.PageDown => "PgDn",
            System.Windows.Input.Key.Up => "Up",
            System.Windows.Input.Key.Down => "Down",
            System.Windows.Input.Key.Left => "Left",
            System.Windows.Input.Key.Right => "Right",
            _ when key >= System.Windows.Input.Key.F1 && key <= System.Windows.Input.Key.F24
                => "F" + (key - System.Windows.Input.Key.F1 + 1),
            _ => ""
        };
        if (main.Length == 0) return "";
        return parts.Count == 0 ? main : string.Join("+", parts) + "+" + main;
    }

    private static bool IsModifier(System.Windows.Input.Key k) => k is
        System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
        or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
        or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
        or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;

    private void DeleteSelectedPreset()
    {
        if (_presetList.SelectedItem is not ListBoxItem li || li.Tag is not string proc) return;
        if (MessageBox.Show($"删除 {proc}.exe 的预设与其自定义覆盖？删除后该软件将回到“扫不到/无数据”状态。",
                "KeyPeeker · 删除预设", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            _overrides.DeleteFor(proc); // 连个人自定义键位一起清掉
            string path = Path.Combine(_presets.PresetsDir, proc + ".json");
            if (File.Exists(path)) File.Delete(path);
            _status.Text = $"已删除 {proc}.json";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "KeyPeeker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshPresetList();
    }

    // ================= “取当前前台应用”：最小化自己，等用户切到目标软件自动捕获 =================
    private void StartPickForeground()
    {
        if (_aiRunning || _picking) return;
        _picking = true;
        SetAiText(Gray, "已让开窗口——现在请点击你想添加的那个软件窗口（如微信、Chrome…）。");
        Hide(); // 自己不是前台后，用户点击目标软件即可被捕获

        int ticks = 0;
        _pickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pickTimer.Tick += (_, _) =>
        {
            ticks++;
            var fg = _getForeground();
            if (fg is not null)
            {
                _pickTimer.Stop();
                _picking = false;
                _aiAppName.Text = fg.Value.ProcessName;
                _aiTitle = fg.Value.WindowTitle;
                Show();
                Activate();
                SetAiText(Gray, $"已取前台应用：{fg.Value.ProcessName}.exe  （{fg.Value.WindowTitle}）");
            }
            else if (ticks > 80) // 20 秒超时
            {
                _pickTimer.Stop();
                _picking = false;
                Show();
                Activate();
                SetAiText(Red, "未检测到目标软件窗口（20 秒超时）。请重试，或在输入框手动输入进程名。");
            }
        };
        _pickTimer.Start();
    }

    private async Task RunAiAsync()
    {
        if (_aiRunning) return;
        string proc = (_aiAppName.Text ?? "").Trim();
        if (proc.Length == 0)
        {
            SetAiResult(false, "请先输入软件进程名，或用“取当前前台应用”按钮。");
            return;
        }
        if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            proc = proc[..^4];

        _aiRunning = true;
        _aiRun.IsEnabled = false;
        SetAiResult(null, $"正在让 DeepSeek 生成 {proc}.exe 的快捷键…");

        var progress = new Progress<string>(t => _aiProgress.Text = t);
        try
        {
            var (ok, message) = await _aiGenerate(proc, _aiTitle.Length > 0 ? _aiTitle : proc, progress, true);
            SetAiResult(ok, message);
            if (ok) RefreshPresetList();
        }
        catch (Exception ex)
        {
            SetAiResult(false, $"发生错误：{ex.Message}");
        }
        finally
        {
            _aiRunning = false;
            _aiRun.IsEnabled = true;
        }
    }

    private void SetAiResult(bool? ok, string text) => SetAiText(ok switch
    {
        true => Green,
        false => Red,
        null => Gray
    }, text);

    private void SetAiText(Brush color, string text)
    {
        _aiProgress.Text = text;
        _aiProgress.Foreground = color;
    }

    // ================= ⑥ 已装软件一键补齐 =================
    private void BuildInstalledPage(out UIElement page)
    {
        var root = new Grid { Margin = new Thickness(2, 4, 2, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(Note("自动扫描本机已安装软件 → 勾选想要的一批 → 一键让 DeepSeek 逐个体检并安装快捷键预设。\n" +
                              "提示：会按勾选数量逐个调用 AI（消耗 API 额度）；可先“只选无预设”避免重复生成。"));
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = FieldMargin() };
        _appFilter = new TextBox { Width = 240, ToolTip = "按名称筛选" };
        _appFilter.TextChanged += (_, _) => RenderInstalledList();
        filterRow.Children.Add(_appFilter);
        filterRow.Children.Add(new TextBlock { Text = "  筛选（如 微信 / chrome）", VerticalAlignment = VerticalAlignment.Center, Foreground = Gray });
        top.Children.Add(filterRow);

        var btnRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        _appRun = new Button { Content = "AI 补齐选中（0）", Style = (Style)Application.Current.TryFindResource("PrimaryButton")!, Margin = new Thickness(0, 0, 8, 4) };
        _appRun.Click += async (_, _) => await RunBatchAiAsync();
        var rescan = new Button { Content = "重新扫描", Margin = new Thickness(0, 0, 8, 4) };
        rescan.Click += (_, _) => ScanInstalledNow();
        var all = new Button { Content = "全选", Margin = new Thickness(0, 0, 8, 4) };
        all.Click += (_, _) => { _checkedApps.Clear(); foreach (var a in _installedApps) _checkedApps.Add(a.ProcessName); RenderInstalledList(); };
        var none = new Button { Content = "清空", Margin = new Thickness(0, 0, 8, 4) };
        none.Click += (_, _) => { _checkedApps.Clear(); RenderInstalledList(); };
        var missing = new Button { Content = "只选无预设的", Margin = new Thickness(0, 0, 8, 4) };
        missing.Click += (_, _) =>
        {
            _checkedApps.Clear();
            foreach (var a in _installedApps)
                if (!HasPresetFile(a.ProcessName)) _checkedApps.Add(a.ProcessName);
            RenderInstalledList();
        };
        _appCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Gray, Margin = new Thickness(4, 0, 0, 0) };
        btnRow.Children.Add(_appRun);
        btnRow.Children.Add(rescan);
        btnRow.Children.Add(all);
        btnRow.Children.Add(none);
        btnRow.Children.Add(missing);
        btnRow.Children.Add(_appCount);
        top.Children.Add(btnRow);

        Grid.SetRow(top, 0); root.Children.Add(top);

        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 4, 0) };
        _appRowsHost = new StackPanel();
        listScroll.Content = _appRowsHost;
        Grid.SetRow(listScroll, 1); root.Children.Add(listScroll);

        var bottom = new TextBlock
        {
            Text = "", TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Gray,
            Margin = new Thickness(0, 6, 0, 0)
        };
        _appScanStatus = bottom;
        Grid.SetRow(bottom, 2); root.Children.Add(bottom);

        page = root;
    }

    private TextBlock _appScanStatus;

    /// <summary>轻量判断某进程是否已有预设文件（避免全量 JSON 解析，批量列表时防卡）。</summary>
    private bool HasPresetFile(string proc) =>
        !string.IsNullOrWhiteSpace(proc) && File.Exists(Path.Combine(_presets.PresetsDir, proc + ".json"));

    private void ScanInstalledNow()
    {
        try
        {
            _installedApps = InstalledAppsScanner.Scan();
            _checkedApps.Clear();
            foreach (var a in _installedApps)
                if (!HasPresetFile(a.ProcessName)) _checkedApps.Add(a.ProcessName);
            _appResults.Clear();
            _scanError = "";
        }
        catch (Exception ex)
        {
            _scanError = ex.Message;
            _installedApps = new List<InstalledApp>();
        }
        RenderInstalledList();
        if (_appScanStatus is not null)
            _appScanStatus.Text = _scanError.Length > 0
                ? $"扫描失败：{_scanError}"
                : $"已扫描到 {_installedApps.Count} 个软件（默认勾选“还没有预设的”）。";
    }

    private void RenderInstalledList()
    {
        _appRowsHost?.Children.Clear();
        if (_appRowsHost is null) return;

        string filter = (_appFilter?.Text ?? "").Trim();
        int shown = 0, withPreset = 0;
        foreach (var app in _installedApps)
        {
            if (filter.Length > 0 &&
                app.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                app.ProcessName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            shown++;
            bool hasPreset = HasPresetFile(app.ProcessName);
            if (hasPreset) withPreset++;

            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsChecked = _checkedApps.Contains(app.ProcessName), Tag = app.ProcessName };
            chk.Checked += (_, _) => _checkedApps.Add((string)chk.Tag!);
            chk.Unchecked += (_, _) => _checkedApps.Remove((string)chk.Tag!);
            Grid.SetColumn(chk, 0); row.Children.Add(chk);

            var name = new TextBlock
            {
                Text = app.DisplayName + (hasPreset ? "" : ""),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F))
            };
            var sub = new TextBlock
            {
                Text = hasPreset ? "  已有预设" : "",
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = hasPreset ? Green : Gray
            };
            var nameStack = new StackPanel { Orientation = Orientation.Horizontal };
            nameStack.Children.Add(name);
            nameStack.Children.Add(sub);
            Grid.SetColumn(nameStack, 1); row.Children.Add(nameStack);

            _appResults.TryGetValue(app.ProcessName, out var res);
            string state = res.State switch
            {
                1 => "✅ 已生成",
                2 => "❌ " + (res.Extra.Length > 30 ? res.Extra[..30] : res.Extra),
                3 => "已跳过（已有预设）",
                4 => "⏳ 生成中…",
                _ => app.ProcessName + ".exe"
            };
            var stateText = new TextBlock
            {
                Text = state,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 2, 0),
                Foreground = res.State switch
                {
                    1 => Green, 2 => Red, 3 => Gray, 4 => new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
                    _ => TertGray
                }
            };
            Grid.SetColumn(stateText, 2); row.Children.Add(stateText);

            _appRowsHost.Children.Add(row);
        }

        if (shown == 0)
        {
            _appRowsHost.Children.Add(new TextBlock
            {
                Text = _scanError.Length > 0 ? $"扫描失败：{_scanError}" : "没有匹配的软件（可点“重新扫描”或换个筛选词）",
                Foreground = Gray, Margin = new Thickness(4, 8, 0, 4)
            });
        }
        if (_appCount is not null)
            _appCount.Text = $"已选 {_checkedApps.Count} / {_installedApps.Count}（已有预设 {withPreset}）";
        if (_appRun is not null)
            _appRun.Content = $"AI 补齐选中（{_checkedApps.Count}）";
    }

    private async Task RunBatchAiAsync()
    {
        if (_batchRunning) return;
        if (_checkedApps.Count == 0) { _appScanStatus.Text = "请先勾选要补齐的软件。"; return; }
        if (string.IsNullOrWhiteSpace(_config.Ai.ApiKey))
        {
            _appScanStatus.Text = "未配置 DeepSeek API Key：请到 ① 填写并保存。";
            return;
        }

        _batchRunning = true;
        _appRun.IsEnabled = false;
        int total = _checkedApps.Count, idx = 0, okCount = 0;
        try
        {
            foreach (var proc in _checkedApps.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList())
            {
                idx++;
                var app = _installedApps.FirstOrDefault(a => a.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase));
                _appScanStatus.Text = $"正在处理 {idx}/{total}：{app?.DisplayName ?? proc} …";

                if (HasPresetFile(proc))
                {
                    _appResults[proc] = (3, "");
                    RenderInstalledList();
                    continue;
                }

                _appResults[proc] = (4, "");
                RenderInstalledList();

                var progress = new Progress<string>(_ => { });
                var (ok, message) = await _aiGenerate(proc, app?.DisplayName ?? proc, progress, false);
                if (ok)
                {
                    _appResults[proc] = (1, message);
                    okCount++;
                }
                else
                {
                    _appResults[proc] = (2, message);
                }
                RenderInstalledList();
                await Task.Delay(200);
            }
            _appScanStatus.Text = $"完成：成功 {okCount} / {total}（失败可看右侧 ❌ 原因后重试）。";
        }
        catch (Exception ex)
        {
            _appScanStatus.Text = $"批量处理出错：{ex.Message}";
        }
        finally
        {
            _batchRunning = false;
            _appRun.IsEnabled = true;
            RenderInstalledList();
        }
    }

    private void Save()
    {
        if (!int.TryParse(_holdMs.Text.Trim(), out int ms) || ms < 200 || ms > 5000)
        {
            _status.Text = "长按毫秒数无效（200~5000）。";
            return;
        }
        var mods = new List<string>();
        if (_chkTCtrl.IsChecked == true) mods.Add("Ctrl");
        if (_chkTAlt.IsChecked == true) mods.Add("Alt");
        if (_chkTShift.IsChecked == true) mods.Add("Shift");
        if (_chkTWin.IsChecked == true) mods.Add("Win");
        if (_chkToggle.IsChecked == true && mods.Count == 0)
        {
            _status.Text = "组合热键至少要勾选一个修饰键（避免误触）。";
            return;
        }

        _config.Ai.ApiKey = _apiKey.Password.Trim();
        // 仅支持 DeepSeek 官方：模型/地址固定，不提供自定义
        _config.Ai.Model = "deepseek-chat";
        _config.Ai.BaseUrl = DeepSeekClient.DefaultBaseUrl;

        _config.HoldToShow.Enabled = _chkHold.IsChecked == true;
        _config.HoldToShow.Modifier = (string)_holdModifier.SelectedItem;
        _config.HoldToShow.HoldMilliseconds = ms;

        _config.ToggleHotkey.Enabled = _chkToggle.IsChecked == true;
        _config.ToggleHotkey.Key = (string)_toggleKey.SelectedItem;
        _config.ToggleHotkey.Modifiers = mods.ToArray();

        bool wantStart = _chkAutoStart.IsChecked == true;
        bool okStart = AutoStart.SetEnabled(wantStart);
        if (!okStart) _status.Text = "写入开机自启被系统拒绝，其余设置已保存。";

        _onSaved(_config);
        _status.Text = "已保存并生效。" + (okStart ? "" : "（自启未生效）");
    }

    // ================= UI 构建辅助 =================
    private static StackPanel PageHost() => new() { Margin = new Thickness(2, 4, 2, 4) };
    private static Thickness FieldMargin() => new(0, 2, 0, 4);

    private static UIElement Field(string label, UIElement control)
    {
        var grid = new Grid { Margin = FieldMargin() };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(l, 0); grid.Children.Add(l);
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }

    private static TextBox Text(string value) => new() { Text = value, Margin = FieldMargin(), VerticalContentAlignment = VerticalAlignment.Center };

    private static ComboBox Combo(string[] items, string selected)
    {
        var cb = new ComboBox { Margin = FieldMargin() };
        foreach (var it in items) cb.Items.Add(it);
        cb.SelectedItem = items.FirstOrDefault(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase))
                          ?? items[0];
        return cb;
    }

    private static CheckBox ModBox(string name, StackPanel parent)
    {
        var cb = new CheckBox { Content = name, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        parent.Children.Add(cb);
        return cb;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Gray,
        Margin = new Thickness(0, 2, 0, 8)
    };
}
