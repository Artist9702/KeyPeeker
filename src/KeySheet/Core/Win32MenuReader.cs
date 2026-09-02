using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KeySheet.Core;

/// <summary>
/// 通过 Win32 菜单 API 非侵入地读取窗口真实菜单与快捷键。
/// 快捷键有两个来源，按序合并：
///   1. 菜单文本里自带的快捷键尾巴（部分程序会在菜单文字里写 \tCtrl+S）；
///   2. 程序内嵌「加速键表」(RT_ACCELERATOR) —— 记事本这类经典程序真正
///      注册快捷键的地方，按命令 ID 对应回菜单项。
/// 全程不发送任何输入、不打开目标菜单、不改变目标应用状态。
/// </summary>
public static partial class Win32MenuReader
{
    private sealed class RawItem
    {
        public string Desc = "";
        public string KeysFromText = "";
        public int CmdId = -1;
    }

    [GeneratedRegex(@"[\(（]\s*&?[A-Za-z0-9+ ]*?[\)）]")]
    private static partial Regex MnemonicSuffix();

    /// <summary>返回 null = 该窗口没有标准 Win32 菜单；空列表 = 有菜单但没读出任何项。</summary>
    public static List<ShortcutGroup>? TryRead(IntPtr hWnd)
    {
        IntPtr top = NativeMethods.GetMenu(hWnd);
        if (top == IntPtr.Zero) return null;

        var rawMenus = new List<(string Label, List<RawItem> Items)>();
        int topCount = NativeMethods.GetMenuItemCount(top);
        for (int i = 0; i < topCount; i++)
        {
            string topText = ReadMenuText(top, i);
            if (string.IsNullOrWhiteSpace(topText)) continue; // 分隔线

            IntPtr sub = NativeMethods.GetSubMenu(top, i);
            if (sub == IntPtr.Zero) continue;

            var items = ReadItems(sub);
            if (items.Count == 0) continue;
            rawMenus.Add((CleanLabel(topText), items));
        }
        if (rawMenus.Count == 0) return new List<ShortcutGroup>();

        // 用加速键表补齐"菜单文本没写快捷键"的项
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        var accel = AccelTableReader.LoadForProcess((int)pid);

        var groups = new List<ShortcutGroup>();
        foreach (var (label, items) in rawMenus)
        {
            var final = new List<ShortcutItem>();
            foreach (var item in items)
            {
                string keys = item.KeysFromText;
                if (keys.Length == 0 && accel is not null && item.CmdId > 0 && accel.TryGetValue(item.CmdId, out var fromAccel))
                    keys = fromAccel;
                final.Add(new ShortcutItem(keys, item.Desc));
            }
            groups.Add(new ShortcutGroup(label, final));
        }
        return groups;
    }

    private static List<RawItem> ReadItems(IntPtr hMenu)
    {
        var list = new List<RawItem>();
        int count = NativeMethods.GetMenuItemCount(hMenu);
        for (int i = 0; i < count; i++)
        {
            string raw = ReadMenuText(hMenu, i);
            if (string.IsNullOrWhiteSpace(raw)) continue; // 分隔线

            var item = new RawItem { CmdId = GetCommandId(hMenu, i) };

            // 有些应用的加速键以若干空格与描述分开而不是制表符
            var m = TrailRegex.Match(raw);
            if (m.Success)
            {
                item.Desc = m.Groups["desc"].Value.Trim();
                item.KeysFromText = m.Groups["keys"].Value.Trim();
            }
            else
            {
                int tab = raw.IndexOf('\t');
                item.Desc = (tab >= 0 ? raw[..tab] : raw).Trim();
                item.KeysFromText = tab >= 0 ? raw[(tab + 1)..].Trim() : "";
            }

            item.Desc = CleanLabel(item.Desc);
            if (item.Desc.Length == 0) continue;

            item.KeysFromText = item.KeysFromText.Replace(" ", "").TrimEnd('.');
            if (!IsKeysLike(item.KeysFromText)) item.KeysFromText = "";

            list.Add(item);
        }
        return list;
    }

    private static readonly Regex TrailRegex = new(
        @"^(?<desc>.*?)[\t ]{2,}(?<keys>[A-Za-z0-9 +\-]+)$", RegexOptions.Compiled);

    private static int GetCommandId(IntPtr hMenu, int pos)
    {
        var mii = new NativeMethods.MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MENUITEMINFO>(),
            fMask = NativeMethods.MIIM_ID
        };
        bool ok = NativeMethods.GetMenuItemInfo(hMenu, (uint)pos, true, ref mii);
        return ok ? (int)mii.wID : -1;
    }

    private static string ReadMenuText(IntPtr hMenu, int pos)
    {
        int len = NativeMethods.GetMenuString(hMenu, (uint)pos, IntPtr.Zero, 0, NativeMethods.MF_BYPOSITION);
        if (len <= 0) return "";
        IntPtr buf = Marshal.AllocHGlobal((len + 1) * sizeof(char));
        try
        {
            NativeMethods.GetMenuString(hMenu, (uint)pos, buf, len + 1, NativeMethods.MF_BYPOSITION);
            return Marshal.PtrToStringUni(buf) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string CleanLabel(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace("&", "");
        s = MnemonicSuffix().Replace(s, "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "del", "delete", "ins", "insert", "home", "end",
        "pgup", "pgdn", "pageup", "pagedown", "esc", "escape",
        "tab", "enter", "return", "space", "backspace", "bksp",
        "up", "down", "left", "right", "prtsc", "printscreen",
        "num0","num1","num2","num3","num4","num5","num6","num7","num8","num9",
    };

    private static bool IsKeysLike(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var raw in parts)
        {
            string p = raw.Trim();
            if (p.Length == 0) return false;
            if (p.Length == 1)
            {
                char c = p[0];
                bool letterOrDigit = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                bool punct = ".,;:/\\'`[]-=".IndexOf(c) >= 0;
                if (!letterOrDigit && !punct) return false;
                continue;
            }
            if (p.Length >= 2 && (p[0] == 'F' || p[0] == 'f') && int.TryParse(p[1..], out int fn) && fn >= 1 && fn <= 24)
                continue;
            if (!NamedKeys.Contains(p)) return false;
        }
        return true;
    }
}
