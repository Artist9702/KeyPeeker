# 预设表说明（JSON）

本目录是 **KeyPeeker 的内置预设**，随程序首次运行自动复制到用户目录：

```
%APPDATA%\KeyPeeker\presets\
```

预设仅在 **实时读取失败**（应用没有标准 Win32 菜单）时才会被使用，例如文件资源管理器（Ribbon 界面）。

## 命名规则

文件名 = 进程名 + `.json`，例如：

- 文件资源管理器 → `explorer.json`（进程 explorer.exe）
- 记事本 → `notepad.json`

## 格式

```json
{
  "processName": "进程名（仅作标识）",
  "displayName": "弹窗里显示的应用名",
  "appliesToNote": "适用版本/范围说明",
  "groups": [
    {
      "name": "分组名",
      "items": [
        { "keys": "Ctrl+Shift+F1", "description": "快捷键说明" }
      ]
    }
  ]
}
```

说明：

- `keys`：多个按键用 `+` 连接；大小写不敏感。支持 `Ctrl / Alt / Shift / Win`、
  单键字母数字、`F1`~`F24`、方向键、`Del / Ins / Home / End / PgUp / PgDn / Tab / Enter / Esc / Space` 等。
- `description`：该快捷键做什么。
- 没有快捷键的菜单项可以省略，只写有快捷键的即可。
- 修改后保存，重新触发即可看到新内容，无需重启 KeyPeeker。
