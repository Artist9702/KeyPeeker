namespace KeySheet.Core;

/// <summary>虚拟键码常量与名称互转（用于配置里写 Ctrl/Shift/F1 这类名字）。</summary>
internal static class KeyNames
{
    // 修饰键虚拟键码（Windows 虚拟键）。
    internal const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1,
                       VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3,
                       VK_LMENU = 0xA4, VK_RMENU = 0xA5,           // Alt
                       VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    internal const int VK_ESCAPE = 0x1B, VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_TAB = 0x09,
                       VK_BACK = 0x08, VK_DELETE = 0x2E, VK_INSERT = 0x2D,
                       VK_HOME = 0x24, VK_END = 0x23, VK_PRIOR = 0x21, VK_NEXT = 0x22; // PageUp/PageDown

    /// <summary>修饰键家族：C=Ctrl, A=Alt, S=Shift, W=Win, 其它=0。</summary>
    internal static char Family(int vk) => vk switch
    {
        VK_LCONTROL or VK_RCONTROL => 'C',
        VK_LMENU or VK_RMENU => 'A',
        VK_LSHIFT or VK_RSHIFT => 'S',
        VK_LWIN or VK_RWIN => 'W',
        _ => '\0'
    };

    internal static bool IsModifier(int vk) => Family(vk) != '\0';

    /// <summary>把配置里的修饰键名（如 "Ctrl"、"LeftCtrl"）解析为该家族应监测的两个虚拟键码。</summary>
    internal static (int vk1, int vk2)? ResolveModifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return name.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => (VK_LCONTROL, VK_RCONTROL),
            "alt" => (VK_LMENU, VK_RMENU),
            "shift" => (VK_LSHIFT, VK_RSHIFT),
            "win" or "windows" or "meta" => (VK_LWIN, VK_RWIN),
            "leftctrl" => (VK_LCONTROL, VK_LCONTROL),
            "rightctrl" => (VK_RCONTROL, VK_RCONTROL),
            "leftalt" => (VK_LMENU, VK_LMENU),
            "rightalt" => (VK_RMENU, VK_RMENU),
            "leftshift" => (VK_LSHIFT, VK_LSHIFT),
            "rightshift" => (VK_RSHIFT, VK_RSHIFT),
            "leftwin" => (VK_LWIN, VK_LWIN),
            "rightwin" => (VK_RWIN, VK_RWIN),
            _ => null
        };
    }

    /// <summary>解析单个非修饰主键名（F1、A、0、Esc…）为虚拟键码。</summary>
    internal static int? ResolveKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string s = name.Trim();
        if (s.Length == 1)
        {
            char c = char.ToUpperInvariant(s[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
            return null;
        }
        if (s.Length >= 2 && (s[0] == 'F' || s[0] == 'f') && int.TryParse(s[1..], out int fn) && fn >= 1 && fn <= 24)
            return 0x6F + fn; // VK_F1 = 0x70，故 F(n) = 0x6F + n

        string l = s.ToLowerInvariant();
        return l switch
        {
            "esc" or "escape" => VK_ESCAPE,
            "enter" or "return" => VK_RETURN,
            "space" => VK_SPACE,
            "tab" => VK_TAB,
            "backspace" or "bksp" => VK_BACK,
            "del" or "delete" => VK_DELETE,
            "ins" or "insert" => VK_INSERT,
            "home" => VK_HOME,
            "end" => VK_END,
            "pgup" or "pageup" => VK_PRIOR,
            "pgdn" or "pagedown" => VK_NEXT,
            "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
            "printscreen" or "prtsc" => 0x2C,
            _ => null
        };
    }

    /// <summary>把虚拟键码转成展示名（A、F1、Del、Up…），用于把加速键表翻译成菜单文本。</summary>
    internal static string VkDisplay(int vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x6F}";
        return vk switch
        {
            VK_ESCAPE => "Esc",
            VK_RETURN => "Enter",
            VK_SPACE => "Space",
            VK_TAB => "Tab",
            VK_BACK => "Backspace",
            VK_DELETE => "Del",
            VK_INSERT => "Ins",
            VK_HOME => "Home",
            VK_END => "End",
            VK_PRIOR => "PgUp",
            VK_NEXT => "PgDn",
            0x26 => "Up", 0x28 => "Down", 0x25 => "Left", 0x27 => "Right",
            0x2C => "PrtSc", 0x14 => "CapsLock", 0x90 => "NumLock", 0x91 => "ScrollLock",
            0x6B => "Num+", 0x6D => "Num-", 0x6A => "Num*", 0x6F => "Num/",
            0x60 => "Num0", 0x61 => "Num1", 0x62 => "Num2", 0x63 => "Num3", 0x64 => "Num4",
            0x65 => "Num5", 0x66 => "Num6", 0x67 => "Num7", 0x68 => "Num8", 0x69 => "Num9",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".",
            0xBF => "/", 0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => vk <= 0x30 ? ((char)vk).ToString() : $"VK({vk})"
        };
    }
}
