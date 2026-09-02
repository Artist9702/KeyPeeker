using Microsoft.Win32;

namespace KeySheet.Core;

/// <summary>开机自启：写入/删除 HKCU 的 Run 键（用户级，无需管理员权限）。</summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyPeeker";

    private static string ExePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string == ExePath;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按目标状态设置自启，返回是否生效（被系统策略拒绝时为 false）。</summary>
    public static bool SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enable)
                key.SetValue(ValueName, $"\"{ExePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
