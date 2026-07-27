# TIMF — Terraria Injection Mod Framework

面向 **原版泰拉瑞亚 1.4.5.x** 的注入式模组框架，**不依赖 tModLoader**。

侧别模型对齐原版自身的设计：**能力接口 → 自动推断侧别 → 侧别作用域服务门闩**，而不是让作者手写枚举去声明。

框架原则是**稳定优先、安全优先**：敏感本机权限默认拒绝，工作区外读取、自主写文件及 Shell/进程执行
必须经框架申请授权，并由框架在执行前向用户或服务器管理员说明目标、用途和授权范围。当前权限代理尚未
作为 public API 发布，因此模组不得直接绕过框架调用这些系统能力。

> 仅供个人学习 / 单机或私服自用。主菜单左下角游戏版本号上方会显示 **TIMF** 版本号（框架自身绘制）。

---

## 文档

| 文档 | 内容 | 适合谁 |
|---|---|---|
| **[编写一个模组](docs/writing-a-mod.md)** | 从建工程到跑起来的最短路径、最小骨架、检查清单 | 第一次写 TIMF mod |
| **[API 参考](docs/api-reference.md)** | `TIMF.Abstractions` 全部公共类型与精确签名 | 写 mod 时随时查 |
| **[侧别与协议模型](docs/side-model.md)** | 两根正交轴、加载/激活规则表、设计沿革 | 想搞懂框架行为 |

新手路径：**编写一个模组** → 遇到具体 API 查 **API 参考** → 对加载时机有疑问看 **侧别与协议模型**。

---

## 快速开始

```powershell
.\build.ps1 Release           # 框架 + TIMF.UI + Bootstrap → dist\
.\build-mods.ps1 Release      # mods\*   → dist\Mods\<Id>\
.\build-examples.ps1 Release  # examples\*（CI / 公开样例）

.\dist\TIMF.Launcher.exe      # 启动
```

`build-mods.ps1` 依赖 `build.ps1` 的产物，**必须先跑前者**。

依赖：.NET SDK、`lib/xna`、`Terraria.exe` 引用、32 位 MinGW（编译 Bootstrap）。路径解析规则见 `Directory.Build.props`，可用环境变量 `TIMF_TERRARIA` 覆盖 Terraria 位置。

游戏内按 **F9** 打开 Mod Settings 中心。日志在 `dist\logs\`。

---

## 架构

```
TIMF.Launcher  →  启动 Terraria.exe 并注入 TIMF.Bootstrap.dll
TIMF.Bootstrap →  在游戏进程内托管 CLR 4.0，调用 TIMF.Core.Loader.Initialize
TIMF.Core      →  发现 / 侧别推断 / 拓扑排序 / 服务注册 / 会话与握手
TIMF.UI        →  IClientMod 库模组，向外暴露 IImmediateModeUi
```

| 路径 | 说明 |
|---|---|
| `src/TIMF.Abstractions` | 公共 API：能力接口、侧别、钩子、服务 |
| `src/TIMF.Core` | 加载器、SideClassifier、会话/握手、Harmony patch |
| `src/TIMF.Launcher` | 启动器 + 注入 |
| `src/TIMF.Bootstrap` | 原生 x86 CLR 宿主 |
| `libs/TIMF.UI` | 即时模式 UI 库（`IClientMod`） |
| `examples/*` | 公开示例源码（最佳实践） |
| `mods/*` | 本地编译部署的模组（gitignore） |
| `dist/` | 构建输出 |

---

## 核心概念速览

TIMF 用**两根正交的轴**描述一个 mod。完整说明见 [侧别与协议模型](docs/side-model.md)。

**能力轴 `TimfSide`** —— 代码属于哪个进程角色，由实现的接口自动推断，镜像原版的 `!Main.dedServ` / `Main.netMode != 1`：

| 实现的接口 | 推断出的 `TimfSide` |
|---|---|
| `IClientMod` | `Client` |
| `IAuthorityMod` | `Authority` |
| 两者都实现 | `Both`（= `Client \| Authority`） |

**协议轴 `TimfNetProfile`** —— 加入的对端是否需要装同样代码，默认 `Vanilla`：

| 值 | 进握手目录 | 原版客户端能加入你的房间吗 |
|---|---|---|
| `Vanilla`（默认） | 否 | **能** |
| `Optional` | 是 | 能（对端没有则不启用） |
| `Required` | 是 | **不能**（会被踢出） |

```csharp
// 客户端显示增强
[TimfMod(Id = "HighLight", Side = TimfSide.Client)]
public sealed class HighLightMod : IClientMod, IModSettings { }

// 原版兼容的主机逻辑（掉落 / 经济 / 天气）—— 默认 Net = Vanilla
[TimfMod(Id = "LootRates")]
public sealed class LootRatesMod : IAuthorityMod, IModSettings, IAuthorityLifecycle { }

// 如需联机双方都安装，可在权威模组上声明 Net = TimfNetProfile.Required。
```

> `[TimfMod(Side = ...)]` 是**断言**而非覆盖：写了就必须与接口推断结果一致，否则加载失败。接口是唯一真相。

**侧别作用域服务**——`IModContext` 按侧别分发能力，用不着的那半边直接给 null 或用运行时门闩挡住：

| 属性 | 专用服 | 联机客户端 | 单人 / 主机 |
|---|---|---|---|
| `context.Client` | **null** | 可用 | 可用 |
| `context.Authority.IsAuthoritative` | `true` | **`false`** | `true` |
| `context.Security` | 无交互时拒绝 | 可申请 | 可申请 |

客户端钩子注册表（`IPlayerUpdateHook` 等）在专用服上 `Add` 会被拒绝并记日志。

敏感文件/进程操作必须先展示精确目标并由用户授权，再由 `context.Security` 代理执行。安全中心支持拒绝、
单次、当前 TIMF 进程、精确持久授权和撤销。当前模组 DLL 仍与游戏运行在同一完全信任进程中，框架无法
可靠拦截绕过代理的 `System.IO`、`Process` 或原生调用；设置中心会持续展示这一隔离边界警告。

作为加载前纵深防御，Core 会扫描模组主 DLL 与私有依赖的 IL/元数据；直接文件、进程、网络、P/Invoke、
动态调用、原生依赖和直接 Harmony 等痕迹会在任何模组构造函数执行前被拒载，并显示在安全中心。普通配置
改走每模组隔离的 `context.Storage`，Terraria 私有方法与 patch 分别走受限反射/patch 代理。静态审计能
阻断常见绕过，但仍不等价于进程级沙箱。

跨模组服务也采用身份绑定发布：普通模组只能通过 `context.ServicePublisher` 发布自己程序集声明的接口，
不能覆盖既有框架/安全/UI 服务；直接调用原始服务注册入口会被预加载审计拒绝。

---

## 示例模组

`examples/` 只保留以下 10 个公开示例，作为当前 API 的最佳实践参考；`build-examples.ps1` 会全量构建它们。

| 示例 | 侧别 / 协议 | 说明 |
|---|---|---|
| BossCursor · HighLight · LowHealthWarning | `Client` | HUD、光照和状态提示等显示增强 |
| WorldMapIcons | `Client` | 地图覆盖层示例（`IMapOverlayHook`） |
| I-Have-My-Phone-Anyway | `Client` | 信息饰品示例（`IInfoAccessoryHook`） |
| CreativeMode | `Client` | 物品生成 |
| ModSettingsHub | `Client` | F9 统一设置中心 |
| ContentTestKit | `Client` | 自定义物品、图格、墙、家具、容器和安全授权测试 |
| **LootRates** | `Authority` / `Vanilla` | 主机掉落与金币倍率，**原版客户端可进房** |
| **WeatherControl** | `Authority` / `Vanilla` | 天气控制与锁定，走 `IWeatherService` |

---

## 许可与声明

仅供学习与私用。请遵守游戏 ToS 与当地法律；公开分发注入工具有风险。
