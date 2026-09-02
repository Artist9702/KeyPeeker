using System.Text.Json;

namespace KeySheet.Core;

/// <summary>JSON 预设表：每个进程一个 &lt;进程名&gt;.json，手工维护，用户在预设目录可自行增改。</summary>
public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed record PresetFile(
        string ProcessName,
        string DisplayName,
        string? AppliesToNote,
        List<PresetGroup> Groups);

    private sealed record PresetGroup(string Name, List<PresetItem> Items);
    private sealed record PresetItem(string Keys, string Description);

    public string PresetsDir { get; }

    public PresetStore(string presetsDir)
    {
        PresetsDir = presetsDir;
        Directory.CreateDirectory(presetsDir);
    }

    /// <summary>首次运行时把随程序集分发的内置预设复制到用户目录（已存在则跳过，尊重用户修改）。</summary>
    public void SeedBuiltins()
    {
        try
        {
            var builtin = AppPaths.BuiltinPresetsDir;
            if (!Directory.Exists(builtin)) return;
            foreach (var file in Directory.EnumerateFiles(builtin, "*.json"))
            {
                string target = Path.Combine(PresetsDir, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[presets] 内置预设初始化失败: {ex.Message}");
        }
    }

    /// <summary>按进程名读取预设，找不到返回 null。</summary>
    public ShortcutSet? GetForProcess(string processName)
    {
        string path = FindPresetFile(processName);
        if (path is null) return null;
        try
        {
            var p = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), JsonOpts);
            if (p is null) return null;
            var groups = (p.Groups ?? new List<PresetGroup>())
                .Where(g => g is not null && (g.Items?.Count ?? 0) > 0)
                .Select(g => new ShortcutGroup(string.IsNullOrWhiteSpace(g.Name) ? "未分类" : g.Name.Trim(),
                                               g.Items!.Select(i => new ShortcutItem(i.Keys ?? "", i.Description ?? "")).ToList()))
                .ToList();
            return new ShortcutSet(ShortcutSource.Preset,
                                   string.IsNullOrWhiteSpace(p.DisplayName) ? processName : p.DisplayName,
                                   processName,
                                   $"{Path.GetFileName(path)}" + (string.IsNullOrWhiteSpace(p.AppliesToNote) ? "" : $"（{p.AppliesToNote}）"),
                                   groups);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[presets] 解析 {path} 失败: {ex.Message}");
            return null;
        }
    }

    private string? FindPresetFile(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        foreach (var ext in new[] { ".json" })
        {
            string direct = Path.Combine(PresetsDir, processName + ext);
            if (File.Exists(direct)) return direct;
        }
        // 大小写兜底
        foreach (var f in Directory.EnumerateFiles(PresetsDir, "*.json"))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(f), processName, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    /// <summary>为尚未收录的进程生成一个空的预设模板文件（返回其路径）。</summary>
    public string CreateTemplateFor(string processName, string displayName)
    {
        string safe = string.Concat(processName.Where(char.IsLetterOrDigit));
        if (safe.Length == 0) safe = "app";
        string path = Path.Combine(PresetsDir, safe + ".json");
        if (!File.Exists(path))
        {
            string json = $$"""
                {
                  "processName": "{{safe}}",
                  "displayName": "{{displayName}}",
                  "appliesToNote": "改这里：适用版本/范围说明",
                  "groups": [
                    {
                      "name": "示例分组",
                      "items": [
                        { "keys": "Ctrl+Shift+F1", "description": "示例：这条快捷键会在弹窗里显示" }
                      ]
                    }
                  ]
                }
                """;
            File.WriteAllText(path, json);
        }
        return path;
    }

    public IReadOnlyList<string> ListPresets() =>
        Directory.Exists(PresetsDir)
            ? Directory.EnumerateFiles(PresetsDir, "*.json").Select(Path.GetFileName).OrderBy(x => x).ToList()
            : Array.Empty<string>();

    /// <summary>
    /// 安装 AI/外部生成的预设内容：去掉可能的 Markdown 围栏 → 解析校验 → 写盘 → 回读。
    /// 返回 (是否成功, 错误信息或说明, 回读到的 ShortcutSet)。
    /// </summary>
    public (bool Ok, string Message, ShortcutSet? Set) InstallGenerated(string processName, string rawJson)
    {
        string clean = rawJson.Trim();
        // 去掉可能的 ```json ... ``` 围栏
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            int first = clean.IndexOf('\n');
            int last = clean.LastIndexOf("```", StringComparison.Ordinal);
            clean = (first > 0 ? clean[(first + 1)..] : clean).Trim();
            if (last > 0) clean = clean[..last].Trim();
        }

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(clean);
        }
        catch (JsonException ex)
        {
            return (false, $"返回内容不是有效 JSON：{ex.Message}（可重试一次）", null);
        }

        // 校验最少结构
        try
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("groups", out var groups) || groups.GetArrayLength() == 0)
                return (false, "AI 返回内容缺少 groups（没有条目），请重试。", null);
            if (!root.TryGetProperty("processName", out var pn) || string.IsNullOrWhiteSpace(pn.GetString()))
                return (false, "AI 返回内容缺少 processName，请重试。", null);
        }
        catch (Exception ex)
        {
            return (false, $"内容结构异常：{ex.Message}", null);
        }
        finally
        {
            doc?.Dispose();
        }

        // 与目标进程名对齐，写盘
        string safeName = string.Concat(processName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (safeName.Length == 0) safeName = "app";
        string path = Path.Combine(PresetsDir, safeName + ".json");
        try
        {
            File.WriteAllText(path, clean, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return (false, $"写入预设失败：{ex.Message}", null);
        }

        var set = GetForProcess(safeName);
        if (set is null || !set.HasData)
            return (false, "预设已写入但无法解析，请打开文件检查或重试。", null);
        return (true, $"已写入 {Path.GetFileName(path)}（{set.TotalWithKeys} 个快捷键）", set);
    }
}
