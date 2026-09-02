using System.Text.Json;

namespace KeySheet.Core;

/// <summary>
/// 个人覆盖层：用户在软件里自定义过的快捷键（与官方/预设默认不同）。
/// 以 {分组|描述} 为条目主键，把默认键位替换为用户真实键位，不影响官方预设文件。
/// 文件：{预设目录}\overrides-{进程名}.json
/// </summary>
public sealed class OverrideStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string PresetsDir { get; }

    public OverrideStore(string presetsDir)
    {
        PresetsDir = presetsDir;
        Directory.CreateDirectory(presetsDir);
    }

    /// <summary>条目主键：分组 + 描述 的组合。</summary>
    public static string ItemKey(string groupName, string description) => $"{groupName}◆{description}";

    public string FilePathFor(string processName)
    {
        string safe = string.Concat(processName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (safe.Length == 0) safe = "app";
        return Path.Combine(PresetsDir, $"overrides-{safe}.json");
    }

    /// <summary>读取某软件的覆盖映射：主键 → 用户自定义键位。文件不存在返回空表。</summary>
    public Dictionary<string, string> Load(string processName)
    {
        string path = FilePathFor(processName);
        try
        {
            if (File.Exists(path))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOpts);
                if (dict is not null) return dict;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[overrides] 读取失败 {path}: {ex.Message}");
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public void Save(string processName, IDictionary<string, string> overrides)
    {
        string path = FilePathFor(processName);
        try
        {
            if (overrides.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            File.WriteAllText(path, JsonSerializer.Serialize(
                new SortedDictionary<string, string>(overrides, StringComparer.Ordinal), JsonOpts));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[overrides] 保存失败 {path}: {ex.Message}");
        }
    }

    public void DeleteFor(string processName)
    {
        try
        {
            string path = FilePathFor(processName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>把预设集合套上覆盖层。无覆盖时返回 null（保持原样）；有覆盖时返回新集合，覆盖项打 Overridden 标记。</summary>
    public ShortcutSet? Apply(string processName, ShortcutSet presetSet)
    {
        var map = Load(processName);
        if (map.Count == 0) return null;

        int changed = 0;
        var groups = new List<ShortcutGroup>();
        foreach (var g in presetSet.Groups)
        {
            var items = new List<ShortcutItem>();
            foreach (var item in g.Items)
            {
                string key = ItemKey(g.Name, item.Description);
                if (map.TryGetValue(key, out string? custom) && !string.IsNullOrWhiteSpace(custom) && custom != item.Keys)
                {
                    items.Add(item with { Keys = custom, Overridden = true });
                    changed++;
                }
                else
                {
                    items.Add(item);
                }
            }
            groups.Add(new ShortcutGroup(g.Name, items));
        }
        if (changed == 0) return null;

        return new ShortcutSet(presetSet.Source, presetSet.AppDisplayName, presetSet.ProcessName,
            presetSet.SourceDetail is { } d ? $"{d}；其中 {changed} 项已按你的自定义键位显示（琥珀色）" : null,
            groups);
    }
}
