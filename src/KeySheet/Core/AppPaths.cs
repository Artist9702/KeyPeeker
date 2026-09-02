namespace KeySheet.Core;

/// <summary>应用数据目录解析：命令行 --datadir &gt; 环境变量 KEY_SHEET_DATA &gt; %APPDATA%\KeySheet。</summary>
public static class AppPaths
{
    public static string DataRoot { get; private set; } = "";

    /// <summary>旧版（KeySheet）数据目录名：仅用于一次性迁移。</summary>
    private const string LegacyRootName = "KeySheet";
    private const string CurrentRootName = "KeyPeeker";

    public static string ConfigFile => Path.Combine(DataRoot, "config.json");
    public static string PresetsDir => Path.Combine(DataRoot, "presets");
    /// <summary>程序集目录下随发布携带的内置预设（首次运行复制到 PresetsDir）。</summary>
    public static string BuiltinPresetsDir => Path.Combine(AppContext.BaseDirectory, "Presets");

    public static void Initialize(string? overrideRoot)
    {
        string root = overrideRoot ?? Environment.GetEnvironmentVariable("KEY_SHEET_DATA");
        if (string.IsNullOrWhiteSpace(root))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string newRoot = Path.Combine(appData, CurrentRootName);
            string legacy = Path.Combine(appData, LegacyRootName);
            // 一次性迁移：KeySheet → KeyPeeker（保留已有预设/Key/配置）
            if (!Directory.Exists(newRoot) && Directory.Exists(legacy))
            {
                try { Directory.Move(legacy, newRoot); }
                catch { /* 迁移失败则仍用旧目录 */ }
            }
            root = Directory.Exists(newRoot) ? newRoot
                : Directory.Exists(legacy) ? legacy
                : newRoot;
        }
        DataRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(PresetsDir);
    }
}
