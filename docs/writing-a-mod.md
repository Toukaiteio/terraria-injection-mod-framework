# 编写一个 TIMF 模组

从零到能跑的最短路径。API 细节见 [api-reference.md](./api-reference.md)，侧别概念见 [side-model.md](./side-model.md)。

> **安全要求：** 模组被加载不等于获得本机权限。不要直接读取 `ModDirectory` / `ContentDirectory` 之外
> 的文件，不要自主写文件，也不要调用 Shell、脚本或 `Process.Start`。这些行为必须经
> `context.Security` 提交精确申请，并在用户授权后由同一个框架代理执行。TIMF 会明确警告：当前同进程
> .NET DLL 仍不是可靠沙箱，直接系统调用无法被完全拦截，因此受支持模组不得绕过代理。
> 加载器会在执行任何模组代码前扫描主 DLL 和私有依赖；直接文件/进程/网络/PInvoke、动态调用、直接
> Harmony 等痕迹会导致整个模组拒载。

## 1. 建工程

两种方式，任选其一。

### 方式 A：Mod SDK 模板（独立开发，推荐）

用发布包内的 **Mod SDK**（`build.ps1` 产出的 `dist\ModSDK\`）一条命令生成骨架，`net48`/`x86`、框架与
Terraria/XNA 引用、构建后自动打包都由 `TIMF.Mod.props` 托底，无需手写引用：

```powershell
setx TIMF_SDK      "<ModSDK 路径>"           # 一次性，重开终端生效
setx TIMF_TERRARIA "<你自备的 Terraria.exe>"  # 一次性，合法游戏副本

dotnet new install <ModSDK>\templates\timf-mod
dotnet new timf-mod -n MyMod --display "My Mod"
cd MyMod
dotnet build -c Release          # 产出可直接投放的 dist\MyMod\
```

生成的 `MyMod` 已含配置、本地化与一个 `IClientMod` 骨架，可直接跳到 [第 4 步](#4-拿服务) 改逻辑。

### 方式 B：仓库内 `mods\`（跟随本仓库一起构建）

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

    // 发布本程序集声明的接口；不可覆盖框架或其他模组服务
    context.ServicePublisher.Publish<IMyModApi>(new MyModApi());

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
| 读取外部文件、写文件或执行进程 | `IModContext.Security` 申请并代理执行 |
| 保存本模组配置 / 读取包内资源 | `IModContext.Storage`（受限目录，无需敏感授权） |
| 反射调用 Terraria 私有方法 | `ITerrariaReflection` |
| 对 Terraria 方法做兼容 patch | `IModContext.Patches`（只允许 prefix/postfix） |
| 发布本模组声明的跨模组接口 | `IModContext.ServicePublisher.Publish<T>()` |
| 依赖别的 mod | `[TimfDependsOn("OtherMod", MinVersion = "1.2.0")]` / `[TimfLoadAfter("OtherMod")]` |

## 6. 本地化

在 mod 目录下建 `Localization\en-US.json`、`Localization\zh-Hans.json`，扁平键值：

```json
{ "Window.Title": "My Mod", "Settings.Hint": "Hello" }
```

用 `context.L.Get(key, fallback)` 取值。回退链是**当前语言 → 语言基 → en-US → en → fallback → 键名**，所以务必保持各语言文件的键集合一致，否则会静默回落。

## 7. 构建与调试

仓库只把以下 10 个模组作为公开示例维护：`BossCursor`、`ContentTestKit`、`CreativeMode`、`HighLight`、
`I-Have-My-Phone-Anyway`、`LootRates`、`LowHealthWarning`、`ModSettingsHub`、`WeatherControl`、
`WorldMapIcons`。它们位于 `examples/`；其他模组不属于公开示例集合。

```powershell
.\build.ps1 Release        # 必须先构建框架，mods 引用其产物
.\build-examples.ps1 Release # 只构建并部署上述公开示例
.\build-mods.ps1 Release   # 编译并部署 mods\* → dist\Mods\<Id>\
```

然后运行 `dist\TIMF.Launcher.exe`。日志在 `dist\logs\`。

按 **F9** 打开 Mod Settings 中心（由 `ModSettingsHub` 提供），可以查看每个 mod 的侧别/协议档、启停状态，并打开其设置页。

进入世界后，`Authority` / `Both` 模组的主启用开关会被锁定；只有纯 `Client` 模组可以继续本地切换。
加入服务器时，服务器未启用的双端/服务端模组会显示“服务器未启用”，框架不会派发其常规钩子，也不会
开放其设置页。这个状态只影响当前会话，不会覆盖用户在主菜单保存的启用偏好。

## 8. 检查清单

- [ ] `context.Client` 用之前判空了吗（专用服上为 null）
- [ ] 动世界状态前查了 `context.Authority.IsAuthoritative` 吗
- [ ] `Unload` 里把注册的钩子都 `Remove` 了吗
- [ ] `IModSettings.BuildSettingsUI` 里没有调 `Begin`/`End` 吧
- [ ] `IMapOverlayHook.OnDrawMap` 里没有调 SpriteBatch 的 `Begin`/`End` 吧
- [ ] 键位 id 用了 `"ModId.Action"` 这种全局唯一格式吗
- [ ] 敏感文件/进程操作是否只走 `context.Security`，并提供了具体、用户可读的用途说明
- [ ] 是否没有直接调用 `File` / `Directory` / `Process` / PInvoke / Harmony / `MethodInfo.Invoke`
- [ ] 确实需要 `Net = Required` 吗（它会踢掉原版玩家）
- [ ] 内容 `InternalName` 是否已经视为永久存档身份，而不是随显示名一起改动
- [ ] 自定义 NPC 奖励、商店和世界状态是否只由主机/服务端决定
- [ ] 是否没有自行保存运行时 Item/Tile/Wall/NPC/Projectile/Buff 数值 ID，而是使用内容键或框架旁挂
- [ ] 自定义 Buff 是否通过 `TimfBuff` 注册，且需要跨存档时保持 `Save=true`
- [ ] 射弹或任务状态是否只在权威端创建/发放，没有由多人客户端自行决定奖励
- [ ] 是否避免了当前尚未开放的世界生成、NPC 自定义状态机、刷怪池和群系音乐/背景改写
