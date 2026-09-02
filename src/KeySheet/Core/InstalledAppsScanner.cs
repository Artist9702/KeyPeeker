using Microsoft.Win32;

namespace KeySheet.Core;

/// <summary>一台电脑上已安装的应用（来自注册表卸载项，过滤系统/运行时噪声）。</summary>
public sealed record InstalledApp(string DisplayName, string ProcessName, string ExePath)
{
    public override string ToString() => $"{DisplayName} ({ProcessName}.exe)";
}

/// <summary>扫描本机已安装应用：HKLM/HKCU 的 Uninstall 键（含 32 位 WOW64 项），
/// 取 DisplayIcon/InstallLocation 里的 exe 名作为进程名，按 exe 去重。</summary>
public static class InstalledAppsScanner
{
    private static readonly string[] StopWords =
    {
        "update", "updater", "redistributable", "runtime", "sdk", "service pack",
        "visual c++", "visual studio", "webview", ".net", "driver", "winsxs",
        "toolkit", "windows sdk", "修复", "补丁", "驱动", "环境",
        "microsoft edge update", "msi", "installer", "helper"
    };

    public static List<InstalledApp> Scan()
    {
        var map = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        AddUninstallRoot(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", map);
        AddUninstallRoot(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", map);
        AddUninstallRoot(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", map);
        return map.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddUninstallRoot(RegistryKey hive, string path, Dictionary<string, InstalledApp> map)
    {
        try
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) return;
            foreach (string name in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(name);
                    if (k is null) continue;
                    string display = (k.GetValue("DisplayName") as string ?? "").Trim();
                    if (display.Length == 0) continue;
                    if (StopWords.Any(s => display.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    if (IsTrue(k, "SystemComponent")) continue;

                    string exe = ExtractExe(
                        (k.GetValue("DisplayIcon") as string) ?? "",
                        (k.GetValue("InstallLocation") as string) ?? "",
                        (k.GetValue("DisplayName") as string) ?? "");

                    if (exe.Length == 0) continue;
                    string proc = Path.GetFileNameWithoutExtension(exe);
                    if (proc.Length == 0) continue;
                    // 跳过常见系统进程名
                    if (StopWords.Any(s => proc.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) continue;

                    // 跳过“卸载器 / 维护服务 / 输入法”这类不是用户界面程序的条目
                    string pl = proc.ToLowerInvariant();
                    if (pl.StartsWith("uninst") || pl.StartsWith("unins")
                        || pl.Contains("setup") || pl.Contains("maintenance") || pl.Contains("update"))
                        continue;
                    if (display.Contains("Maintenance Service", StringComparison.OrdinalIgnoreCase)
                        || display.Contains("输入法", StringComparison.Ordinal))
                        continue;

                    var app = new InstalledApp(display, proc, exe);
                    map.TryAdd(proc, app);
                }
                catch { /* 单个键读失败忽略 */ }
            }
        }
        catch { /* 分支不可读忽略 */ }
    }

    private static bool IsTrue(RegistryKey k, string valueName)
    {
        try { return (k.GetValue(valueName) as int?) == 1; }
        catch { return false; }
    }

    /// <summary>从 DisplayIcon / InstallLocation 提取可执行文件路径（带引号/参数时截断）。</summary>
    private static string ExtractExe(string icon, string location, string displayName)
    {
        string s = icon ?? "";
        int i = s.IndexOf(',');
        if (i > 0) s = s[..i];
        s = s.Trim().Trim('"', ' ');
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(s)) return s;
        if (s.Length == 0 && location.Length > 0 && File.Exists(location)) return location;
        // 兜底：常见目录下的可执行文件不细查，跳过（宁可少列不列错）
        return "";
    }
}
