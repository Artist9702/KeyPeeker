using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeySheet.Core;

/// <summary>前台窗口信息。</summary>
public sealed record ForegroundInfo(
    IntPtr Hwnd,
    uint Pid,
    string ProcessName,
    string WindowTitle);

/// <summary>
/// 聚合器：为前台窗口取快捷键。
/// 策略 = 先用 Win32 菜单实时读取（真实），完全读不到时回退到按进程名匹配的 JSON 预设。
/// </summary>
public sealed class ShortcutAggregator
{
    private readonly PresetStore _presets;
    private readonly OverrideStore? _overrides;
    private readonly bool _allowInvasive;

    public ShortcutAggregator(PresetStore presets, bool allowInvasive = false, OverrideStore? overrides = null)
    {
        _presets = presets;
        _allowInvasive = allowInvasive;
        _overrides = overrides;
    }

    /// <summary>取得当前前台窗口信息；前台是我们自己或没有可见窗口时返回 null。</summary>
    public static ForegroundInfo? GetForeground()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        string processName;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            processName = p.ProcessName;
        }
        catch
        {
            processName = pid.ToString();
        }

        int titleLen = NativeMethods.GetWindowTextLength(hwnd);
        string title = "";
        IntPtr buf = Marshal.AllocHGlobal((titleLen + 1) * sizeof(char));
        try
        {
            NativeMethods.GetWindowText(hwnd, buf, titleLen + 1);
            title = Marshal.PtrToStringUni(buf) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return new ForegroundInfo(hwnd, pid, processName, title);
    }

    /// <summary>收集快捷键集合。</summary>
    public ShortcutSet Collect(ForegroundInfo info)
    {
        string displayName = string.IsNullOrWhiteSpace(info.WindowTitle) ? info.ProcessName : info.WindowTitle;

        // 1) 真实菜单读取：Win32 菜单 + 加速键表
        List<ShortcutGroup>? win32 = null;
        try
        {
            win32 = Win32MenuReader.TryRead(info.Hwnd);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[read] 菜单读取异常: {ex.Message}");
        }
        int winKeys = win32 is null ? 0 : win32.Sum(g => g.Items.Count(i => !string.IsNullOrWhiteSpace(i.Keys)));
        if (winKeys > 0)
        {
            return new ShortcutSet(ShortcutSource.RealMenu, displayName, info.ProcessName,
                                   $"通过 Win32 菜单实时读取（真实，共 {winKeys} 个快捷键）", win32!);
        }

        // 1b) UI Automation 实时扫描（Win32 拿不到时尝试）
        List<ShortcutGroup>? uia = null;
        try
        {
            uia = UiaMenuReader.TryRead(info.Hwnd);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[read] UIA 读取异常: {ex.Message}");
        }
        int uiaKeys = uia is null ? 0 : uia.Sum(g => g.Items.Count(i => !string.IsNullOrWhiteSpace(i.Keys)));
        if (uiaKeys > 0)
        {
            return new ShortcutSet(ShortcutSource.RealMenu, displayName, info.ProcessName,
                                   $"通过 UI Automation 实时读取（真实，共 {uiaKeys} 个快捷键）", uia);
        }

        // 1c) 侵入式激活扫描（可选，默认开启）：轻点 Alt 激活菜单后读取
        List<ShortcutGroup>? invasive = null;
        if (_allowInvasive && NativeMethods.GetForegroundWindow() == info.Hwnd)
        {
            try
            {
                invasive = InvasiveScan.TryRead(info.Hwnd);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[read] 激活扫描异常: {ex.Message}");
            }
        }
        int invKeys = invasive is null ? 0 : invasive.Sum(g => g.Items.Count(i => !string.IsNullOrWhiteSpace(i.Keys)));
        if (invKeys > 0)
        {
            return new ShortcutSet(ShortcutSource.RealMenu, displayName, info.ProcessName,
                                   $"通过菜单激活扫描实时读取（真实，共 {invKeys} 个快捷键）", invasive);
        }

        // 2) 预设回退
        var preset = _presets.GetForProcess(info.ProcessName);
        if (preset is not null && preset.HasData)
        {
            // 2b) 套上个人覆盖层（用户自定义过的键位优先显示）
            if (_overrides is not null)
            {
                var applied = _overrides.Apply(info.ProcessName, preset);
                if (applied is not null) return applied;
            }
            return preset;
        }

        // 3) 无数据
        var why = (win32 is null && uia is null && invasive is null)
            ? "该窗口没有标准 Win32 菜单"
            : "菜单里没有可读快捷键";
        var none = new ShortcutSet(ShortcutSource.None, displayName, info.ProcessName,
                                   $"{why}，且未收录预设 {info.ProcessName}.json", Array.Empty<ShortcutGroup>());
        return none;
    }
}
