using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeySheet.Core;

/// <summary>
/// 读取目标进程可执行文件内嵌的「加速键表」(RT_ACCELERATOR)，
/// 得到 命令ID → 快捷键 的真实映射。很多带标准菜单的 Win32 程序
/// （记事本等）的快捷键并不写在菜单文本里，而是靠加速键表实现；
/// 该表能拿到应用程序真正注册的快捷键，比预设/猜测可靠。
/// </summary>
public static class AccelTableReader
{
    /// <summary>返回 cmd -&gt; 显示文本 的映射；失败或没有加速键表返回 null。log 可选，用于诊断各步骤。</summary>
    public static Dictionary<int, string>? LoadForProcess(int pid, Action<string>? log = null)
    {
        log?.Invoke($"[accel] pid={pid}");
        string? exePath = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            exePath = p.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[accel] 取进程模块失败: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        if (string.IsNullOrEmpty(exePath))
        {
            log?.Invoke("[accel] 模块路径为空");
            return null;
        }
        log?.Invoke($"[accel] exe={exePath}");

        IntPtr hMod = NativeMethods.LoadLibraryEx(exePath, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
        int lastErr = Marshal.GetLastWin32Error();
        if (hMod == IntPtr.Zero)
        {
            log?.Invoke($"[accel] LoadLibraryEx(AS_DATAFILE) 失败 err={lastErr}，尝试 AS_DATAFILE|AS_IMAGE");
            hMod = NativeMethods.LoadLibraryEx(exePath, IntPtr.Zero,
                NativeMethods.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LOAD_LIBRARY_AS_IMAGE);
            lastErr = Marshal.GetLastWin32Error();
        }
        if (hMod == IntPtr.Zero)
        {
            log?.Invoke($"[accel] LoadLibraryEx 最终失败 err={lastErr}");
            return null;
        }
        log?.Invoke($"[accel] LoadLibraryEx OK hmod=0x{hMod.ToInt64():X}");

        try
        {
            var result = new Dictionary<int, string>();
            bool enumOk = false;
            NativeMethods.EnumResNameProc cb = (hm, _, name, _) =>
            {
                enumOk = true;
                try
                {
                    IntPtr acc = NativeMethods.LoadAccelerators(hm, name);
                    int err = Marshal.GetLastWin32Error();
                    log?.Invoke($"[accel]   资源 name=0x{name.ToInt64():X} LoadAccelerators={(acc == IntPtr.Zero ? "失败 err=" + err : "OK")}");
                    if (acc != IntPtr.Zero)
                        CollectAccelerators(acc, result, log);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[accel]   回调内异常: {ex}");
                }
                return true;
            };
            bool ret = NativeMethods.EnumResourceNames(hMod, NativeMethods.RT_ACCELERATOR, cb, IntPtr.Zero);
            int enumErr = Marshal.GetLastWin32Error();
            log?.Invoke($"[accel] EnumResourceNames ret={ret} err={enumErr} 找到资源回调={enumOk} 累计 {result.Count} 条");
            return result.Count > 0 ? result : null;
        }
        finally
        {
            NativeMethods.FreeLibrary(hMod);
        }
    }

    private static void CollectAccelerators(IntPtr hAccel, Dictionary<int, string> map, Action<string>? log)
    {
        const int maxEntries = 4096;
        IntPtr buf = Marshal.AllocHGlobal(maxEntries * Marshal.SizeOf<NativeMethods.ACCEL>());
        try
        {
            // 一次拷全表（先 NULL 查询总数在部分实现上不可靠）
            int got = NativeMethods.CopyAcceleratorTable(hAccel, buf, maxEntries);
            log?.Invoke($"[accel]   CopyAcceleratorTable -> {got} 条");
            for (int i = 0; i < got; i++)
            {
                var acc = Marshal.PtrToStructure<NativeMethods.ACCEL>(buf + i * Marshal.SizeOf<NativeMethods.ACCEL>());
                if (acc.cmd == 0) continue;
                string text = ToDisplayText(acc.fVirt, acc.key);
                if (text.Length == 0) continue;
                // 同一个命令可能有多个加速键（少见），只保留第一个
                map.TryAdd(acc.cmd, text);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string ToDisplayText(ushort fVirt, ushort key)
    {
        var parts = new List<string>();
        if ((fVirt & NativeMethods.FCONTROL) != 0) parts.Add("Ctrl");
        if ((fVirt & NativeMethods.FALT) != 0) parts.Add("Alt");
        if ((fVirt & NativeMethods.FSHIFT) != 0) parts.Add("Shift");

        string main;
        if ((fVirt & NativeMethods.FVIRTKEY) != 0)
            main = KeyNames.VkDisplay(key);
        else
            main = key == 0 ? "" : ((char)key).ToString();

        if (main.Length == 0) return "";
        return parts.Count == 0 ? main : string.Join("+", parts) + "+" + main;
    }
}
