# TIMF — Terraria Injection Mod Framework

语言： [English](README.md) | **简体中文**

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
| **[编写一个模组](docs/writing-a-mod.zh-CN.md)** | 从建工程到跑起来的最短路径、最小骨架、检查清单 | 第一次写 TIMF mod |
| **[API 参考](docs/api-reference.zh-CN.md)** | `TIMF.Abstractions` 全部公共类型与精确签名 | 写 mod 时随时查 |
| **[侧别与协议模型](docs/side-model.zh-CN.md)** | 两根正交轴、加载/激活规则表、设计沿革 | 想搞懂框架行为 |

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

依赖：.NET SDK、仓库内的 `lib/xna` 引用、`Terraria.exe` 引用、32 位 MinGW（编译 Bootstrap）。仓库有意不包含游戏二进制，也不写入任何机器专属工具链路径。

### 重新 clone 后的本机配置

重新 clone 后，首次构建前需要提供以下仅属于本机的输入：

```powershell
# 编译时的游戏引用：使用你自己拥有的合法 Terraria 安装。
$env:TIMF_TERRARIA = "<你的 Terraria.exe 路径>"

# Bootstrap 编译器：i686 / 32 位 g++。
$env:TIMF_MINGW_GPP = "<i686-g++.exe 路径>"
# 也可以设置 TIMF_MINGW_ROOT，指向包含该编译器的 MinGW/MSYS2 根目录。

.\build.ps1 Release
.\build-mods.ps1 Release
```

编译时使用的 `Terraria.exe` 必须与实际启动的游戏版本一致。目前应使用 1.4.5.7 的游戏程序；如果继续使用 1.4.5.6 等旧引用，虽然可能可以成功编译，但模组调用发生变化的游戏 API 时会在运行阶段失败。

如果要持久保存 Windows 环境变量，可使用 `setx`，然后重新打开终端。启动器使用 `--server` 时，可用可选的 `TIMF_TERRARIA_SERVER` 指定服务器程序。`Directory.Build.props` 和 `sdk/TIMF.Mod.props` 也接受显式 MSBuild 属性；这些文件不再包含固定的 Steam 路径或用户目录。

启动器可以通过第一个命令行参数、`TIMF_TERRARIA`，或本机专用的 `dist\timf.json`（`gamePath` / `serverPath`）定位游戏。`dist\timf.json`、`dist\config\` 和 `dist\logs\` 都是本机状态，不应提交或放入共享压缩包。如果复用了旧的 `dist` 并希望恢复模组首次运行的默认值，需要删除对应的持久化配置；宏伟蓝图+ 对应删除 `dist\config\GrandDesignPlus.json` 和 `dist\config\mod-data\GrandDesignPlus\GrandDesignPlus.json`，再重新启动。

`mods\` 目录同样是有意加入 gitignore 的：其中是本地 / 私有模组源码，重新 clone 后不会自动恢复。请单独保存这些源码或补丁，或者使用仓库内受跟踪的 `examples\` 项目作为公开、可构建的示例。重新 clone 且没有另外恢复 `mods\` 时，`build-mods.ps1` 会提示没有可构建的本地模组。

游戏内按 **F9** 打开 Mod Settings 中心。日志在 `dist\logs\`。

### 独立开发模组（Mod SDK + `dotnet new` 模板）

除了在仓库内 `mods\` 下开发，也可以**完全脱离本仓库**、用发布包内的 **Mod SDK** 开发单个模组。`build.ps1`
会把可分发的 SDK 组装到 `dist\ModSDK\`（含引用程序集、共享构建 props 与 `dotnet new` 模板）：

```powershell
setx TIMF_SDK      "<解压后的 ModSDK 路径>"    # 一次性，重开终端生效
setx TIMF_TERRARIA "<你自备的 Terraria.exe>"   # 合法游戏副本，绝不随 SDK 分发

dotnet new install <ModSDK>\templates\timf-mod
dotnet new timf-mod -n MyMod --display "My Mod"
cd MyMod
dotnet build -c Release        # 产出可直接投放的 dist\MyMod\（dll + 本地化 + 默认配置）
```

生成的骨架是一个含配置与本地化的 `IClientMod`。`net48` / `x86`、框架与 Terraria/XNA 引用、构建后自动
打包都由 SDK 的 `TIMF.Mod.props` 统一提供，模组的 `.csproj` 只需一行 `Import`。编译产物会经加载前安全
审计；把 `dist\MyMod\` 整个目录拷进 TIMF home 的 `Mods\` 即可安装。

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
| `libs/TIMF.Pinyin` | 共享中文拼音搜索库模组（由 `CreativeMode` 等示例复用） |
| `examples/*` | 公开示例源码（最佳实践） |
| `mods/*` | 本地编译部署的模组（gitignore） |
| `dist/` | 构建输出 |

---

## 核心概念速览

TIMF 用**两根正交的轴**描述一个 mod。完整说明见 [侧别与协议模型](docs/side-model.zh-CN.md)。

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
public sealed class HighLightMod : IClientMod, IModSettings, IModFeatureToggle { }

// 原版兼容的主机逻辑（掉落 / 经济 / 天气）—— 默认 Net = Vanilla
[TimfMod(Id = "LootRates")]
public sealed class LootRatesMod : IAuthorityMod, IModSettings, IAuthorityLifecycle, IModFeatureToggle { }

// 如需联机双方都安装，可在权威模组上声明 Net = TimfNetProfile.Required。
```

> `[TimfMod(Side = ...)]` 是**断言**而非覆盖：写了就必须与接口推断结果一致，否则加载失败。接口是唯一真相。

`IModFeatureToggle` 是模组的**世界内功能开关**：它只切换模组自己的配置状态，不会触发加载、卸载或重新打补丁，适合在进入世界后临时停用主要功能。
Mod Settings 中心会在模组实现该接口且当前会话允许操作时显示这个开关；模组主启用开关仍然只在主菜单修改。

模组默认采用世界阶段加载：进入世界时加载，回到主菜单时卸载。只有内容模组、声明
`[TimfMod(LoadBeforeWorld = true)]` 的模组及其硬依赖会在注入后、进入世界前准备好。

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

作为加载前纵深防御，Core 会在**不加载程序集**的前提下扫描模组主 DLL 与私有依赖的 IL/元数据；直接文件、
进程、网络、P/Invoke、动态调用、原生依赖和直接 Harmony 等痕迹会在任何模组构造函数执行前被拒载，并显示
在安全中心。运行期反复抛异常的模组会被看门狗自动禁用（保持驻留但不再派发钩子）。普通配置改走每模组隔离
的 `context.Storage`，Terraria 私有方法与 patch 分别走受限反射/patch 代理。静态审计能阻断常见绕过，但
仍不等价于进程级沙箱。

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
| CreativeMode | `Client` | 物品生成 + 中文/拼音搜索（依赖 `TIMF.Pinyin`） |
| ModSettingsHub | `Client` | F9 统一设置中心：主开关、功能开关和设置页状态 |
| ContentTestKit | `Both` / `Required` | 预世界注册自定义物品、图格、墙、家具、容器、草/群系、射弹、Buff/Debuff、宠物物品、NPC 商店/任务和安全授权测试 |
| **LootRates** | `Authority` / `Vanilla` | 主机掉落与金币倍率，**原版客户端可进房** |
| **WeatherControl** | `Authority` / `Vanilla` | 原生天气、风力、月相和事件控制/锁定，走原版 `WorldData` 同步 |

---

## 许可与声明

- 仅用于**学习研究**与**单机 / 自建私服**的个人使用，请勿用于破坏多人游戏公平或绕过反作弊。
- 需自备合法的 Terraria 客户端；本仓库**不包含也不分发**任何游戏二进制（`Terraria.exe` 已 gitignore）。
- 使用注入技术修改商业游戏须自行遵守游戏 ToS 与当地法律，风险自负。
