# 编写一个 TIMF 模组

从零到能跑的最短路径。API 细节见 [api-reference.md](./api-reference.md)，侧别概念见 [side-model.md](./side-model.md)。

> **安全要求：** 模组被加载不等于获得本机权限。不要直接读取 `ModDirectory` / `ContentDirectory` 之外
> 的文件，不要自主写文件，也不要调用 Shell、脚本或 `Process.Start`。这些行为属于敏感权限，必须经
> 框架授权并由框架告知用户；当前版本尚未公开权限服务，因此安全模组应避免这些行为。

## 1. 建工程

在 `mods\<Id>\` 下建一个类库：

- 目标框架 **`net48`**，平台 **`x86`**（游戏是 32 位进程）
- 引用 `TIMF.Abstractions`（取 `src\TIMF.Abstractions\bin\Release\net48\TIMF.Abstractions.dll`）
- 按需引用 `Terraria.exe` 与 `lib\xna` 下的 XNA 程序集

目录名、csproj 名、`[TimfMod(Id=...)]` 保持一致最省事——`build-mods.ps1` 要求每个 mod 目录下恰好一个 `<Name>.csproj`。

## 2. 选侧别

先回答一个问题：**你的逻辑跑在谁身上？**

```
只影响本地玩家的观感与操作（UI、准星、地图图标、自动使用物品）
  └─→ IClientMod

会改变世界状态（掉落、NPC、天气、经济）
  ├─ 改动能被原版封包正常表达 ────────→ IAuthorityMod（默认即可）
  └─ 需要对端也装同样代码才正确 ──────→ IAuthorityMod + [TimfMod(Net = Optional/Required)]

两者都要（例如原版安全的主机逻辑 + 自己的配置界面/overlay）
  └─→ IClientMod + IAuthorityMod
```

> 默认协议档是 `Vanilla`，也就是**不破坏原版兼容**。只有当你确信对端必须装同样代码时才提升到 `Optional`/`Required`——`Required` 会让你的主机踢掉纯原版玩家。

## 3. 最小骨架

### 客户端 mod

```csharp
using Microsoft.Xna.Framework;
using TIMF.Abstractions;

namespace MyMod
{
    [TimfMod(Id = "MyMod", Side = TimfSide.Client)]
    public sealed class MyMod : IClientMod, IModSettings, IPlayerUpdateHook
    {
        private IModContext _ctx;

        public string Name => "My Mod";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;

            // 专用服上 Client 为 null —— 务必判空
            var client = context.Client;
            if (client != null)
                client.PlayerUpdate.Add(this);

            context.Log.Info("MyMod loaded");
        }

        public void Unload()
        {
            _ctx?.Client?.PlayerUpdate.Remove(this);
        }

        public void OnPreUpdate()
        {
            // 每帧在本地玩家 ItemCheck 前触发
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            // 只画控件，不要 Begin/End
            ui.Text(_ctx.L.Get("Settings.Hint", "Hello"));
        }

        public void PostDraw(GameTime gameTime) { }
    }
}
```

### 权威 mod（原版兼容）

```csharp
[TimfMod(Id = "LootBoost")]                      // 不写 Net，默认 Vanilla
public sealed class LootBoostMod : IAuthorityMod, IAuthorityLifecycle
{
    public string Name => "Loot Boost";
    public string Version => "1.0.0";

    public void Load(IModContext context) { }
    public void Unload() { }

    public void OnAuthorityActivate(IModContext context)
    {
        // 激活 ≠ 有权威。握手档 mod 在联机客户端上也会激活。
        if (context.Authority == null || !context.Authority.IsAuthoritative)
        {
            context.Log.Warn("skipped — not authoritative");
            return;
        }
        InstallHooks();
    }

    public void OnAuthorityDeactivate() => UninstallHooks();

    public void PostDraw(GameTime gameTime) { }
}
```

> 纯 `Authority` 侧的 mod 是**延迟加载**的：进入会话（单人/主机/专用服）时才 `Load`，离开时 `Unload`。

### 需要双方同装的权威 mod

```csharp
[TimfMod(Id = "MyRules", Net = TimfNetProfile.Required)]
public sealed class MyRulesMod : IAuthorityMod, IAuthorityLifecycle { /* ... */ }
```

## 4. 拿服务

```csharp
public void Load(IModContext context)
{
    // 侧别作用域服务（首选）
    var ui       = context.Client?.Ui;            // TIMF.UI 未安装时为 null
    var keybinds = context.Client?.Keybinds;
    var weather  = context.Authority.Weather;     // Authority 永不为 null

    // 跨 mod 服务总线
    if (context.Services.TryGetService(out IWeatherService w)) { }

    // 本 mod 的 Localization/*.json
    var title = context.L.Get("Window.Title", "My Mod");
}
```

`IModRegistry` 是例外——Core 在发现阶段之后才注册它，需要**延迟解析**（例如在 `PostDraw` 里），不要在 `Load` 中取。

## 5. 常用能力

| 想做的事 | 用什么 |
|---|---|
| 每帧改本地玩家操作 | `IPlayerUpdateHook` + `IClientServices.PlayerUpdate` |
| 在地图/小地图画东西 | `IMapOverlayHook` + `IClientServices.MapOverlay` |
| 加信息饰品效果 | `IInfoAccessoryHook` + `IClientServices.InfoAccessories` |
| 注册热键（进原版设置界面） | `IKeybindService.Register("MyMod.Toggle", ...)` |
| 提供设置页 | `IModSettings` |
| 改天气 | `IAuthorityServices.Weather` |
| 依赖别的 mod | `[TimfDependsOn("OtherMod", MinVersion = "1.2.0")]` / `[TimfLoadAfter("OtherMod")]` |

## 6. 本地化

在 mod 目录下建 `Localization\en-US.json`、`Localization\zh-Hans.json`，扁平键值：

```json
{ "Window.Title": "My Mod", "Settings.Hint": "Hello" }
```

用 `context.L.Get(key, fallback)` 取值。回退链是**当前语言 → 语言基 → en-US → en → fallback → 键名**，所以务必保持各语言文件的键集合一致，否则会静默回落。

## 7. 构建与调试

```powershell
.\build.ps1 Release        # 必须先构建框架，mods 引用其产物
.\build-mods.ps1 Release   # 编译并部署 mods\* → dist\Mods\<Id>\
```

然后运行 `dist\TIMF.Launcher.exe`。日志在 `dist\logs\`。

按 **F9** 打开 Mod Settings 中心（由 `ModSettingsHub` 提供），可以查看每个 mod 的侧别/协议档、启停状态，并打开其设置页。

## 8. 检查清单

- [ ] `context.Client` 用之前判空了吗（专用服上为 null）
- [ ] 动世界状态前查了 `context.Authority.IsAuthoritative` 吗
- [ ] `Unload` 里把注册的钩子都 `Remove` 了吗
- [ ] `IModSettings.BuildSettingsUI` 里没有调 `Begin`/`End` 吧
- [ ] `IMapOverlayHook.OnDrawMap` 里没有调 SpriteBatch 的 `Begin`/`End` 吧
- [ ] 键位 id 用了 `"ModId.Action"` 这种全局唯一格式吗
- [ ] 确实需要 `Net = Required` 吗（它会踢掉原版玩家）
