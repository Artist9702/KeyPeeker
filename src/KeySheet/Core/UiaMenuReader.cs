using System.Windows.Automation;

namespace KeySheet.Core;

/// <summary>
/// UI Automation 实时扫描层（第三数据源，位于 Win32 菜单之后、预设之前）。
/// 对部分以 UIA 暴露菜单的应用（WPF 传统菜单、部分老式对话框/程序）能读到
/// 加速键文本。全程只读（GetCurrentPropertyValue / FindAll），不调用 Invoke，
/// 不打开菜单、不发送输入。带超时保护，防止个别应用的 UIA 提供程序卡住主线程。
/// </summary>
public static class UiaMenuReader
{
    /// <summary>返回 null = 没有可用的 UIA 菜单结构；空列表 = 有菜单但没读到快捷键。</summary>
    public static List<ShortcutGroup>? TryRead(IntPtr hwnd, int timeoutMs = 500)
    {
        try
        {
            List<ShortcutGroup>? result = null;
            var task = Task.Run(() => result = Scan(hwnd));
            if (!task.Wait(timeoutMs))
                return null; // 提供程序太慢，放弃（宁可无数据也不卡界面）

            return result is { Count: > 0 } ? result : null;
        }
        catch
        {
            return null; // UIA 对无辅助功能的应用/系统保护窗口可能直接抛错
        }
    }

    private static List<ShortcutGroup>? Scan(IntPtr hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        if (root is null) return null;

        var menuBars = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));

        var menus = new List<AutomationElement>();
        if (menuBars is { Count: > 0 })
        {
            foreach (AutomationElement bar in menuBars) menus.Add(bar);
        }
        else
        {
            // 部分程序只暴露 ControlType.Menu 的顶层菜单
            var topMenus = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
            foreach (AutomationElement m in topMenus) menus.Add(m);
        }
        if (menus.Count == 0) return null;

        var groups = new List<ShortcutGroup>();
        var visited = new HashSet<string>();
        foreach (var menu in menus)
        {
            var children = menu.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
            foreach (AutomationElement top in children)
            {
                string topName = NameOf(top);
                if (topName.Length == 0 || !visited.Add(topName)) continue;

                var items = ReadSubItems(top);
                if (items.Count == 0) continue;
                groups.Add(new ShortcutGroup(topName, items));
            }
        }
        return groups;
    }

    /// <summary>递归读一个顶层菜单项下的命令项（含两级子菜单，扁平化到该分组）。</summary>
    private static List<ShortcutItem> ReadSubItems(AutomationElement parent, int depth = 0)
    {
        var list = new List<ShortcutItem>();
        if (depth > 2) return list;

        var children = parent.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
        foreach (AutomationElement child in children)
        {
            string rawName = child.Current.Name ?? "";
            string keys = "";
            string desc = rawName;

            // 部分提供程序把加速键放在 AcceleratorKey，或拼在名称尾部
            try
            {
                var acc = child.Current.AcceleratorKey;
                if (!string.IsNullOrEmpty(acc)) keys = acc;
            }
            catch { /* 个别属性读取失败忽略 */ }

            int tab = desc.IndexOf('\t');
            if (tab >= 0)
            {
                desc = rawName[..tab].Trim();
                if (keys.Length == 0) keys = rawName[(tab + 1)..].Trim();
            }
            if (keys.Length == 0)
            {
                // 尝试解析形如 “Save (Ctrl+S)” / “保存  Ctrl+S” 的尾巴
                var m = System.Text.RegularExpressions.Regex.Match(desc, @"[\t ]{1,3}(?<k>[A-Za-z0-9+ ]{2,20})$");
                if (m.Success && LooksLikeKeys(m.Groups["k"].Value))
                {
                    keys = m.Groups["k"].Value.Trim();
                    desc = desc[..m.Index].Trim();
                }
            }

            desc = CleanLabel(desc);
            if (desc.Length == 0) continue;
            keys = keys.Replace(" ", "");
            if (!LooksLikeKeys(keys)) keys = "";

            // 有子菜单的项本身可能无快捷键，递归展开子命令
            var sub = ReadSubItems(child, depth + 1);
            if (sub.Count > 0)
            {
                list.AddRange(sub);
                continue;
            }
            list.Add(new ShortcutItem(keys, desc));
        }
        return list;
    }

    private static string NameOf(AutomationElement el)
    {
        try { return (el.Current.Name ?? "").Trim(); }
        catch { return ""; }
    }

    private static string CleanLabel(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace("&", "");
        s = s.Replace("...", "");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static bool LooksLikeKeys(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        // 至少含一个修饰键前缀，或为 F 键/单功能键，避免把普通单词当快捷键
        return System.Text.RegularExpressions.Regex.IsMatch(s,
            @"^(?i)((Ctrl|Alt|Shift|Win)\+)+([A-Z0-9]|F\d{1,2})$|^(?i)(F\d{1,2}|Del|Delete|Esc|Tab|Enter|Home|End)$");
    }
}
