using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KeySheet.Core;

namespace KeySheet.UI;

/// <summary>
/// 快捷键悬浮面板 —— Apple 简约圆角风格：半透明白色玻璃卡 + 大圆角 + 柔和阴影，
/// 键位显示为浅灰“胶囊”，顶部来源为小圆角徽章。无边框、置顶、不抢焦点。
/// 关闭时机（Esc / 松开修饰键 / 再次触发）由控制器负责。
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _processText;
    private readonly TextBlock _sourceText;
    private readonly Border _sourcePill;
    private readonly TextBlock _footerHint;
    private readonly TextBlock _countText;
    private readonly StackPanel _colsHost;
    private readonly ScrollViewer _scroll;

    // ---- Apple 系统色 ----
    private static readonly Color CardBg = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);          // 近白玻璃
    private static readonly Color Hairline = Color.FromArgb(0x24, 0x00, 0x00, 0x00);         // 发丝分割线
    private static readonly Color PrimaryText = Color.FromRgb(0x1D, 0x1D, 0x1F);
    private static readonly Color SecondaryText = Color.FromRgb(0x6E, 0x6E, 0x73);
    private static readonly Color TertiaryText = Color.FromRgb(0xAE, 0xAE, 0xB2);
    private static readonly Color PillBg = Color.FromRgb(0xF2, 0xF2, 0xF7);                  // 键位胶囊底
    private static readonly Color PillBgOverride = Color.FromRgb(0xFF, 0xF3, 0xDD);          // 自定义键胶囊（暖琥珀）
    private static readonly Color OverrideText = Color.FromRgb(0x9A, 0x6B, 0x00);

    private static readonly SolidColorBrush BgCard = new(CardBg);
    private static readonly SolidColorBrush LineBrush = new(Hairline);
    private static readonly SolidColorBrush TitleBrush = new(PrimaryText);
    private static readonly SolidColorBrush SubBrush = new(SecondaryText);
    private static readonly SolidColorBrush TertBrush = new(TertiaryText);
    private static readonly SolidColorBrush KeyTextBrush = new(PrimaryText);
    private static readonly SolidColorBrush KeyOverrideTextBrush = new(OverrideText);

    public bool TriggeredByHold { get; set; }

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Width;

        _titleText = MakeText(TitleBrush, 17, FontWeights.SemiBold);
        _processText = MakeText(SubBrush, 12, FontWeights.Normal);
        _sourceText = MakeText(Brushes.White, 11, FontWeights.SemiBold);
        _sourcePill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 9, 3.5),
            Background = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = _sourceText
        };

        _countText = MakeText(SubBrush, 11, FontWeights.Normal);
        _countText.HorizontalAlignment = HorizontalAlignment.Right;
        _footerHint = MakeText(TertBrush, 11, FontWeights.Normal);

        // ---------- 头部 ----------
        var titleStack = new StackPanel();
        titleStack.Children.Add(_titleText);
        _processText.Margin = new Thickness(0, 3, 0, 0);
        titleStack.Children.Add(_processText);

        var headGrid = new Grid();
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(_sourcePill, 1);
        headGrid.Children.Add(titleStack);
        headGrid.Children.Add(_sourcePill);

        // ---------- 主体：多列容器（分类整组排列，不跨列） ----------
        _colsHost = new StackPanel { Orientation = Orientation.Horizontal };
        _scroll = new ScrollViewer
        {
            Content = _colsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false,
            Margin = new Thickness(6, 4, 6, 0)
        };

        // ---------- 底部 ----------
        var footer = new Grid { Margin = new Thickness(20, 7, 20, 13) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_footerHint, 0);
        Grid.SetColumn(_countText, 1);
        footer.Children.Add(_footerHint);
        footer.Children.Add(_countText);

        var stack = new StackPanel();
        var line1 = new Border { Height = 1, Background = LineBrush };
        var line2 = new Border { Height = 1, Background = LineBrush };
        var headHost = new StackPanel { Margin = new Thickness(20, 16, 20, 11) };
        headHost.Children.Add(headGrid);
        stack.Children.Add(headHost);
        stack.Children.Add(line1);
        stack.Children.Add(_scroll);
        stack.Children.Add(line2);
        stack.Children.Add(footer);

        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = BgCard,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF)), // 内侧亮边
            BorderThickness = new Thickness(1),
            Child = stack
        };
        card.Effect = new DropShadowEffect
        {
            BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.35, Color = Colors.Black
        };

        Content = new Border { Background = Brushes.Transparent, Child = card, Margin = new Thickness(16) };
        SourceInitialized += (_, _) => MakeNoActivate();
    }

    private void MakeNoActivate()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    /// <summary>填充数据并显示在光标附近。可反复调用以刷新内容。</summary>
    public void ShowFor(ShortcutSet set, AppConfig config, bool triggeredByHold)
    {
        TriggeredByHold = triggeredByHold;
        SizeToContent = SizeToContent.Manual;

        _titleText.Text = set.AppDisplayName;
        _processText.Text = $"{set.ProcessName}.exe";
        _countText.Text = set.HasData ? $"共 {set.TotalWithKeys} 个快捷键" : "";
        bool anyOverride = set.Groups.Any(g => g.Items.Any(i => i.Overridden));
        _footerHint.Text = triggeredByHold
            ? "松开修饰键即关闭 · Esc 也可关闭" + (anyOverride ? " · 琥珀色=你自定义" : "")
            : "再次按触发热键或 Esc 关闭 · 切到别的窗口再触发可刷新" + (anyOverride ? " · 琥珀色=你自定义" : "");

        (string label, SolidColorBrush bg) = set.Source switch
        {
            ShortcutSource.RealMenu => ("● 实时读取", Solid(SolidGreen)),
            ShortcutSource.Preset => ("● 内置预设", Solid(SolidAmber)),
            _ => ("○ 无数据", new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)))
        };
        _sourceText.Text = label;
        _sourcePill.Background = bg;

        // ---------- 多列布局：以“分类组”为单位；按总行数平均分成 1~3 列，一组绝不跨列 ----------
        const double rowH = 34, groupHeadH = 46, colGap = 30, sidePad = 44;
        double budgetH = config.Popup.MaxHeight;
        var blocks = new List<(string Name, List<ShortcutItem> Items, double H, double W)>();

        if (set.HasData)
        {
            foreach (var g in set.Groups)
            {
                var items = g.Items.Where(i =>
                        !config.Popup.ShowOnlyWithShortcuts || !string.IsNullOrWhiteSpace(i.Keys))
                    .ToList();
                if (items.Count == 0) continue;
                int maxK = 0, maxD = 0;
                foreach (var it in items)
                {
                    maxK = Math.Max(maxK, it.Keys?.Length ?? 0);
                    maxD = Math.Max(maxD, Math.Min((it.Description?.Length ?? 0), 40));
                }
                double w = Math.Max(380, maxK * 8.6 + maxD * 7.2 + 110);
                blocks.Add((g.Name, items, groupHeadH + items.Count * rowH + 8, w));
            }
        }

        // 目标列数：行数越多列数越多，控制在 1~3 列
        int totalRows = blocks.Sum(b => b.Items.Count);
        int targetCols = totalRows switch
        {
            <= 20 => 1,
            <= 42 => 2,
            _ => 3
        };
        targetCols = Math.Min(targetCols, Math.Max(blocks.Count, 1));

        // 按高度从大到小放入“当前最矮”的列（保证整组不拆、列高尽量均衡）
        var columns = new List<List<(string Name, List<ShortcutItem> Items, double H, double W)>>();
        var binH = new List<double>();
        for (int i = 0; i < targetCols; i++)
        {
            columns.Add(new List<(string Name, List<ShortcutItem> Items, double H, double W)>());
            binH.Add(0);
        }
        foreach (var b in blocks.OrderByDescending(x => x.H))
        {
            int idx = 0;
            for (int i = 1; i < binH.Count; i++)
                if (binH[i] < binH[idx]) idx = i;
            columns[idx].Add(b);
            binH[idx] += b.H;
        }
        columns.RemoveAll(c => c.Count == 0);

        _colsHost.Children.Clear();
        double totalW;
        if (columns.Count == 0) // 无数据
        {
            totalW = 620;
            var emptyCol = new StackPanel { Width = 600 };
            emptyCol.Children.Add(BuildEmptyState(set));
            _colsHost.Children.Add(emptyCol);
        }
        else
        {
            totalW = (columns.Count - 1) * colGap + sidePad;
            foreach (var column in columns)
            {
                double colW = column.Max(b => b.W);
                var colPanel = new StackPanel { Width = colW, Margin = new Thickness(4, 0, 10, 0) };
                Grid.SetIsSharedSizeScope(colPanel, true);
                foreach (var (name, items, _, _) in column)
                    colPanel.Children.Add(BuildGroup(name, items));
                _colsHost.Children.Add(colPanel);
                totalW += colW;
            }
        }

        // 尺寸：宽 = 各列之和（封顶屏幕宽，超出出横向滚动条）；高 = 最高列，不用下翻
        double maxColH = columns.Count > 0 ? columns.Max(c => c.Sum(b => b.H)) : 160;
        double fixedChrome = 118; // 头部+分割线+底部
        double wantW = totalW;
        NativeMethods.GetCursorPos(out var cp);
        double capW = 700;
        try
        {
            var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cp.X, cp.Y)).WorkingArea;
            double scale = GetDpiScale(new WindowInteropHelper(this).Handle);
            capW = Math.Max(wa.Width / scale - 24, 700);
        }
        catch { /* 默认 700 兜底 */ }

        Width = Math.Min(wantW, capW);
        double bodyH = Math.Min(maxColH + 6, budgetH);
        Height = Math.Min(bodyH + fixedChrome, budgetH + fixedChrome);
        Opacity = config.Popup.Opacity;

        Show();
        UpdateLayout();
        CenterOnScreen();
        ActivateGuard();
    }

    private static readonly Color SolidGreen = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color SolidAmber = Color.FromRgb(0xFF, 0x9F, 0x0A);

    private void ActivateGuard()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    private UIElement BuildEmptyState(ShortcutSet set)
    {
        var sp = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        var t1 = MakeText(TitleBrush, 13.5, FontWeights.SemiBold);
        t1.Text = "没找到这个应用的快捷键数据";
        t1.Margin = new Thickness(0, 0, 0, 6);
        var t2 = MakeText(SubBrush, 12, FontWeights.Normal);
        t2.TextWrapping = TextWrapping.Wrap;
        t2.Text = set.SourceDetail ?? "";
        t2.Margin = new Thickness(0, 0, 0, 6);
        var t3 = MakeText(TertBrush, 11, FontWeights.Normal);
        t3.TextWrapping = TextWrapping.Wrap;
        t3.Text = "① 托盘 →「设置」→ ② AI 补齐：填 DeepSeek API Key 后一键让 AI 生成该软件快捷键\n② 或 托盘 →「为当前应用创建预设模板」手动维护";
        sp.Children.Add(t1);
        sp.Children.Add(t2);
        sp.Children.Add(t3);
        return sp;
    }

    private UIElement BuildGroup(string name, IReadOnlyList<ShortcutItem> items)
    {
        var panel = new StackPanel { Margin = new Thickness(10, 2, 10, 0) };

        var headText = MakeText(SubBrush, 12, FontWeights.SemiBold);
        headText.Text = name.ToUpperInvariant();
        headText.Margin = new Thickness(14, 13, 4, 6);
        panel.Children.Add(headText);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "keys" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rowIndex = 0;
        foreach (var item in items)
        {
            bool hasKeys = !string.IsNullOrWhiteSpace(item.Keys);
            bool over = item.Overridden && hasKeys;

            // CheatSheet 式：键位列（等宽对齐）+ 说明列
            var keys = MakeText(over ? KeyOverrideTextBrush : (hasKeys ? KeyTextBrush : TertBrush),
                13.5, FontWeights.SemiBold);
            keys.FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
            keys.Text = hasKeys ? item.Keys : "—";
            keys.Margin = new Thickness(16, 5, 26, 5);
            Grid.SetColumn(keys, 0);
            Grid.SetRow(keys, rowIndex);
            grid.Children.Add(keys);

            var desc = MakeText(hasKeys ? TitleBrush : TertBrush, 13.5, FontWeights.Normal);
            desc.Text = item.Description;
            desc.Margin = new Thickness(4, 5, 20, 5);
            Grid.SetColumn(desc, 1);
            Grid.SetRow(desc, rowIndex);
            grid.Children.Add(desc);

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rowIndex++;
        }

        panel.Children.Add(grid);
        return panel;
    }

    private static TextBlock MakeText(Brush color, double size, FontWeight weight)
    {
        return new TextBlock
        {
            Foreground = color,
            FontSize = size,
            FontWeight = weight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static SolidColorBrush Solid(Color c) => new(c);

    /// <summary>把弹窗居中到鼠标所在屏幕的正中间。</summary>
    private void CenterOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.GetCursorPos(out var pt);
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y)).WorkingArea;
        double scale = GetDpiScale(hwnd);

        Left = ((wa.Left + wa.Width / 2.0) / scale) - ActualWidth / 2.0;
        Top = ((wa.Top + wa.Height / 2.0) / scale) - ActualHeight / 2.0;
        // 极端情况下防溢出
        Left = Math.Max(wa.Left / scale + 4, Left);
        Top = Math.Max(wa.Top / scale + 4, Top);
    }

    private static double GetDpiScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}
