# TIMF — Terraria Injection Mod Framework

面向 **原版泰拉瑞亚 1.4.5.x** 的客户端注入式模组框架（**不依赖 tModLoader**）。

当前 tModLoader 仍停留在 1.4.4 时，可用 TIMF 在新版客户端上加载简单的 **客户端侧** 模组。内置示例：

- **BossCursor** — 指向 Boss 的箭头指示
- **HighLight** — 敌对 NPC 红圈高亮；敌对射弹速度预测线
- **LowHealthWarning** — 低血量时屏幕边缘红色 vignette 警告
- **TIMF.UI** — 纯托管即时模式 UI 库模组（其他模组可依赖）
- **ModSettingsHub** — 统一的模组列表 + 设置页面窗口（F9）

> 仅供个人学习 / 单机或私服自用。

主菜单左下角游戏版本号上方会显示 **TIMF v1.0.0**（由框架自身绘制，不依赖模组）。

## 架构

```
TIMF.Launcher  →  启动 Terraria.exe 并注入 TIMF.Bootstrap.dll
TIMF.Bootstrap →  在游戏进程内托管 CLR 4.0，调用 TIMF.Core.Loader.Initialize
TIMF.Core      →  发现/拓扑排序 Mods，服务注册，OnPostDraw + IUiHost 帧循环
TIMF.UI / 示例模组 → 独立 DLL（库模组通过 Services 暴露能力）
```

游戏为 **.NET Framework 4.x / x86 / XNA 4.0**。模组叠加层走公共事件 `Main.OnPostDraw`；框架菜单版本号用 **Harmony** 挂在 `Main.DrawVersionNumber` 上以保证同批绘制。

### UI 框架设计（TIMF.UI）

`TIMF.UI` 是一个 **纯托管即时模式（immediate-mode）** UI 库模组：

- 纯 `net48`、零原生依赖，直接用 XNA `SpriteBatch` 绘制，部署即一个 DLL
- 以 **库模组** 形式发布：在 `Load` 中把 `IImmediateModeUi` / `IUiHost` 注册进 `Services`，其他模组按依赖取用
- 帧循环由 Core 驱动：`IUiHost.NewFrame` → 各模组 `PostDraw`（构建控件）→ `IUiHost.Render`（统一提交）
- 即时模式：控件每帧现声明现绘制，无保留态控件树，状态留在调用方，写法接近 `if (ui.Button(...)) { ... }`
- 定位为轻量配置面板，够用即止（不追求复杂布局系统 / XML 皮肤）

## 目录

| 路径 | 说明 |
|------|------|
| `src/TIMF.Abstractions` | 模组 API（`IMod`、依赖特性、服务/UI 接口） |
| `src/TIMF.Core` | 框架核心（加载器、依赖排序、钩子） |
| `src/TIMF.Launcher` | 启动器 + 注入 |
| `src/TIMF.Bootstrap` | 原生 x86 CLR 宿主 |
| `libs/TIMF.UI` | 即时模式 UI **库模组**（发布到 `Mods/`） |
| `examples/*` | 示例模组（独立工程） |
| `dist/` | `build.ps1` 输出 |

## 构建

### 依赖

- .NET SDK（用于 `net48` 构建）
- 已安装 **XNA 4.0 可再发行组件**（本机 GAC 中有 `Microsoft.Xna.Framework*`）
- 原版游戏 `Terraria.exe`（默认路径可在 `dist/timf.json` 配置）
- **32 位** MinGW-w64（`i686` 的 `g++`）用于编译 Bootstrap

### 命令

```powershell
cd E:\pj2\terraria-injection-mod-framework
.\build.ps1 Release
```

产物在 `dist\`：

- `TIMF.Launcher.exe`
- `TIMF.Bootstrap.dll`
- `TIMF.Core.dll` / `TIMF.Abstractions.dll` / `0Harmony.dll`
- 每个模组一个独立文件夹（DLL 与素材同放其中）：
  - `Mods\BossCursor\BossCursor.dll` + `Mods\BossCursor\Cursor.png`
  - `Mods\HighLight\HighLight.dll`
  - `Mods\LowHealthWarning\LowHealthWarning.dll`
  - `Mods\TIMF.UI\TIMF.UI.dll`
  - `Mods\ModSettingsHub\ModSettingsHub.dll`

## 运行

1. 确认 `dist\TIMF.Bootstrap.dll` 为 **32 位**。
2. 运行：

```powershell
cd dist
.\TIMF.Launcher.exe
# 或指定游戏路径
.\TIMF.Launcher.exe "E:\SteamLibrary\steamapps\common\Terraria\Terraria.exe"
```

3. 查看日志：

- `dist\logs\launcher.log`
- `dist\logs\timf-core.log`
- `%TEMP%\timf-bootstrap.log`
- `dist\logs\mod-BossCursor.log`

## BossCursor 示例

配置文件：`dist\config\BossCursor.json`（首次运行自动生成）

| 字段 | 含义 |
|------|------|
| `Enabled` | 是否显示 |
| `CursorSize` | 缩放 |
| `CursorDistance` | 相对玩家的环绕半径（像素） |
| `HideOnScreen` | Boss 在屏幕内时隐藏箭头 |
| `BlackListPillars` | 不为天界柱绘制 |
| `ToggleKey` | 开关热键（默认 `Insert`；勿用 `F8`，会被原版 Debug 菜单占用） |

## HighLight 示例

配置文件：`dist\config\HighLight.json`（首次运行自动生成）

| 字段 | 含义 |
|------|------|
| `Enabled` | 总开关 |
| `ToggleKey` | 热键（默认 `P`） |
| `CircleR/G/B/A` | 圆与线的基础颜色（0–255） |
| `Opacity` | 透明度 0–1 |
| `CircleScale` | 圆半径倍率 |
| `VelocityLineLengthMultiplier` | 线长 ≈ 速度 × 该系数 |
| `UseMaxScreenLengthForLine` | `true` 时线延伸到屏幕边缘（忽略速度长度） |
| `VelocityLineThicknessMultiplier` | 线粗 ≈ 速度 × 该系数 |
| `MaxVelocityLineThickness` | 线粗上限 |
| `FadeLineEnds` | 线尾渐隐 |
| `DrawEveryNFrames` | 每隔 N 帧绘制（`1`=每帧，原版模组约 `2`） |

行为概要：

- 遍历 `Main.npc`：`active && !friendly && !hide` → 在实体中心画红圈
- 遍历 `Main.projectile`：`active && !friendly && !hide` → 画圈 + 沿 `velocity` 的方向线段（长短/粗细随速度）

## LowHealthWarning 示例

配置文件：`dist\config\LowHealthWarning.json`（首次运行自动生成）

| 字段 | 含义 |
|------|------|
| `Enabled` | 总开关 |
| `ToggleKey` | 热键（默认 `Home`） |
| `ThresholdRatio` | 生命比例 ≤ 此值时开始警告（默认 `0.25`） |
| `FullStrengthRatio` | 生命比例 ≤ 此值时达到最强（默认 `0.08`） |
| `MaxEdgeThickness` | 边缘最大厚度（像素，受分辨率 18% 上限约束） |
| `MaxOpacity` | 最外圈峰值透明度（默认 `0.42`，避免遮挡视野） |
| `PulseSpeed` / `PulseAmount` | 低血时轻微脉动 |
| `GradientBands` | 向内渐隐条带数（越高边缘越柔） |
| `ColorR/G/B` | 警告色 |

设计要点：只画屏幕**四周渐隐边框**，中间区域不铺色；血量越低边越厚/越红。死亡 / 幽灵态 / 主菜单 / 全屏地图不显示。

## 模组依赖

加载流程：

1. **发现** `Mods\<ModId>\<ModId>.dll` 中的 `IMod` / `[TimfMod]`（兼容旧的 `Mods\*.dll` 平铺布局）
2. **校验** 硬依赖是否存在、可选最低版本、循环依赖
3. **拓扑排序** 后按序 `Load`（依赖方先于依赖者）
4. 缺失硬依赖的模组 **跳过加载**，原因写入 `timf-core.log`

声明方式：

```csharp
[TimfMod(Id = "MyMod")]
[TimfDependsOn("TIMF.UI", MinVersion = "1.0.0")]  // 硬依赖
[TimfLoadAfter("BossCursor")]                      // 软排序（缺失不失败）
public sealed class MyMod : IMod { /* ... */ }
```

也可写在特性字符串里：`[TimfMod(Id = "MyMod", Dependencies = "TIMF.UI", LoadAfter = "BossCursor")]`。

跨模组服务：库模组在 `Load` 里 `context.Services.Register<T>(impl)`，消费方 `TryGetService` / `GetService`。

## TIMF.UI（即时模式 UI 库）

- `TIMF.UI` 注册 `IImmediateModeUi` 与 `IUiHost`。
- Core 每帧：`IUiHost.NewFrame` → 各模组 `PostDraw`（可调用 UI API）→ `IUiHost.Render`。
- 直接自建窗口（自绘 HUD / 工具窗）：

```csharp
// 消费 TIMF.UI 自建窗口
[TimfDependsOn("TIMF.UI")]
public sealed class MyUiMod : IMod
{
    IImmediateModeUi _ui;
    bool _open = true;
    public void Load(IModContext ctx) => ctx.Services.TryGetService(out _ui);
    public void PostDraw(GameTime gt)
    {
        if (_ui == null) return;
        if (_ui.Begin("My Panel", ref _open))
        {
            _ui.Text("hello");
            if (_ui.Button("OK")) { /* ... */ }
        }
        _ui.End();
    }
    // ...
}
```

当前控件：`Text` / `TextColored` / `Button` / `Selectable` / `Checkbox` / `SliderFloat` / `InputFloat` / `Separator` / `Spacing` / `SameLine` / 可拖拽/折叠/关闭的窗口标题栏。足够做配置面板；复杂布局/XML 皮肤未做（刻意轻量）。

## 模组设置页（ModSettingsHub + IModSettings）

`ModSettingsHub` 是一个统一的设置中心（**F9** 开关，主菜单也可用）：

- 左侧列出所有已加载模组（`IModRegistry`），标 `⚙` 者带设置页
- 右侧渲染选中模组的设置页

任何模组只要在自己的 `IMod` 类上**再实现 `IModSettings`** 即可获得一页设置，无需依赖 `TIMF.UI`、无需自己开窗口——Hub 会把 `ui` 传进来：

```csharp
[TimfMod]
public sealed class MyMod : IMod, IModSettings
{
    MyConfig _cfg;
    // ... IMod 成员 ...

    public void BuildSettingsUI(IImmediateModeUi ui)
    {
        var dirty = false;
        dirty |= ui.Checkbox("Enabled", ref _cfg.Enabled);
        dirty |= ui.SliderFloat("Strength", ref _cfg.Strength, 0f, 1f);
        if (dirty) _cfg.Save(path);   // 改动即时持久化
        // 注意：不要在这里调用 ui.Begin/ui.End，控件直接挂进 Hub 的窗口
    }
}
```

相关服务：

| 服务 | 由谁注册 | 用途 |
|------|----------|------|
| `IImmediateModeUi` / `IUiHost` | `TIMF.UI` | 即时模式绘制 |
| `IModRegistry`（含 `IModInfo`） | `TIMF.Core`（所有模组加载后） | 枚举已加载模组、取其 `IModSettings` |

内置的 **BossCursor / HighLight / LowHealthWarning** 均已实现 `IModSettings`，在 Hub 里可直接调参并即时保存。


## 编写自己的模组

1. 新建 `net48` / `x86` 类库，引用 `TIMF.Abstractions` 与 `Terraria.exe`。
2. 实现 `IMod`，可选标 `[TimfMod]` / `[TimfDependsOn]`。
3. 每个模组放到 **独立文件夹** `dist\Mods\<ModId>\`，DLL 与素材（贴图、数据）同放其中；库模组如 `TIMF.UI` 亦然。
4. 在 `PostDraw` 中使用原版 API 或 `IImmediateModeUi`；读取素材用 `ctx.ContentDirectory`（默认为模组文件夹，存在 `Content\` 子目录时指向它）。

### 模组打包约定

```
Mods\
  BossCursor\
    BossCursor.dll     ← 入口 DLL（文件名建议与文件夹同名）
    Cursor.png         ← 素材，随 ctx.ContentDirectory 定位
  MyMod\
    MyMod.dll
    ThirdParty.dll     ← 私有依赖 DLL（不会被当作模组扫描，可被自动解析）
    Content\           ← 可选，存在时 ctx.ContentDirectory 指向这里
      icon.png
```

每个子文件夹只把「与文件夹同名的 DLL」（或唯一 DLL）当作模组入口，其余 DLL 视为私有依赖，注入进程时按需解析——因此第三方库 DLL 也不会污染扫描或平铺在 `Mods\` 根。

```csharp
using System.IO;
using Microsoft.Xna.Framework;
using TIMF.Abstractions;

[TimfMod]
public sealed class MyMod : IMod
{
    public string Name => "MyMod";
    public string Version => "1.0.0";
    public void Load(IModContext ctx)
    {
        var iconPath = Path.Combine(ctx.ContentDirectory, "icon.png"); // 自己的文件夹内
        ctx.Log.Info("hi");
    }
    public void Unload() { }
    public void PostDraw(GameTime gameTime) { /* overlay */ }
}
```

tML 概念对照：`ModSystem.PostDraw*` ≈ `IMod.PostDraw` via `Main.OnPostDraw`；库模组 ≈ 带 `Services` 的 soft-framework DLL；`Mods\<ModId>\` 打包 ≈ 每模组独立目录。


## 参考

- 游戏版本：1.4.5.6（以本机 `Terraria.exe` 为准）
- tModLoader API 文档（1.4.4，语义参考）：https://docs.tmodloader.net/docs/preview/annotated.html
- BossCursor 模组资源来自 tmodloader Steam 创意工坊模组 BossCursor

## 限制（v1）

- 仅客户端叠加层；无服务端模组、无热重载
- TIMF.UI 为轻量即时模式，聚焦配置面板，未做复杂布局系统 / 皮肤
- Harmony 仅用于框架菜单版本号；模组侧仍以 `OnPostDraw` 为主
- 游戏大版本更新后需重新验证公共 API
