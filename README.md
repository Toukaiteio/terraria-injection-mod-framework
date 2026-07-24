# TIMF — Terraria Injection Mod Framework

面向 **原版泰拉瑞亚 1.4.5.x** 的注入式模组框架（**不依赖 tModLoader**）。

侧别模型对齐 Forge/Fabric 思路：**能力接口 → 自动分类 → 侧别服务门闩**，而不是只靠手写枚举。

> 仅供个人学习 / 单机或私服自用。

主菜单左下角游戏版本号上方会显示 **TIMF** 版本号（框架自身绘制）。

## 架构

```
TIMF.Launcher  →  启动 Terraria.exe 并注入 TIMF.Bootstrap.dll
TIMF.Bootstrap →  在游戏进程内托管 CLR 4.0，调用 TIMF.Core.Loader.Initialize
TIMF.Core      →  发现 / 能力推断 Side / 拓扑排序 / 服务注册 / 会话与握手
TIMF.UI        →  IClientMod 库模组，暴露 IImmediateModeUi
```

### 模组能力与侧别（`TimfSide`）

| 能力接口 | 推断 Side | 握手 | 原版客户端进房 |
|----------|-----------|------|----------------|
| `IClientMod`（及客户端钩子） | **Client** | 否 | 无关 |
| `IAuthorityMod` / `IServerMod` | **Server** | 是 | `RequiredOnJoin` 时否 |
| `IClientMod` + `IAuthorityMod` | **Both** | 是 | 同上 |
| **`IVanillaPlugin`** | **Plugin** | **否** | **可以** |

也可在 `[TimfMod(Side=...)]` 显式声明；与能力不一致时 **加载失败**（写进日志）。

```csharp
// 客户端示例
[TimfMod(Id = "AutoHeal", Side = TimfSide.Client)]
public sealed class AutoHealMod : IClientMod, IModSettings, IPlayerUpdateHook { ... }

// 原版兼容主机插件（掉落/经济）
[TimfMod(Id = "LootRates")]
public sealed class LootRatesMod : IVanillaPlugin, IModSettings, IServerMod { ... }

// 需联机双方都装的权威逻辑
[TimfMod(Id = "TimfServerProbe", Side = TimfSide.Server, RequiredOnJoin = true)]
public sealed class TimfServerProbeMod : IAuthorityMod, IServerMod { ... }
```

### 侧别服务（`IModContext`）

```csharp
void Load(IModContext context)
{
    // 专用服上为 null —— 务必判空
    var client = context.Client;
    if (client != null)
    {
        client.PlayerUpdate.Add(this);   // 本地玩家钩子
        var ui = client.Ui;              // TIMF.UI
        var kb = client.Keybinds;
    }

    // 永不为 null；动世界逻辑前检查权威
    if (context.Authority.IsAuthoritative)
    {
        // SP / Host / Dedicated only
    }
}
```

| 属性 | 专用服 | 联机客户端 | SP / Host |
|------|--------|------------|-----------|
| `context.Client` | **null** | 可用 | 可用 |
| `context.Authority.IsAuthoritative` | true | **false** | true |

客户端钩子注册表（`IPlayerUpdateHook` 等）在专用服上 `Add` 会被拒绝。

### Plugin vs Server

- **Plugin**（`IVanillaPlugin`）：只在权威进程跑；不进 TIMF 握手；永不踢原版。适合掉落倍率、金币倍率等。
- **Server / Both**：进握手目录；`RequiredOnJoin` 默认真；可要求进房者安装 TIMF + 对应模组。

## 目录

| 路径 | 说明 |
|------|------|
| `src/TIMF.Abstractions` | 公共 API：能力接口、侧别、钩子、服务 |
| `src/TIMF.Core` | 加载器、SideClassifier、会话/握手、Harmony |
| `src/TIMF.Launcher` | 启动器 + 注入 |
| `src/TIMF.Bootstrap` | 原生 x86 CLR 宿主 |
| `libs/TIMF.UI` | 即时模式 UI **IClientMod** 库 |
| `mods/*` | 本地编译部署的模组（gitignore） |
| `examples/*` | 公开示例源码（最佳实践） |
| `dist/` | 构建输出 |

## 构建

```powershell
cd E:\pj2\terraria-injection-mod-framework
.\build.ps1 Release           # 框架 + TIMF.UI + Bootstrap → dist\
.\build-mods.ps1 Release      # mods\*
.\build-examples.ps1 Release  # examples\*（CI / 公开样例）
```

依赖：.NET SDK、`lib/xna`、`Terraria.exe` 引用、32 位 MinGW（Bootstrap）。详见 `Directory.Build.props`。

### 新增模组（推荐写法）

1. `mods\<Id>\` 建 `net48` / `x86` 类库。
2. 引用 `TIMF.Abstractions` + Terraria + XNA。
3. 实现 **能力接口**（`IClientMod` / `IAuthorityMod` / `IVanillaPlugin`）。
4. 通过 `context.Client` / `context.Authority` 取服务。
5. `.\build-mods.ps1` 自动发现部署。

## 示例模组（`examples/`）

| 示例 | 能力 | 说明 |
|------|------|------|
| AutoHeal / AutoAim / … | `IClientMod` | 客户端 QoL |
| ModSettingsHub | `IClientMod` | F9 统一设置 |
| TIMF.UI | `IClientMod` | UI 库 |
| **LootRates** | **`IVanillaPlugin`** | 主机掉落/金币倍率，原版可进 |
| TimfServerProbe | `IAuthorityMod` | 握手 Server 探针 |

## 许可与声明

仅供学习与私用。请遵守游戏 ToS 与当地法律；公开分发注入工具有风险。
