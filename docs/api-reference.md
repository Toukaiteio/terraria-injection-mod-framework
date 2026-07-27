# TIMF API 参考

`TIMF.Abstractions` 的完整公共 API，以及独立程序集 `TIMF.Content` 提供的自定义内容 API。
除第 12 节外，所有类型都在 `TIMF.Abstractions` 命名空间下；内容类型位于 `TIMF.Content`。

TIMF 把稳定性放在功能数量之前：公开 API、内容身份和原版存档兼容性均视为框架契约。无法安全完成的
内容激活应记录明确错误并停止该内容管线，而不是带着部分扩容或半注册状态继续运行。

TIMF 同时坚持安全优先：模组不得因为已被加载就自动获得宿主用户的文件系统或命令执行权限。工作区外
读取、自主写文件和 Shell/进程执行属于敏感行为，必须先通过框架申请授权，并由框架把申请内容告知用户。

签名中出现的外部类型来自 `Microsoft.Xna.Framework`（`GameTime` / `Color` / `Vector2` / `Rectangle`）与 `Microsoft.Xna.Framework.Input`（`Keys`）。

## 目录

- [1. 入口与能力标记](#1-入口与能力标记)
- [2. 上下文与侧别服务](#2-上下文与侧别服务)
- [3. 侧别与协议枚举](#3-侧别与协议枚举)
- [4. 特性（Attribute）](#4-特性attribute)
- [5. UI](#5-ui)
- [6. 客户端钩子](#6-客户端钩子)
- [7. 键位](#7-键位)
- [8. 本地化](#8-本地化)
- [9. 注册表、会话、日志](#9-注册表会话日志)
- [10. 天气](#10-天气)
- [11. 前缀](#11-前缀)
- [12. 自定义内容](#12-自定义内容)
- [13. 安全与敏感权限](#13-安全与敏感权限)

---

## 1. 入口与能力标记

### `IMod`

mod 入口。在你的 DLL 中实现一个 public 类。

```csharp
string Name { get; }      // 显示名，同时是默认的依赖 id
string Version { get; }
void Load(IModContext context);
void Unload();
void PostDraw(GameTime gameTime);
```

- `PostDraw` 每帧在游戏绘制完成后调用（`Main.OnPostDraw`）。**专用服上不会触发**，建议只在 `IClientMod` 上实现。

### `IClientMod : IMod` · `IAuthorityMod : IMod`

纯标记接口，无成员。加载器据此推断 [`TimfSide`](#timfside)：

| 实现 | 推断结果 |
|---|---|
| `IClientMod` | `TimfSide.Client` |
| `IAuthorityMod` | `TimfSide.Authority` |
| 两者都实现 | `TimfSide.Both` |
| 都不实现（裸 `IMod`） | `TimfSide.Client` |

`IAuthorityMod` **默认保持原版兼容**——不进握手目录，纯原版客户端仍可加入你作主机的房间。需要对端也装才能工作时，用 `[TimfMod(Net = ...)]` 显式提升。

### `IAuthorityLifecycle`

权威侧的可选生命周期回调。

```csharp
void OnAuthorityActivate(IModContext context);
void OnAuthorityDeactivate();
```

> **这是生命周期接口，不是能力标记。** 单独实现它**不会**让 mod 获得权威能力，也**不影响** `TimfSide` 推断。能力只由 `IAuthorityMod` 声明。

> **「被激活」不等于「有权威」。** 对 `Optional`/`Required` 协议档的 mod，握手成功后它也会在联机客户端上激活，此时 `IAuthorityServices.IsAuthoritative` 为 **false**。世界写入必须用 `IsAuthoritative` 把关，而不是靠这个回调。

只需要 `Load`/`Unload` 的纯延迟加载 mod 不必实现本接口。

---

## 2. 上下文与侧别服务

### `IModContext`

```csharp
ILogger Log { get; }
string HomeDirectory { get; }      // TIMF 根目录（logs / config / mods）
string ConfigDirectory { get; }    // Home/config，跨 mod 共享
string ModDirectory { get; }       // 本 mod 程序集所在目录
string ContentDirectory { get; }   // 资源目录；默认 = ModDirectory，若存在 ModDirectory/Content 则取后者
string ModAssemblyPath { get; }
IServiceRegistry Services { get; }
IModLocalization L { get; }        // 本 mod 的 Localization/*.json
IClientServices Client { get; }
IAuthorityServices Authority { get; }
Security.ISensitiveOperationService Security { get; }
Storage.IModStorage Storage { get; }
Security.IModPatchService Patches { get; }
```

| 属性 | 专用服 | 联机客户端 | 单人 / 主机 |
|---|---|---|---|
| `Client` | **null** | 可用 | 可用 |
| `Authority` | 非 null | 非 null | 非 null |
| `Authority.IsAuthoritative` | `true` | **`false`** | `true` |

> `Client` 在专用服上为 null，**必须判空**。`Authority` 永不为 null，但动世界状态前要先查 `IsAuthoritative`。
> `Security` 是按模组身份绑定的敏感操作代理；不要从共享服务总线模拟其他模组的申请。
> `Storage` 只允许本模组自己的配置文件和包内只读资源；`Patches` 是受限 Terraria patch 代理。

### `IClientServices`

客户端进程服务。专用服上整体为 null。

```csharp
IImmediateModeUi Ui { get; }                        // 来自 TIMF.UI 库；若未安装则为 null
IKeybindService Keybinds { get; }
IPlayerUpdateHookRegistry PlayerUpdate { get; }
IMapOverlayHookRegistry MapOverlay { get; }
IInfoAccessoryHookRegistry InfoAccessories { get; }
```

### `IAuthorityServices`

```csharp
bool IsAuthoritative { get; }      // 本进程是否拥有世界模拟权（单人 / 主机 / 专用服；联机客户端为 false）
IWeatherService Weather { get; }
IPrefixService Prefix { get; }
```

---

## 3. 侧别与协议枚举

TIMF 用**两根正交的轴**描述一个 mod。详细设计理由见 [side-model.md](./side-model.md)。

### `TimfSide`

能力轴——代码属于哪个 Terraria 进程角色。镜像原版自身分支的两个事实。

```csharp
[Flags]
public enum TimfSide
{
    None      = 0,        // 未声明能力，对已加载的 mod 永远非法
    Client    = 1 << 0,   // 对应 !Main.dedServ  —— 有本地玩家可绘制 / 读输入
    Authority = 1 << 1,   // 对应 Main.netMode != 1 —— 本进程拥有世界模拟权
    Both      = Client | Authority,
}
```

> `Authority` 的含义是「这段代码是世界逻辑」，**不是**「这个进程是服务器」——正如原版把世界模拟代码也编进客户端二进制，只在运行时门控。当前进程能否真的写入，问 `IAuthorityServices.IsAuthoritative`。

```csharp
public static class TimfSides
{
    public static bool IsClientCapable(TimfSide side);
    public static bool IsAuthorityCapable(TimfSide side);
    public static bool IsDeferredAuthority(TimfSide side);  // side == Authority
}
```

- `IsDeferredAuthority`：纯权威 mod 在会话授予权威前无事可做，因此加载器**推迟其程序集加载**到激活时，并在停用时卸载。带客户端半边的 mod 则启动即加载。

### `TimfNetProfile`

协议轴——加入的对端是否需要装同样的代码。原版**没有**对应概念，纯属 TIMF 层。三个值构成严格性阶梯 `Vanilla < Optional < Required`。

```csharp
public enum TimfNetProfile
{
    Vanilla  = 0,  // 不进握手目录；纯原版客户端可加入。客户端 mod 也是这个值（没有权威半边）
    Optional = 1,  // 进握手目录，对端也有时启用；缺失不踢
    Required = 2,  // 进握手目录；主机踢掉缺少此 mod 或版本过低的对端
}

public static class TimfNetProfiles
{
    public static bool ParticipatesInHandshake(TimfNetProfile p);  // p >= Optional
    public static bool RequiresPeer(TimfNetProfile p);             // p == Required
    public static bool IsVanillaHostCompatible(TimfNetProfile p);  // p == Vanilla
}
```

---

## 4. 特性（Attribute）

### `TimfModAttribute`

`[AttributeUsage(Class, Inherited = false, AllowMultiple = false)]`，可选。不写时，第一个 public 非抽象 `IMod` 即入口，侧别全靠接口推断。

```csharp
public string Id { get; set; }              // 稳定 id，用于依赖引用；留空则取 IMod.Name
public string Dependencies { get; set; }    // 逗号分隔的硬依赖 id（等价于多个 [TimfDependsOn]）
public string LoadAfter { get; set; }       // 逗号分隔的软排序 id（等价于 [TimfLoadAfter]）
public TimfSide Side { get; set; }
public bool SideSpecified { get; }          // Side 被赋值过则为 true
public TimfNetProfile Net { get; set; }     // 默认 TimfNetProfile.Vanilla
```

> **`Side` 是断言，不是覆盖。** 写了就必须与接口推断结果**完全一致**，否则加载失败。它的作用是自文档化 + 防止接口漂移，不能用来「声明」接口没实现的能力。
>
> `Net` 若非 `Vanilla`，则要求 mod 具备 `Authority` 半边，否则加载失败——没有权威逻辑就没有可协商的东西。

### `TimfHookAttribute`

标注在**钩子接口**上，声明允许注册它的进程角色。钩子注册表在 `Add` 时读取该特性执行强制。

```csharp
public TimfHookAttribute(TimfSide side);
public TimfSide Side { get; }
```

### `TimfDependsOnAttribute` · `TimfLoadAfterAttribute`

均为 `AllowMultiple = true`。

```csharp
public TimfDependsOnAttribute(string modId);
public string ModId { get; }
public string MinVersion { get; set; }   // 可选；加载时强制校验

public TimfLoadAfterAttribute(string modId);
public string ModId { get; }
```

- `TimfDependsOn`：硬依赖，目标缺失或加载失败时本 mod **不会加载**。
- `TimfLoadAfter`：软排序提示，目标不存在**不会**导致失败。

#### 版本格式与比较

`MinVersion` 在加载时强制校验：目标 mod 版本更低则本 mod 加载失败并写入日志。

格式为 **1–4 段点分数字**，可带**预发布后缀**，允许前导 `v`：

```
1.2        1.2.0        1.2.0.3        1.2.0-beta.1        v1.2.0
```

比较规则：先按数字逐段比（缺省段补 0，所以 `1.2` == `1.2.0`），全部相同时**预发布低于正式版**——`1.2.0-beta` < `1.2.0`。

> **校验失败即拒绝。** `MinVersion` 或目标的 `IMod.Version` 任一无法解析，依赖都会被判定为不满足，而不是假定通过。同一套比较逻辑也用于握手的版本门；那里的版本串来自不可信对端，宽松回退会让对端发个乱码就绕过 `Net = Required` 的版本要求。

负数、超过 4 段、空串、`latest` 这类值都不是合法版本。mod 自身的 `Version` 若不可解析，加载器会在发现阶段记一条警告——它仍能加载，但无法满足任何 `MinVersion` 依赖或握手版本校验。

---

## 5. UI

### `IModSettings` — `[TimfHook(TimfSide.Client)]`

```csharp
void BuildSettingsUI(IImmediateModeUi ui);
```

> 只在 `ui` 上构建控件，**不要调用 `Begin`/`End`**——外层窗口由设置中心负责。

仅客户端进程可用。权威侧 mod 实现它也没问题，但需要一个有 UI 的会话（单人 / 主机）才能打开，专用服没有界面。

> 注意：实现 `IModSettings` **不会**让 mod 获得客户端能力、不影响侧别推断。它回答的是「这个钩子能在哪被派发」，而非「这个 mod 是否需要客户端半边」。

### `IImmediateModeUi`

由 TIMF.UI 库模组提供。通过 `IClientServices.Ui` 或 `context.Services` 取得，在 `PostDraw` 中调用。

```csharp
bool IsReady { get; }                       // 纹理 / 字体就绪

bool Begin(string title);                   // 返回 false 表示折叠/关闭，仍需调用 End
bool Begin(string title, ref bool open);
void End();
bool BeginChild(string id, float height, float width = 0f);   // 定高可滚动子区域
void EndChild();

void Text(string text);
void TextColored(string text, Color color);
void Separator();
void Spacing(float pixels = 6f);
void SameLine(float spacing = 8f);

bool Button(string label);
bool Selectable(string label, bool selected);                 // 整行可选中，用于列表
bool Checkbox(string label, ref bool value);
bool SliderFloat(string label, ref float value, float min, float max);
bool InputFloat(string label, ref float value, float step = 0.1f);
bool TabBar(string id, string[] labels, ref int selectedIndex);
bool CollapsingHeader(string label, ref bool open);
bool InputText(string label, ref string value, int maxLength = 64);

Vector2 MousePosition { get; }              // UI 逻辑坐标
bool IsMouseClicked { get; }
bool WantCaptureMouse { get; }
bool WantCaptureKeyboard { get; }           // 文本框聚焦并吞掉键盘输入时为 true
bool AnyWindowOpen { get; }
bool IsGameFocused { get; }
```

- `TabBar` / `InputText` / `CollapsingHeader` 在**本帧发生变化**时返回 true。`TabBar` 会把索引钳制进 `[0, labels.Length)`。
- `InputText` 走游戏输入路径，因此**中文输入法可用，Ctrl+V 可粘贴**。

### `IUiHost`

UI **库**的帧驱动接口，普通玩法 mod 不需要实现。

```csharp
void NewFrame(GameTime gameTime);
void Render();
void EarlyBlockGameInput();
```

Core 在 mod `PostDraw` **之前**调 `NewFrame`、**之后**调 `Render`。`EarlyBlockGameInput` 必须在游戏消费点击前运行——绘制期再拦截，在主菜单上已经太晚。

---

## 6. 客户端钩子

三个注册表接口形状一致，都从 `IClientServices` 取得，在 `Load` 里 `Add`：

```csharp
void Add(THook hook);
void Remove(THook hook);
```

对应关系：`IPlayerUpdateHookRegistry` / `IMapOverlayHookRegistry` / `IInfoAccessoryHookRegistry`。

三个钩子接口均标注 `[TimfHook(TimfSide.Client)]`，在专用服上 `Add` 会被拒绝并记日志。

### `IPlayerUpdateHook`

```csharp
void OnPreUpdate();
```

Core 通过 `Player.ItemCheck` 的 Harmony 前缀派发（仅本地玩家）。

> 刻意**不**挂 `Player.Update`：`Update` 会在其后执行 `ResetControls`，只有从 `ItemCheck` 派发，钩子设置的 `controlUseItem` / 鼠标瞄准才能生效。

### `IMapOverlayHook`

```csharp
void OnDrawMap(MapOverlayInfo info, ref string hoverText);
```

由原版 `MapIconOverlay.Draw` 的 Harmony 后缀调用，全屏地图与小地图都会渲染。

> **运行在原版已打开的 SpriteBatch 内部——不要 `Begin`/`End`。**

```csharp
public struct MapOverlayInfo
{
    public Vector2 MapPosition;       // 可见地图区域左上角（瓦片坐标）
    public Vector2 MapOffset;         // 地图绘制的屏幕偏移（像素）
    public Rectangle? ClippingRect;   // 小地图裁剪矩形；全屏时为 null
    public float MapScale;            // 瓦片→像素缩放
    public float DrawScale;           // 图标绘制缩放建议值
    public float Alpha;               // 0..1
    public bool Fullscreen;           // true = 全屏地图，false = 小地图/覆盖层

    public Vector2 WorldToMap(Vector2 worldPixels);  // 与原版放置自身图标的算法一致
    public bool Contains(Vector2 mapPos);            // 是否在可见区域内（尊重小地图裁剪）
}
```

### `IInfoAccessoryHook`

```csharp
void OnRefreshInfoAccessories(object localPlayer);
```

在本地玩家的信息饰品标志位重建后触发：每帧的 `Player.UpdateEquips`，以及背包打开时的 `Player.RefreshInfoAccs`。

> 玩家以 `object` 传入，好让 `TIMF.Abstractions` 不引用 Terraria。在钩子内自行转型为 `Terraria.Player`，然后按需设置 `acc*` 字段。

---

## 7. 键位

注册进原版 `PlayerInput.KnownTriggers`，因此热键会出现在**设置 → 快捷键**中，共用同一套重绑定与存档路径。

### `IKeybindService`

```csharp
IKeybind Register(string id, string displayName, Keys defaultKey);  // 已存在则直接返回
void Unregister(string id);
IKeybind Get(string id);                                            // 不存在返回 null
bool TryGet(string id, out IKeybind keybind);
```

- `id` **必须全局唯一，建议用 `"ModId.Action"` 格式**。
- `defaultKey` 仅在该配置档尚无绑定时生效。

### `IKeybind`

```csharp
string Id { get; }
string DisplayName { get; }
bool Current { get; }                    // 本帧按住
bool JustPressed { get; }
bool JustReleased { get; }
string CurrentBindingDisplay { get; }    // 如 "Insert"；未绑定时为空串
```

---

## 8. 本地化

### `IModLocalization`

经 `IModContext.L` 取得，自动加载本 mod `Localization/` 下的 JSON 键值文件。

```csharp
string CurrentLanguage { get; }                        // 如 "en-US"、"zh-Hans"
string Get(string key, string fallback = null);
string Format(string key, params object[] args);       // Get 后再 string.Format
bool Has(string key);
```

`Get` 的回退链：**当前语言 → 语言基（zh-Hans→zh）→ en-US → en → `fallback` → 键名本身**。

### `ILanguageService`

框架级语言跟踪器，镜像 `Terraria.Localization.Language.ActiveCulture`。

```csharp
string CurrentLanguage { get; }     // 游戏未就绪时为 "en-US"
event Action LanguageChanged;       // 语言变化后触发（首次轮询时也会触发）
```

---

## 9. 注册表、会话、日志

### `IServiceRegistry`

跨 mod 服务解析总线。普通模组经 `IModContext.Services` 解析服务；发布自定义服务必须改走绑定调用方程序集的
`IModContext.ServicePublisher`。直接调用 `Register` 属于框架可信组件的保留入口，普通模组一旦包含该调用
痕迹会在加载前被拒绝，避免覆盖 `ISecurityCenter`、反射代理或 UI 服务。

```csharp
void Register<TService>(TService instance) where TService : class;
TService GetService<TService>() where TService : class;
bool TryGetService<TService>(out TService service) where TService : class;
```

```csharp
// IMyModApi 必须是当前模组程序集自己声明的接口。
context.ServicePublisher.Publish<IMyModApi>(new MyModApi());
```

发布器拒绝框架/其他程序集声明的接口、类型不匹配的实例和任何重复注册，因此模组不能抢占或替换既有服务。

### `IModRegistry` · `IModInfo`

```csharp
IReadOnlyList<IModInfo> Mods { get; }                                  // 含已禁用的 mod，按发现/加载顺序
bool TrySetEnabled(string id, bool enabled, out string message);       // 未找到或被拒绝时返回 false
```

> Core 在**发现阶段之后**才注册 `IModRegistry`，所以要**延迟解析**（例如在 `PostDraw` 里），不要在 `Load` 中取。

```csharp
public interface IModInfo
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    TimfSide Side { get; }              // 由接口推断出的能力侧
    TimfNetProfile NetProfile { get; }
    bool IsEnabled { get; }             // 用户开关；false 则跳过加载 / 权威激活
    bool IsLoaded { get; }              // 本进程已完成 IMod.Load
    bool ServerLogicActive { get; }     // 本次会话已激活其权威半边
    IModSettings Settings { get; }      // 已加载且当前会话允许操作设置时非 null
    bool HasSettings { get; }
}
```

Core 返回的条目还实现可选扩展接口 `IModSessionState`。它没有直接扩展 `IModInfo`，以保持已有消费者的
二进制兼容；需要会话级控制的 UI 可以用 `info as IModSessionState` 检测：

```csharp
public interface IModSessionState
{
    bool IsSessionAllowed { get; }       // 当前世界/服务器是否允许执行，不改写用户偏好
    bool CanChangeEnabled { get; }       // 当前是否允许改变主启用开关
    string InteractionLockReason { get; }
    bool HasSettingsCapability { get; }  // 类型是否声明了设置页，即使当前被会话锁定
    bool CanOpenSettings { get; }        // 设置页当前是否允许操作
}
```

启停规则：

- 主菜单中可以改变任意非框架模组的用户开关；
- 进入单人、主机、专用服或联机世界后，任何具有 `Authority` 能力的模组（`Authority` / `Both`）主开关
  都会锁定，必须返回主菜单才能改变；纯 `Client` 模组仍可本地开关；
- 联机客户端在握手完成前默认禁止所有本地 `Authority` / `Both` 模组执行；握手完成后只放行服务器公布
  且本地版本/用户开关匹配的交集；
- 服务器未启用的本地双端/服务端模组只做**会话级禁用**，不会把持久 `IsEnabled` 偏好写成 false；返回
  主菜单后恢复；
- 会话禁用同时门控框架派发的 `PostDraw`、玩家更新、地图覆盖、信息饰品钩子、内容饰品效果和自定义
  图块/墙壁放置；其设置页必须显示为不可用，不能继续调用 `BuildSettingsUI`。

> 判断「是否破坏原版兼容」时用 `NetProfile`，不要去 switch `Side`。

### `ITimfSession`

会话角色与权威启用状态。在 mod `Load` 之前就已注册为服务，状态随 `netMode` 变化更新。

```csharp
public enum TimfSessionKind
{
    Menu = 0,               // 主菜单 / 尚未进入世界
    SinglePlayer = 1,       // netMode 0
    Host = 2,               // Host & Play / listen server（netMode 2，非 dedServ）
    DedicatedServer = 3,    // Main.dedServ
    MultiplayerClient = 4,  // netMode 1
}

public interface ITimfSession
{
    TimfSessionKind Kind { get; }
    bool ServerLogicEnabled { get; }
    bool RemoteTimfConfirmed { get; }                          // 联机客户端完成握手后为 true
    IReadOnlyList<ITimfRemoteModInfo> EnabledServerMods { get; } // 加入时 = 主机列表 ∩ 本地
}

public interface ITimfRemoteModInfo
{
    string Id { get; }
    string Version { get; }
}
```

### `ILogger`

```csharp
void Info(string message);
void Warn(string message);
void Error(string message);
void Error(string message, Exception exception);
void Debug(string message);
```

---

## 10. 天气

经 `IAuthorityServices.Weather` 取得。Core 启动时注册全部原版大气通道，其他 mod 可注册自定义通道（建议 id 用 `modid.name`）。

> **写操作只应在 `IsAuthoritative` 为 true 时进行。**

### `IWeatherService`

```csharp
IReadOnlyList<IWeatherChannel> Channels { get; }              // 原版 + 插件，按类别再按 id 排序
void Register(IWeatherChannel channel);                       // 按 Id 注册或替换
bool Unregister(string id);                                   // id 未知返回 false
bool TryGet(string id, out IWeatherChannel channel);
IReadOnlyList<IWeatherChannel> GetByCategory(WeatherCategory category);
WeatherSnapshot Capture();
bool TrySet(string channelId, WeatherValue value, WeatherSetOptions options, out string error);
bool TryApplyBundle(WeatherBundle bundle, out string error);
void SetLock(WeatherBundle bundle, bool enabled);             // 每次原版天气 tick 后重新施加，防止随机漂移
bool IsLockEnabled { get; }
WeatherBundle LockedBundle { get; }                           // 未锁定时为 null
void SyncToClients();                                         // 广播 WorldData，让原版客户端更新视觉
```

### `IWeatherChannel`

```csharp
string Id { get; }                          // 稳定 id，如 vanilla.atmosphere.preset
string DisplayName { get; }
WeatherCategory Category { get; }
WeatherValueKind ValueKind { get; }
IReadOnlyList<string> Choices { get; }      // 仅 Choice 类型有效
float? Min { get; }                         // 标量的闭区间边界；null = 无界
float? Max { get; }
bool CanWrite { get; }
WeatherValue Read();
bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error);
```

### 值类型

```csharp
public enum WeatherCategory  { Atmosphere = 0, Wind = 1, Moon = 2, Event = 3, Other = 4 }

public enum WeatherValueKind
{
    Toggle  = 0,   // 开关（血月、沙尘暴、是否下雨）
    Scalar  = 1,   // 连续量（雨强 0–1、风速 −1.5–1.5）
    Integer = 2,   // 整数离散（月相 0–7）
    Choice  = 3,   // IWeatherChannel.Choices 中的具名选项
}

public struct WeatherValue
{
    public bool?  BoolValue;
    public float? FloatValue;
    public int?   IntValue;
    public string StringValue;

    public static WeatherValue FromBool(bool v);
    public static WeatherValue FromFloat(float v);
    public static WeatherValue FromInt(int v);
    public static WeatherValue FromString(string v);
}

public sealed class WeatherSetOptions
{
    public bool Instant = true;       // 支持的通道跳过淡入（如立即下雨）
    public bool SyncNetwork = true;   // 广播 MessageID.WorldData
}

public sealed class WeatherBundle    // 复合改动；字段为 null 表示「保持不变」
{
    public string AtmospherePreset;
    public float? RainIntensity;
    public float? WindSpeed;
    public int?   MoonPhase;
    public List<string> EnableEvents;
    public List<string> DisableEvents;
    public bool Instant = true;
    public bool SyncNetwork = true;
}

public sealed class WeatherSnapshot   // Capture() 的返回值
{
    public float WindSpeed;      public int  MoonPhase;
    public bool  Raining;        public float RainIntensity;
    public bool  Sandstorm;      public bool SlimeRain;
    public bool  BloodMoon;      public bool PumpkinMoon;
    public bool  FrostMoon;      public bool LanternNight;
    public int   CloudCount;
    public Dictionary<string, WeatherValue> Channels;   // 按 IWeatherChannel.Id 索引
    public string Summary;
}
```

### 内置通道 id

`WeatherChannelIds` 中的常量：

| 常量 | 值 |
|---|---|
| `AtmospherePreset` | `vanilla.atmosphere.preset` |
| `RainActive` | `vanilla.atmosphere.raining` |
| `RainIntensity` | `vanilla.atmosphere.rain_intensity` |
| `Sandstorm` | `vanilla.atmosphere.sandstorm` |
| `SlimeRain` | `vanilla.atmosphere.slime_rain` |
| `CloudCount` | `vanilla.atmosphere.clouds` |
| `WindSpeed` | `vanilla.wind.speed` |
| `MoonPhase` | `vanilla.moon.phase` |
| `BloodMoon` | `vanilla.event.blood_moon` |
| `PumpkinMoon` | `vanilla.event.pumpkin_moon` |
| `FrostMoon` | `vanilla.event.frost_moon` |
| `LanternNight` | `vanilla.event.lantern_night` |

`WeatherChannelIds.AtmospherePresets` 提供 `AtmospherePreset` 的取值：`clear`、`cloudy`、`light_rain`、`rain`、`heavy_rain`、`storm`、`blizzard`、`sandstorm`、`windy`、`slime_rain`。

---

## 11. 前缀

### `IPrefixService`

经 `IAuthorityServices.Prefix` 取得。Core 启动时暴力枚举出全部原版最佳前缀，mod 可为自定义物品注册覆盖值。

```csharp
void RegisterBestPrefix(int itemType, int prefixId);
bool TryGetBestPrefixes(int itemType, out IReadOnlyList<int> prefixIds);
bool TryGetRandomBestPrefix(int itemType, out int prefixId);
```

> 一件物品**可能有多个**最佳前缀（例如饰品），每次重铸随机取其一。

---

## 12. 自定义内容

自定义内容位于独立的 `TIMF.Content.dll` / `TIMF.Content` 命名空间。内容 mod 必须实现
`IContentMod`，并使用 `Net = TimfNetProfile.Optional` 或 `Required`；自定义 ID 无法与纯原版端互通。

### 稳定性契约

以下规则属于内容 API 的稳定契约：

- **内容键是持久身份。** `ModId/InternalName` 用于 ID 分配表和所有旁挂存档；运行时 `Type` 只是当前
  进程的数值索引，mod 不应把它写入自己的长期数据。
- **原版存档保持原版可读。** 模组物品、图块、墙壁和自定义容器 ID 不会写进 `.plr` / `.wld`；没有
  TIMF 时原版仍可读取主体文件，只是暂时看不到旁挂内容。
- **保存不能破坏运行中状态。** 为调用原版序列化而临时清空的物品、图块、墙壁和箱子，会在成功路径和
  异常路径中恢复；旁挂文件使用同目录临时文件、磁盘刷新和原子替换。
- **暂时缺少 mod 不等于删除内容。** 无法解析的内容键会保留。只有玩家在对应坐标或槽位建立了新内容时，
  才以玩家当前修改为准并丢弃冲突的旧记录。
- **注册顺序不应成为存档身份。** 稳定 ID 分配表按内容键保存；新增另一个 mod 或改变加载顺序不应改变
  已发布内容的含义。
- **公开类型是兼容边界。** `TIMF.Abstractions` 和 `TIMF.Content` 中的 public 类型及成员是 mod 作者可
  依赖的 API；`TIMF.Core`、Harmony 补丁和反射辅助器均为实现细节，不构成兼容承诺。

为了维持上述保证，发布后的 `ModId` 和 `InternalName` 不得随意更名。显示名称、提示文本和贴图路径可以
修改，但持久内容键的迁移需要框架未来提供显式别名机制，不能靠重新分配数值 ID 代替。

稳定性契约不表示任意 Terraria 版本之间的二进制补丁必然兼容。升级游戏版本时仍应先运行
`ContentTestKit` 的数组、放置、掉落、存档和配方测试，再用备份世界验证正式存档。

```csharp
[TimfMod(Id = "MyContent", Net = TimfNetProfile.Required)]
public sealed class MyContentMod : IContentMod
{
    public void AddContent(IContentRegistry registry)
    {
        registry.AddItem<MyPlaceableItem>();
        registry.AddTile<MyTile>();
        registry.AddWall<MyWall>();
    }

    // IMod 成员略
}
```

### `TimfItem` / `TimfTile` / `TimfContainerTile` / `TimfWall`

两类定义都提供稳定的 `InternalName`、`ContentKey`、运行时分配的 `Type`、`Texture` 和
`SetStaticDefaults()`。`TimfItem.SetDefaults()` 配置每个物品实例；`TimfTile.SetStaticDefaults()`
用于写入 `Main.tileSolid[Type]`、`Main.tileFrameImportant[Type]` 等图块集合。

```csharp
public sealed class MyTile : TimfTile
{
    public static int RegisteredType { get; private set; }

    // 可返回原版物品 id，也可返回已经分配的自定义 TimfItem.Type。
    // 默认值 0 表示挖掘后不掉落物品。
    public override int ItemDrop => MyPlaceableItem.RegisteredType;
    public override int ItemDropStack => 1;

    public override void SetStaticDefaults()
    {
        RegisteredType = Type;
        Main.tileSolid[Type] = true;
        Main.tileFrameImportant[Type] = false;
    }
}
```

`TimfTile.ItemDrop` 和 `ItemDropStack` 通过原版 `WorldGen.KillTile_GetItemDrops` 管线生效；
实际实体只会在单机或服务端生成，爆炸、锤击失败及 `noItem` 等原版规则仍然有效，不会额外重复掉落。

物品可覆盖 `AddRecipes()`，通过 `TimfRecipe` 注册包含原版或模组物品、模组制作站的配方。该方法在
所有内容完成 ID 分配和 `SetStaticDefaults()` 后执行，因此可以安全引用其他定义的 `RegisteredType`：

```csharp
public override void AddRecipes()
{
    TimfRecipe.Create(MyResult.RegisteredType, 5)
        .AddIngredient(MyMaterial.RegisteredType, 1)
        .AddTile(MyWorkbenchTile.RegisteredType)
        .Register();
}
```

当前配方 API 支持结果数量、多个物品材料和一个制作站；后注册的配方默认禁止微光分解，以免使用已经
完成初始化的原版分解表产生不一致结果。

需要锚点规则的特殊图块可通过 `PlacementTemplateTile` 复制原版 `TileObjectData`。例如自定义火把返回
`TileID.Torches`，即可获得地面、左右侧面和背景墙锚点，同时仍使用自己的图块 ID 和贴图。
发光图块覆盖 `ModifyLight(i, j, ref red, ref green, ref blue)`；框架会把它与原版环境光按分量取最大值。

箱子类图块继承 `TimfContainerTile`。默认会复制原版 2×2 箱子的 `TileObjectData` 和放置后创建
`Chest` 的钩子；定义仍需把运行时集合标成容器：

```csharp
public sealed class MyChestTile : TimfContainerTile
{
    public override int ItemDrop => MyChestItem.RegisteredType;

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileNoAttach[Type] = true;
        Main.tileContainer[Type] = true;
        TileID.Sets.BasicChest[Type] = true;
    }
}
```

框架会沿用原版 40 槽箱子 UI、重命名和“非空时禁止破坏”规则。空箱被破坏时只掉落一次
`ItemDrop`，不会按 2×2 的四个格子重复掉落。

`TimfWall` 提供与图块独立的 Wall ID、`Texture`、`ItemDrop` 和 `SetStaticDefaults()`。墙壁物品在
`TimfItem.SetDefaults()` 中设置 `Item.createWall = MyWall.RegisteredType`。自定义墙壁贴图使用原版
144×180 墙壁帧表布局。

`IContentLookup` 可从 `context.Services` 取得，提供 `ItemType<T>()`、`TileType<T>()`、
`GetItem()`、`GetTile()`、`GetWall()`、`RegisteredItems`、`RegisteredTiles`、`RegisteredWalls`
和诊断用 `Report()`。

### 图块与墙壁存档规则

自定义图块和墙壁 ID 都不会写进 `.wld`。框架在原版世界序列化前临时移除这些内容，把完整格子状态写入
同路径的 `<world>.wld.timf-tiles`，保存完成后立即恢复内存；加载世界并进入可玩状态后再从旁挂覆盖回来。
移除某个内容 mod 时，其无法解析的旁挂记录会保留；如果玩家在该坐标放置了原版图块，新修改优先，
旧旁挂记录会在下次保存时丢弃。

### 物品与容器存档规则

模组物品不会以运行时数值 ID 写入玩家或世界存档：

- 背包、装备、染料、宠物/矿车栏、四个个人储存空间（存钱罐、保险箱、护卫熔炉、虚空保险库）和
  装备预设写入玩家旁挂 `<player>.plr.timfitems`；
- 原版世界箱子中的模组物品，以及整个 `TimfContainerTile` 自定义容器，写入世界旁挂
  `<world>.wld.timf-chests`；
- 自定义物品和容器身份均使用 `ModId/InternalName`，ID 重分配不会改变存档含义；自定义容器内的
  原版物品仍保存原版物品 ID；
- 写 `.wld` 时，自定义容器实体会先从 `Main.chest` 临时摘除，原版箱子里的模组物品槽会临时变成
  空气；无论保存成功或抛出异常，运行中的原对象都会恢复；旁挂通过临时文件、磁盘刷新和原子替换提交；
- 内容 mod 暂时缺失时，无法解析的记录会继续保留，重新安装后可恢复。若玩家在缺失内容的位置或槽位
  建立了新内容，则玩家当前修改优先，旧记录不会覆盖它。

> `InternalName` 是存档身份的一部分，发布后不要改名。图块贴图必须随 mod 部署，路径规则与物品一致，
> 默认是 `Content/<InternalName>.png`。

---

## 13. 安全与敏感权限

### 安全优先原则

加载 DLL 只表示允许模组使用已公开的游戏与框架能力，**不表示用户授予了任意本机权限**。所有新增服务
和模组 API 都必须遵守以下规则：

- **默认拒绝。** 没有可验证授权时，敏感操作不得执行；授权 UI 不可用、专用服无人交互、申请超时或
  授权记录无法解析时也必须拒绝，而不是降级为默认允许。
- **最小权限。** 申请必须限制到完成当前功能所需的最小路径、操作类型、命令和持续时间；不能用一次
  “允许文件访问”换取整个磁盘、永久写入和任意命令执行能力。
- **先告知、后执行。** 框架必须在敏感操作发生前向用户或专用服管理员展示申请，至少包含模组身份、
  行为类型、目标路径或程序、命令参数、用途说明、授权范围和有效期。模组不能自行伪造授权提示。
- **可撤销、可审计。** 用户应能查看和撤销持久授权。框架记录授权、拒绝和实际使用结果，但日志必须
  避免泄露令牌、密码、完整隐私文件内容等秘密。
- **路径按真实目标校验。** 在判断路径是否位于允许范围内之前，必须进行绝对路径规范化，并防止通过
  `..`、符号链接、目录联接、大小写或网络路径绕过授权边界。
- **拒绝必须无副作用。** 被拒绝的操作不能先创建临时文件、截断目标、启动子进程或读取部分内容后再
  报错；调用方应得到明确、可诊断的拒绝结果。

### 必须申请授权的行为

以下行为至少属于敏感权限，不能仅凭 `IMod.Load()` 已执行而自动允许：

| 行为 | 默认规则 | 授权时必须展示 |
|---|---|---|
| 读取模组工作区之外的文件或目录 | 拒绝 | 规范化后的路径、读取范围、用途 |
| 模组自主创建、修改、覆盖、移动或删除文件 | 拒绝 | 目标路径、操作类型、是否覆盖、用途 |
| 执行 Shell、命令行程序、脚本或启动子进程 | 拒绝 | 可执行文件、完整参数、工作目录、用途 |

“模组工作区”默认只包含该模组的 `ModDirectory` / `ContentDirectory` 中随包分发的只读资源。即使目标位于
工作区内，**写入**和**执行**仍是敏感行为。框架自身为日志、配置、内容 ID 表和存档旁挂执行的固定写入，
属于框架核心声明行为；它们必须限定在框架目录或游戏存档旁挂位置，不能被模组借用为任意写文件通道。

授权应优先支持“仅本次操作”或“仅本次会话”。持久授权必须由用户明确选择，且绑定模组稳定身份、权限
种类和规范化目标；模组升级后若目标或能力扩大，必须重新申请。专用服没有交互 UI 时，只接受管理员事先
配置的精确授权，不能弹窗失败后自动放行。

### 已实现的授权与警告 UI

`IModContext.Security` 提供按模组身份绑定的代理。申请创建后默认是 `Pending`，安全中心会自动打开；用户
可以拒绝、允许一次、允许到本次 TIMF 进程结束，或持久允许**完全相同**的文件操作。持久授权绑定模组 ID、
程序集 SHA-256、行为、规范化目标、覆盖意图和用途说明；模组二进制升级或用途改变后必须重新授权。出于
风险与参数隐私考虑，进程执行只支持单次或当前进程授权，不提供持久授权。Mod Settings
首页持续显示隔离边界警告，并可随时打开安全中心撤销持久授权。

```csharp
using TIMF.Abstractions.Security;

// 第一次调用只提交申请，不读取文件。
var request = context.Security.RequestFileRead(
    @"D:\Data\example.bin", "Import the map selected by the user");

// 后续帧查询；只有安全中心明确授权后才能通过代理执行。
request = context.Security.GetRequest(request.Id);
if (request.Status == SensitiveOperationStatus.Granted)
{
    byte[] bytes = context.Security.ReadAllBytes(request.Id);
}
```

完整代理表：

| 申请 | 获批后的执行 | 重要约束 |
|---|---|---|
| `RequestFileRead(path, purpose)` | `ReadAllBytes(requestId)` | 绝对路径、逐级拒绝重解析点 |
| `RequestFileWrite(path, overwrite, purpose)` | `WriteAllBytes(requestId, data)` | 精确目标、覆盖意图单独授权、同目录临时文件原子提交 |
| `RequestProcess(exe, args, cwd, purpose)` | `RunProcess(requestId, timeout)` | exe/cwd 必须是绝对现存路径；不经 Shell；超时上限 5 分钟 |

拒绝、取消、尚未决定、错误类型或不属于本模组的 request ID 都无法执行。单次授权在开始执行前即被消费，
即使实际 I/O 失败也不会自动恢复。专用服当前没有交互 UI，也没有管理员预授权配置格式，因此申请会直接
拒绝；TIMF.UI 不可用时也会立即拒绝，等待决定超过五分钟则超时拒绝，不能因为无人点击而默认放行。

### 加载前静态安全审计

模组包中的主程序集和同目录私有依赖会在任何模组构造函数、静态初始化器或 `Load()` 执行之前扫描。
发现下列痕迹时整个模组会被拒绝加载：

- `System.IO.File`、`Directory`、`FileStream` 等直接文件系统访问（`Path`、内存流和内存文本读写器除外）；
- `Process` / `ProcessStartInfo`、P/Invoke、内部调用、`calli`、`Marshal` 与原生 DLL；
- 直接网络、Socket、注册表访问；
- `Reflection.Emit`、动态程序集加载、反射 `Invoke` / `CreateDelegate`、表达式运行时编译；
- 直接创建或控制 Harmony patch；
- 直接调用原始服务注册入口、读取/写入环境变量或运行时编译代码；
- 在包内捆绑同名 `TIMF.Abstractions` / `TIMF.Content` / Harmony DLL，或发生程序集身份/实际路径冲突；
- 无法完整解析的方法体、元数据或依赖。审计失败必须拒载，不会退化为警告后继续。

拒载结果会写入核心日志并自动打开安全中心；Mod Settings 首页显示被拒模组数量，安全中心列出程序集、
方法和命中的 API。框架不会为了让官方模组通过而设置普通模组白名单。唯一不走普通审计的是框架自带
`TIMF.UI`，它必须同时匹配 `trusted-framework-components.v1` 中由构建流程生成的精确相对路径与 SHA-256；
文件被替换或清单缺失时同样拒载。

发现阶段不再实例化 `IMod` 来读取名称/版本。简单常量属性通过 IL 元数据读取，其他情况回退到类型名和
程序集版本，确保审计之前没有模组代码执行窗口。

### 受限存储与兼容代理

普通配置不需要每次弹出敏感授权，但必须走 `IModContext.Storage`：

```csharp
if (context.Storage.ConfigExists("MyMod.json"))
    json = context.Storage.ReadConfigText("MyMod.json");
context.Storage.WriteConfigText("MyMod.json", json);

byte[] icon = context.Storage.ReadContentBytes("Images/Icon.png");
```

配置被限制在 `config/mod-data/<ModId>/` 下，只接受单一安全文件名并原子写入；包内资源只能从本模组的
`ContentDirectory` 相对读取，所有路径都会拒绝重解析点和目录逃逸。旧版 `config/<ModId>.json` 在文件名
与模组 ID 忽略标点后完全相同时由 Core 首次复制到新目录，既保留设置，也不能借此读取其他模组配置。

兼容 Terraria 私有 API 时，禁止直接 `MethodInfo.Invoke` 或 Harmony：

- `ITerrariaReflection.Invoke` 只接受 `Terraria.exe` 声明、名称不含文件/保存/加载等敏感标记，且不接收
  字符串或流参数的方法；
- `IModContext.Patches` 只允许对同样通过检查的 Terraria 方法安装 prefix/postfix，回调必须是本模组
  程序集声明的静态方法；不公开 transpiler、任意 Harmony ID 或 Core/BCL patch 能力。

这些代理用于兼容游戏私有方法，不是绕过 `Security` 的通用反射入口。

### 当前隔离边界

TIMF 现在能够可靠约束的是**经 `IModContext.Security` 代理执行**的操作。普通 .NET Framework 模组 DLL
仍与游戏同进程、完全信任运行，框架无法可靠拦截它直接调用 `System.IO`、`Process`、P/Invoke 或自行加载
本机代码。安全中心和 Mod Settings 会明确展示该警告，不把“已有授权 UI”误称为进程沙箱。

因此受支持模组不得绕过代理直接执行上述敏感行为。静态审计会阻断直接调用、普通委托、私有依赖、原生
依赖、动态调用和直接 Harmony 等常见绕过，但不能数学上证明任意托管程序无恶意行为；混淆器、运行时漏洞
或未覆盖的间接调用仍可能逃逸。真正的强隔离仍需要把不受信任代码移出 Terraria 进程。

`ISecurityCenter` 只公开待处理/拒载数量、边界警告和打开窗口能力，真正的批准、撤销与审计入口留在 Core
内部，申请模组不能通过公共服务总线自行批准。

需要人工验证时，可在主菜单打开 **Mod Settings → Content Test Kit → 安全授权管线测试**：提交读取核心
日志的测试申请后，检查首页待处理警告和安全中心决策；获批后还需再次点击执行，测试只报告字节数，不会
显示日志内容。

---

## 附：TIMF.UI 库

`libs/TIMF.UI` 对外只暴露一个 public 类型 `TimfUiMod`，mod 作者**不应**直接引用它。

它在 `Load` 时向 `context.Services` 注册 `IImmediateModeUi` 与 `IUiHost` 两个服务，消费方只需通过 `IClientServices.Ui` 或 `context.Services` 拿接口。

依赖它请写 `[TimfDependsOn("TIMF.UI")]`。
