using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KeySheet.Core;

/// <summary>调用 DeepSeek Chat 接口，请模型生成符合预设格式的快捷键 JSON。</summary>
public static class DeepSeekClient
{
    public static string DefaultBaseUrl = "https://api.deepseek.com";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public sealed record Result(bool Ok, string Content, string Error);

    /// <param name="progress">阶段回调（空实现用于无 UI 调用）。</param>
    public static async Task<Result> GeneratePresetAsync(
        string apiKey, string baseUrl, string model,
        string processName, string windowTitle, string userLanguage, Action<string>? progress)
    {
        progress?.Invoke("正在连接 DeepSeek…");
        if (string.IsNullOrWhiteSpace(apiKey))
            return new Result(false, "", "未配置 API Key：请先在 托盘 → 设置 里填写 DeepSeek API Key。");

        var request = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "deepseek-chat" : model.Trim(),
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = BuildSystemPrompt() },
                new { role = "user", content = BuildUserPrompt(processName, windowTitle, userLanguage) }
            }
        };

        try
        {
            var url = (baseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/chat/completions";
            using var msg = new HttpRequestMessage(HttpMethod.Post, url);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            msg.Content = new StringContent(
                JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            progress?.Invoke("正在生成该应用的快捷键清单…");
            using var resp = await Http.SendAsync(msg);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return new Result(false, "", $"DeepSeek 返回 {resp.StatusCode}：{Trim(body, 300)}");
            }

            string? text = ExtractContent(body);
            if (string.IsNullOrWhiteSpace(text))
                return new Result(false, "", "模型没有返回内容，请重试。");
            return new Result(true, text, "");
        }
        catch (Exception ex)
        {
            return new Result(false, "", $"请求失败：{ex.Message}");
        }
    }

    private static string? ExtractContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSystemPrompt() => """
        你是 Windows 快捷键专家。用户会给出一个软件，你需要整理出该软件**官方文档中常见的键盘快捷键**，
        并以严格的 JSON 输出，用于一个叫 KeyPeeker 的快捷键浮层工具。

        硬性要求：
        1. 只输出一个 JSON 对象，不要任何解释、不要 Markdown 代码块围栏（不要 ```json ```）。
        2. JSON 结构必须为：
           {"processName":"进程名","displayName":"显示名","appliesToNote":"版本/范围备注，诚实说明不确定处","groups":[{"name":"分组名","items":[{"keys":"Ctrl+S","description":"保存"}]}]}
        3. "keys" 格式：修饰键 + "+" + 键，例如 Ctrl+S、Ctrl+Shift+N、F5、Alt+F4；修饰键只用 Ctrl/Alt/Shift/Win。
        4. 分组按功能（文件/编辑/视图/导航/窗口/工具…）组织，每组合适的条目数量。
        5. 只列有可靠依据的快捷键；不确定的不要编造，宁缺毋滥；description 用简洁中文。
        6. 条目总数十条到八十条之间。
        """;

    private static string BuildUserPrompt(string processName, string windowTitle, string userLanguage)
        => $$"""
            软件：
            - 进程名：{{processName}}.exe
            - 窗口标题：{{windowTitle}}（标题可能只是打开的文档名，软件身份以进程名为准）
            说明语言：{{userLanguage}}
            请按系统要求输出该软件的快捷键 JSON。
            """;

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
