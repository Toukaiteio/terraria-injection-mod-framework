# TIMF API 参考

`TIMF.Abstractions` 的完整公共 API。所有类型都在 `TIMF.Abstractions` 命名空间下（包括 `Weather/`、`Prefix/` 子目录中的类型，它们**没有**子命名空间）。

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
```

| 属性 | 专用服 | 联机客户端 | 单人 / 主机 |
|---|---|---|---|
| `Client` | **null** | 可用 | 可用 |
| `Authority` | 非 null | 非 null | 非 null |
| `Authority.IsAuthoritative` | `true` | **`false`** | `true` |

> `Client` 在专用服上为 null，**必须判空**。`Authority` 永不为 null，但动世界状态前要先查 `IsAuthoritative`。

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

跨 mod 服务总线。库模组（如 TIMF.UI）在此注册接口，使用方经 `IModContext.Services` 解析。

```csharp
void Register<TService>(TService instance) where TService : class;
TService GetService<TService>() where TService : class;
bool TryGetService<TService>(out TService service) where TService : class;
```

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
    IModSettings Settings { get; }      // 实现了 IModSettings 且当前已加载时非 null
    bool HasSettings { get; }
}
```

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

## 附：TIMF.UI 库

`libs/TIMF.UI` 对外只暴露一个 public 类型 `TimfUiMod`，mod 作者**不应**直接引用它。

它在 `Load` 时向 `context.Services` 注册 `IImmediateModeUi` 与 `IUiHost` 两个服务，消费方只需通过 `IClientServices.Ui` 或 `context.Services` 拿接口。

依赖它请写 `[TimfDependsOn("TIMF.UI")]`。
