using System.Text;
using KeySheet.Core;

namespace KeySheet;

/// <summary>
/// 命令行诊断模式：--diag [进程名] —— 在不弹出 GUI 的情况下，
/// 打印前台窗口（或指定进程的主窗口）能被读到的快捷键，用于开发验证。
/// 报告同时写入 {数据目录}\diag-last.txt，方便在无控制台的启动方式下取回结果。
/// </summary>
public static class DiagRunner
{
    public static int Run(string[] args, string presetsDir)
    {
        var store = new PresetStore(presetsDir);
        var overrides = new OverrideStore(presetsDir);
        bool allowInvasive = args.Contains("--invasive");
        var aggregator = new ShortcutAggregator(store, allowInvasive, overrides);
        var sb = new StringBuilder();
        bool dumpAccel = args.Contains("--accel");

        // --apps：输出本机扫描到的已装软件清单
        if (args.Contains("--apps"))
        {
            var apps = InstalledAppsScanner.Scan();
            sb.AppendLine($"本机扫描到 {apps.Count} 个软件：");
            sb.AppendLine();
            foreach (var a in apps)
                sb.AppendLine($"  {a.DisplayName,-32} -> {a.ProcessName}.exe");
            WriteReport(presetsDir, sb.ToString());
            return 0;
        }

        string? target = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--diag" && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                target = args[i + 1];
        }

        ForegroundInfo? info = null;
        if (target is null)
        {
            info = ShortcutAggregator.GetForeground();
            sb.AppendLine("== 目标：当前前台窗口 ==");
        }
        else
        {
            info = FindByProcessName(target);
            sb.AppendLine($"== 目标：进程 {target} ==");
        }

        int exitCode;
        if (info is null)
        {
            sb.AppendLine("[错误] 找不到目标窗口（前台无可见窗口 / 指定进程未运行且没有主窗口）。");
            exitCode = 1;
        }
        else
        {
            sb.AppendLine($"窗口标题 : {info.WindowTitle}");
            sb.AppendLine($"进程名   : {info.ProcessName} (pid={info.Pid})");
            sb.AppendLine($"句柄     : 0x{info.Hwnd.ToInt64():X}");
            sb.AppendLine();

            if (dumpAccel)
            {
                var accelMap = AccelTableReader.LoadForProcess((int)info.Pid, msg => sb.AppendLine(msg));
                sb.AppendLine($"加速键表 : {(accelMap is null ? "无/读取失败" : accelMap.Count + " 条")}");
                if (accelMap is not null)
                {
                    foreach (var kv in accelMap.OrderBy(k => k.Key).Take(80))
                        sb.AppendLine($"   cmd=0x{kv.Key:X4}  ->  {kv.Value}");
                }
                sb.AppendLine();
            }

            if (allowInvasive)
            {
                // 侵入式扫描要求目标在前台；尽力而为（受系统前台锁限制，可能失败）
                if (NativeMethods.GetForegroundWindow() != info.Hwnd)
                    NativeMethods.SetForegroundWindow(info.Hwnd);
                Thread.Sleep(200);
                sb.AppendLine($"前台确认: {(NativeMethods.GetForegroundWindow() == info.Hwnd ? "是（将发送 Alt）" : "否（跳过侵入扫描）")}");
            }

            var set = aggregator.Collect(info);
            sb.AppendLine($"来源     : {SourceText(set)}");
            if (set.SourceDetail is { Length: > 0 }) sb.AppendLine($"说明     : {set.SourceDetail}");
            sb.AppendLine($"统计     : 共 {set.TotalItems} 个菜单项，其中 {set.TotalWithKeys} 个带快捷键");
            sb.AppendLine();

            if (!set.HasData)
            {
                sb.AppendLine("[无数据] " + (set.SourceDetail ?? ""));
            }
            else
            {
                foreach (var g in set.Groups)
                {
                    sb.AppendLine($"── {g.Name} ──");
                    foreach (var item in g.Items)
                    {
                        string keys = string.IsNullOrWhiteSpace(item.Keys) ? "(无快捷键)" : item.Keys;
                        sb.AppendLine($"   {keys,-20} {item.Description}");
                    }
                    sb.AppendLine();
                }
            }

            if (args.Contains("--uia"))
            {
                sb.AppendLine("======== UIA 扫描层原始结果 ========");
                var uia = UiaMenuReader.TryRead(info.Hwnd);
                if (uia is null)
                {
                    sb.AppendLine("[UIA] 无可用菜单结构");
                }
                else
                {
                    foreach (var g in uia)
                    {
                        sb.AppendLine($"── {g.Name} ──");
                        foreach (var item in g.Items)
                        {
                            string keys = string.IsNullOrWhiteSpace(item.Keys) ? "(无快捷键)" : item.Keys;
                            sb.AppendLine($"   {keys,-20} {item.Description}");
                        }
                        sb.AppendLine();
                    }
                }
            }
            exitCode = 0;
        }

        // 落盘（GUI 子系统进程在无控制台环境下 Console 不可见，文件是最可靠的取回方式）
        string reportPath = Path.Combine(Path.GetDirectoryName(presetsDir) ?? ".", "diag-last.txt");
        try { File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8); }
        catch { /* 忽略写盘失败 */ }
        TryAttachConsole();
        Console.Write(sb);
        return exitCode;
    }

    private static void WriteReport(string presetsDir, string text)
    {
        string reportPath = Path.Combine(Path.GetDirectoryName(presetsDir) ?? ".", "diag-last.txt");
        try { File.WriteAllText(reportPath, text, Encoding.UTF8); }
        catch { /* 忽略写盘失败 */ }
        TryAttachConsole();
        Console.Write(text);
    }

    private static ForegroundInfo? FindByProcessName(string name)
    {
        var procName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(procName))
        {
            IntPtr hwnd = p.MainWindowHandle;
            if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
            {
                return new ForegroundInfo(hwnd, (uint)p.Id, p.ProcessName, p.MainWindowTitle);
            }
        }
        return null;
    }

    private static string SourceText(ShortcutSet set) => set.Source switch
    {
        ShortcutSource.RealMenu => "实时读取（真实 Win32 菜单）",
        ShortcutSource.Preset => "内置预设（手工维护 JSON）",
        _ => "无数据"
    };

    private static void TryAttachConsole()
    {
        if (!NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS)) return;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);
        }
        catch
        {
            // 输出重定向失败则静默
        }
    }
}
