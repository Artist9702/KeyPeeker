namespace KeySheet.Core;

/// <summary>快捷键数据来源标记。</summary>
public enum ShortcutSource
{
    None = 0,          // 没有任何数据
    RealMenu = 1,      // 通过 Win32 菜单 API 实时读取（真实）
    Preset = 2         // 回退到本地 JSON 预设表（手工维护）
}

/// <summary>一条快捷键。Keys 可能为空（表示该菜单项没有快捷键）。Overridden=true 表示按键被用户自定义覆盖。</summary>
public sealed record ShortcutItem(string Keys, string Description, bool Overridden = false);

/// <summary>一组快捷键（对应一个菜单/一个分类）。</summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutItem> Items)
{
    public int Count => Items.Count;
}

/// <summary>针对某个前台窗口收集到的完整快捷键集合。</summary>
public sealed class ShortcutSet
{
    public ShortcutSet(ShortcutSource source, string appDisplayName, string processName,
                       string? sourceDetail, IReadOnlyList<ShortcutGroup> groups)
    {
        Source = source;
        AppDisplayName = appDisplayName;
        ProcessName = processName;
        SourceDetail = sourceDetail;
        Groups = groups;
    }

    public ShortcutSource Source { get; }
    public string AppDisplayName { get; }
    public string ProcessName { get; }
    /// <summary>来源补充说明，例如预设文件名、菜单句柄是否缺失等。</summary>
    public string? SourceDetail { get; }
    public IReadOnlyList<ShortcutGroup> Groups { get; }

    public int TotalItems => Groups.Sum(g => g.Count);
    public int TotalWithKeys => Groups.Sum(g => g.Items.Count(i => !string.IsNullOrWhiteSpace(i.Keys)));

    public bool HasData => TotalItems > 0;
}
