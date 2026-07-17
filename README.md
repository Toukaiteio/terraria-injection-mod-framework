# TIMF — Terraria Injection Mod Framework

面向 **原版泰拉瑞亚 1.4.5.x** 的客户端注入式模组框架（**不依赖 tModLoader**）。

当前 tModLoader 仍停留在 1.4.4 时，可用 TIMF 在新版客户端上加载简单的 **客户端侧** 模组。首个示例是独立的 **BossCursor**（指向 Boss 的箭头指示）。

> 仅供个人学习 / 单机或私服自用。注入可能被杀软误报，请自行加白。

主菜单左下角游戏版本号上方会显示 **TIMF v1.0.0**（由框架自身绘制，不依赖模组）。

## 架构

```
TIMF.Launcher  →  启动 Terraria.exe 并注入 TIMF.Bootstrap.dll
TIMF.Bootstrap →  在游戏进程内托管 CLR 4.0，调用 TIMF.Core.Loader.Initialize
TIMF.Core      →  加载 Mods\*.dll，订阅 Main.OnPostDraw
BossCursor     →  独立示例模组（不在 Core 内）
```

游戏为 **.NET Framework 4.x / x86 / XNA 4.0**。v1 绘制钩子使用公共事件 `Main.OnPostDraw`，无需 Harmony。

## 目录

| 路径 | 说明 |
|------|------|
| `src/TIMF.Abstractions` | 模组 API（`IMod` 等） |
| `src/TIMF.Core` | 框架核心 |
| `src/TIMF.Launcher` | 启动器 + 注入 |
| `src/TIMF.Bootstrap` | 原生 x86 CLR 宿主 |
| `examples/BossCursor` | 示例模组（独立工程） |
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
- `TIMF.Core.dll` / `TIMF.Abstractions.dll`
- `Mods\BossCursor.dll` + `Mods\Cursor.png`

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

## 编写自己的模组

1. 新建 `net48` / `x86` 类库，引用 `TIMF.Abstractions` 与 `Terraria.exe`。
2. 实现 `IMod`，可选标 `[TimfMod]`。
3. 将 DLL 复制到 `dist\Mods\`。
4. 在 `PostDraw` 中使用 `Main` / `NPC` / `Player` 等原版 API（与 tML 文档概念类似，但无 `ModContent`）。

```csharp
using Microsoft.Xna.Framework;
using TIMF.Abstractions;

[TimfMod]
public sealed class MyMod : IMod
{
    public string Name => "MyMod";
    public string Version => "1.0.0";
    public void Load(IModContext ctx) { ctx.Log.Info("hi"); }
    public void Unload() { }
    public void PostDraw(GameTime gameTime) { /* overlay */ }
}
```

tML 概念对照见计划文档 / 源码注释：`ModSystem.PostDraw*` ≈ `IMod.PostDraw` via `Main.OnPostDraw`。

## 参考

- 游戏版本：1.4.5.6（以本机 `Terraria.exe` 为准）
- tModLoader API 文档（1.4.4，语义参考）：https://docs.tmodloader.net/docs/preview/annotated.html
- 原 BossCursor 模组资源（源码隐藏）：`tModLoader\ModReader\BossCursor`

## 限制（v1）

- 仅客户端叠加层；无服务端模组、无完整配置 UI、无热重载
- 未集成 Harmony（后续可加）
- 游戏大版本更新后需重新验证公共 API
