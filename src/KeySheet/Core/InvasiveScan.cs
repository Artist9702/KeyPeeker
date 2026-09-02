using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace KeySheet.Core;

/// <summary>
/// 侵入式激活扫描（兜底第四数据源）：部分应用（尤其带 Ribbon/动态菜单的程序）
/// 的菜单内容要按 Alt 激活后才会创建/暴露给辅助功能 API。
/// 做法：确认目标窗口在前台 → 轻点一次 Alt → 等待菜单弹出 → 用 UIA 读取
/// 已弹出的菜单项（含快捷键文本）→ 无论成败最后按 Esc 还原。
/// 仅在 Win32 与被动 UIA 都读不到时被调用。
/// </summary>
public static class InvasiveScan
{
    private const int VK_MENU = 0x12;
    private const int VK_ESCAPE = 0x1B;

    /// <summary>返回 null = 不适用（窗口不在前台/读不到）；非空列表 = 激活后读到的分组。</summary>
    public static List<ShortcutGroup>? TryRead(IntPtr hwnd, int holdMs = 260)
    {
        // 必须确保 Alt 发给的就是目标窗口
        if (NativeMethods.GetForegroundWindow() != hwnd) return null;

        try
        {
            TapKey(VK_MENU);
            Thread.Sleep(holdMs);

            var groups = ReadOpenMenus();
            return groups is { Count: > 0 } ? groups : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            // 无论结果如何都还原菜单状态（Esc 关闭打开的菜单/取消键提示）
            TapKey(VK_ESCAPE);
            Thread.Sleep(60);
        }
    }

    private static List<ShortcutGroup>? ReadOpenMenus()
    {
        // Alt 后应用弹出的菜单以 ControlType.Menu 的形式出现在根元素下
        var root = AutomationElement.RootElement;
        var menus = root.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
        if (menus is null || menus.Count == 0) return null;

        var groups = new List<ShortcutGroup>();
        int idx = 0;
        foreach (AutomationElement menu in menus)
        {
            string menuName = (menu.Current.Name ?? "").Trim();
            var items = ReadLeafItems(menu);
            if (items.Count == 0) continue;
            groups.Add(new ShortcutGroup(
                menuName.Length > 0 ? menuName : (idx++ == 0 ? "菜单" : $"菜单 {idx}"), items));
        }
        return groups;
    }

    /// <summary>递归收集菜单里的叶子命令项（含快捷键文本）。</summary>
    private static List<ShortcutItem> ReadLeafItems(AutomationElement parent, int depth = 0)
    {
        var list = new List<ShortcutItem>();
        if (depth > 3) return list;

        var children = parent.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
        if (children is null) return list;

        foreach (AutomationElement child in children)
        {
            string raw = child.Current.Name ?? "";
            string keys = "";
            try
            {
                var acc = child.Current.AcceleratorKey;
                if (!string.IsNullOrEmpty(acc)) keys = acc;
            }
            catch { /* 忽略 */ }

            string desc = raw;
            int tab = raw.IndexOf('\t');
            if (tab >= 0)
            {
                desc = raw[..tab].Trim();
                if (keys.Length == 0) keys = raw[(tab + 1)..].Trim();
            }
            if (keys.Length == 0)
            {
                var m = Regex.Match(desc, @"[\t ]{1,3}(?<k>[A-Za-z0-9+ ]{2,20})$");
                if (m.Success && LooksLikeKeys(m.Groups["k"].Value))
                {
                    keys = m.Groups["k"].Value.Trim();
                    desc = desc[..m.Index].Trim();
                }
            }

            // 有子菜单的项：递归展开
            var subItems = ReadLeafItems(child, depth + 1);
            if (subItems.Count > 0)
            {
                list.AddRange(subItems);
                continue;
            }

            desc = Clean(desc);
            if (desc.Length == 0) continue;
            keys = keys.Replace(" ", "");
            if (!LooksLikeKeys(keys)) keys = "";
            list.Add(new ShortcutItem(keys, desc));
        }
        return list;
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace("&", "").Replace("...", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static bool LooksLikeKeys(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return Regex.IsMatch(s,
            @"^(?i)((Ctrl|Alt|Shift|Win)\+)+([A-Z0-9]|F\d{1,2})$|^(?i)(F\d{1,2}|Del|Delete|Esc|Tab|Enter|Home|End)$");
    }

    private static void TapKey(int vk)
    {
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        keybd_event((byte)vk, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
