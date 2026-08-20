# TIMF API Reference

Language: **English** | [简体中文](api-reference.zh-CN.md)

The complete public API of `TIMF.Abstractions`, plus the custom-content API exposed by the separate `TIMF.Content` assembly. Except where noted in section 12, types live in the `TIMF.Abstractions` namespace; content types live in `TIMF.Content`.

TIMF prioritizes stability over feature count. Public APIs, content identities, and vanilla save compatibility are framework contracts. If content cannot be activated safely, the framework records a clear error and stops that content pipeline instead of continuing with partially expanded or registered state.

TIMF also follows a security-first model: loading a mod does not grant it arbitrary filesystem or command-execution permissions on the host. Reading outside the workspace, writing files independently, and running shells or processes require an authorization request through the framework, which presents the request to the user.

External types used by these signatures come from `Microsoft.Xna.Framework` (`GameTime` / `Color` / `Vector2` / `Rectangle`), `Microsoft.Xna.Framework.Input` (`Keys`), and `Microsoft.Xna.Framework.Graphics` (`Texture2D`).

## Contents

- [1. Entry point and capability markers](#1-entry-point-and-capability-markers)
- [2. Context and side-scoped services](#2-context-and-side-scoped-services)
- [3. Side and protocol enums](#3-side-and-protocol-enums)
- [4. Attributes](#4-attributes)
- [5. UI](#5-ui)
- [6. Client hooks](#6-client-hooks)
- [7. Keybinds](#7-keybinds)
- [8. Localization](#8-localization)
- [9. Registries, sessions, and logging](#9-registries-sessions-and-logging)
- [10. Weather](#10-weather)
- [11. Prefixes](#11-prefixes)
- [12. Custom content](#12-custom-content)
- [13. Security and sensitive permissions](#13-security-and-sensitive-permissions)

---

## 1. Entry point and capability markers

### `IMod`

The mod entry point. Implement one public class in your DLL.

```csharp
string Name { get; }      // Display name and default dependency ID
string Version { get; }
void Load(IModContext context);
void Unload();
void PostDraw(GameTime gameTime);
```

- `PostDraw` runs every frame after the game finishes drawing (`Main.OnPostDraw`). It **does not run on a dedicated server** and is normally implemented only by `IClientMod` types.

### `IClientMod : IMod` · `IAuthorityMod : IMod`

Marker interfaces with no members. The loader uses them to infer [`TimfSide`](#timfside):

| Implementation | Inferred side |
|---|---|
| `IClientMod` | `TimfSide.Client` |
| `IAuthorityMod` | `TimfSide.Authority` |
| Both | `TimfSide.Both` |
| Neither (plain `IMod`) | `TimfSide.Client` |

`IAuthorityMod` is **vanilla-compatible by default**: it does not enter the handshake directory, so pure-vanilla clients can still join a world hosted by the mod. If the peer must also install the mod, explicitly raise the profile with `[TimfMod(Net = ...)]`.

### `IAuthorityLifecycle`

Optional lifecycle callbacks for the authority side.

```csharp
void OnAuthorityActivate(IModContext context);
void OnAuthorityDeactivate();
```

> **This is a lifecycle interface, not a capability marker.** Implementing it alone does **not** give a mod authority capability and does not affect `TimfSide` inference. Capabilities are declared only by `IAuthorityMod` and `IClientMod`.

> **Activation does not mean authority.** A mod using the `Optional` / `Required` profile also activates on a multiplayer client after a successful handshake; `IAuthorityServices.IsAuthoritative` is then **false**. Gate world writes with `IsAuthoritative`, not with this callback.

A pure delayed-loading mod that only needs `Load` / `Unload` does not need to implement this interface.

### `IModFeatureToggle`

Optional capability exposing a mod's main in-world feature switch. It is a lightweight configuration change: it does not load or unload the mod and does not re-install patches. Bind it to the mod's own configuration, save it in the setter, and make the actual feature read the same state.

```csharp
public interface IModFeatureToggle
{
    bool FeatureEnabled { get; set; }
}
```

This is not a replacement for the primary mod switch. The primary switch is managed by `IModRegistry.TrySetEnabled` and is locked after entering a world according to session rules. The settings hub exposes the feature through [`IModInfo.FeatureToggle`](#imodregistry--imodinfo); it is `null` when the mod is not loaded or the current session does not allow the operation.

---

## 2. Context and side-scoped services

### `IModContext`

```csharp
ILogger Log { get; }
string HomeDirectory { get; }      // TIMF root (logs / config / mods)
string ConfigDirectory { get; }    // Home/config, shared across mods
string ModDirectory { get; }       // Directory containing this mod assembly
string ContentDirectory { get; }   // Resource directory; ModDirectory/Content when it exists
string ModAssemblyPath { get; }
IServiceRegistry Services { get; }
IModLocalization L { get; }        // This mod's Localization/*.json
IClientServices Client { get; }
IAuthorityServices Authority { get; }
Security.ISensitiveOperationService Security { get; }
Storage.IModStorage Storage { get; }
Security.IModPatchService Patches { get; }
```

| Property | Dedicated server | Multiplayer client | Single-player / host |
|---|---|---|---|
| `Client` | **null** | Available | Available |
| `Authority` | Non-null | Non-null | Non-null |
| `Authority.IsAuthoritative` | `true` | **`false`** | `true` |

> `Client` is null on a dedicated server and **must be checked**. `Authority` is never null, but check `IsAuthoritative` before changing world state. `Security` is a mod-identity-bound sensitive-operation proxy; do not impersonate another mod through the shared service bus. `Storage` is limited to this mod's configuration and packaged read-only resources; `Patches` is a restricted Terraria patch proxy.

### `IClientServices`

Client-process services. The complete property is null on a dedicated server.

```csharp
IImmediateModeUi Ui { get; }                        // From TIMF.UI; null when not installed
IKeybindService Keybinds { get; }
IPlayerUpdateHookRegistry PlayerUpdate { get; }
IMapOverlayHookRegistry MapOverlay { get; }
IInfoAccessoryHookRegistry InfoAccessories { get; }
```

### `IAuthorityServices`

```csharp
bool IsAuthoritative { get; }      // Whether this process owns world simulation authority
IWeatherService Weather { get; }
IPrefixService Prefix { get; }
```

---

## 3. Side and protocol enums

TIMF uses **two orthogonal axes** to describe a mod. See the [Side and Protocol Model](./side-model.md) for the design rationale.

### `TimfSide`

Capability axis: which Terraria process role owns the code. It mirrors two facts used by vanilla.

```csharp
[Flags]
public enum TimfSide
{
    None      = 0,        // No capability; always invalid for a loaded mod
    Client    = 1 << 0,   // !Main.dedServ — local player, drawing, and input
    Authority = 1 << 1,   // Main.netMode != 1 — world simulation authority
    Both      = Client | Authority,
}
```

> `Authority` means “this code is world logic”, **not** “this process is a server”. Vanilla also compiles world logic into the client binary and gates it at runtime. Whether the current process may actually write world state is answered by `IAuthorityServices.IsAuthoritative`.

```csharp
public static class TimfSides
{
    public static bool IsClientCapable(TimfSide side);
    public static bool IsAuthorityCapable(TimfSide side);
    public static bool IsDeferredAuthority(TimfSide side);  // side == Authority
}
```

- `IsDeferredAuthority`: a pure authority mod has no work before a session grants authority, so the loader **defers assembly loading** until activation and unloads it on deactivation. A mod with a client half is not subject to this pure-authority delay; its exact timing still follows `LoadBeforeWorld` or the default world phase.

### `TimfNetProfile`

Protocol axis: whether the joining peer must install the same code. Vanilla has **no** equivalent; this is a TIMF-layer concept. The values form the strictness ladder `Vanilla < Optional < Required`.

```csharp
public enum TimfNetProfile
{
    Vanilla  = 0,  // Not in the handshake; vanilla clients can join
    Optional = 1,  // In the handshake; activate when the peer also has it
    Required = 2,  // In the handshake; kick a peer that lacks it or is too old
}

public static class TimfNetProfiles
{
    public static bool ParticipatesInHandshake(TimfNetProfile p);  // p >= Optional
    public static bool RequiresPeer(TimfNetProfile p);             // p == Required
    public static bool IsVanillaHostCompatible(TimfNetProfile p);  // p == Vanilla
}
```

---

## 4. Attributes

### `TimfModAttribute`

`[AttributeUsage(Class, Inherited = false, AllowMultiple = false)]`, optional. Without it, the first public, non-abstract `IMod` is the entry point and the side is inferred entirely from interfaces.

```csharp
public string Id { get; set; }              // Stable dependency ID; defaults to IMod.Name
public string Dependencies { get; set; }    // Comma-separated hard dependency IDs
public string LoadAfter { get; set; }       // Comma-separated soft ordering IDs
public bool LoadBeforeWorld { get; set; }   // Load after injection, before entering a world
public TimfSide Side { get; set; }
public bool SideSpecified { get; }          // True when Side was assigned
public TimfNetProfile Net { get; set; }     // Defaults to TimfNetProfile.Vanilla
```

> **`Side` is an assertion, not an override.** If specified, it must exactly match interface inference or loading fails. It documents the contract and protects against interface drift; it cannot declare an unimplemented capability.
>
> If `Net` is not `Vanilla`, the mod must have an `Authority` half or loading fails — without authority logic there is nothing to negotiate.

`LoadBeforeWorld` defaults to `false`. Mods without the flag load on entering a world and unload on returning to the main menu. Content mods are promoted to the pre-world phase automatically, as are hard dependencies of pre-world mods. Pure `Authority` mods still wait for authority activation before instantiation.

### `TimfHookAttribute`

Applied to **hook interfaces** to declare which process role may register them. The registry reads it during `Add` and enforces it.

```csharp
public TimfHookAttribute(TimfSide side);
public TimfSide Side { get; }
```

### `TimfDependsOnAttribute` · `TimfLoadAfterAttribute`

Both allow multiple uses.

```csharp
public TimfDependsOnAttribute(string modId);
public string ModId { get; }
public string MinVersion { get; set; }   // Optional; checked during loading

public TimfLoadAfterAttribute(string modId);
public string ModId { get; }
```

- `TimfDependsOn`: hard dependency. If the target is missing or fails to load, this mod **does not load**.
- `TimfLoadAfter`: soft ordering hint. A missing target **does not** cause failure.

#### Version format and comparison

`MinVersion` is checked during loading. If the target version is lower, this mod fails to load and the reason is logged.

The format is **1–4 dot-separated numeric segments**, optionally with a prerelease suffix and an optional leading `v`:

```
1.2        1.2.0        1.2.0.3        1.2.0-beta.1        v1.2.0
```

Compare numeric segments first; omitted segments are zero, so `1.2 == 1.2.0`. When numeric parts match, prerelease versions are lower than stable versions: `1.2.0-beta < 1.2.0`.

> **Invalid input fails closed.** If `MinVersion` or the target's `IMod.Version` cannot be parsed, the dependency is unsatisfied instead of being assumed valid. The same comparison is used for handshake version gates, where the peer's version is untrusted; permissive fallback would let malformed input bypass a `Required` version requirement.

Negative values, more than four segments, an empty string, and values such as `latest` are invalid. If a mod's own `Version` cannot be parsed, discovery logs a warning; the mod may still load, but it cannot satisfy any `MinVersion` dependency or handshake version check.

---

## 5. UI

### `IModSettings` — `[TimfHook(TimfSide.Client)]`

```csharp
void BuildSettingsUI(IImmediateModeUi ui);
```

> Build controls on `ui` only. **Do not call `Begin` / `End`**; the settings hub owns the outer window.

This is available only in a client process. An authority mod may implement it, but a session with a UI (single-player / host) is needed to open it; a dedicated server has no interface.

> Implementing `IModSettings` does **not** give a mod client capability and does not affect side inference. It answers “where may this hook be dispatched?”, not “does this mod need a client half?”.

### `IImmediateModeUi`

Provided by the TIMF.UI library mod. Obtain it through `IClientServices.Ui` or `context.Services`, and use it in `PostDraw`.

```csharp
bool IsReady { get; }                       // Textures / font are ready

bool Begin(string title);                   // False means collapsed/closed; End is still required
bool Begin(string title, ref bool open);
void End();
bool BeginChild(string id, float height, float width = 0f);   // Fixed-height scrollable child region
void EndChild();

void Text(string text);
void TextColored(string text, Color color);
void Separator();
void Spacing(float pixels = 6f);
void SameLine(float spacing = 8f);

bool Button(string label);
bool Selectable(string label, bool selected);                 // Full-row selection, useful for lists
bool Checkbox(string label, ref bool value);
bool SliderFloat(string label, ref float value, float min, float max);
bool InputFloat(string label, ref float value, float step = 0.1f);
bool TabBar(string id, string[] labels, ref int selectedIndex);
bool CollapsingHeader(string label, ref bool open);
bool InputText(string label, ref string value, int maxLength = 64);

Vector2 MousePosition { get; }              // UI logical coordinates
bool IsMouseClicked { get; }
bool WantCaptureMouse { get; }
bool WantCaptureKeyboard { get; }           // True when a text box has focus and consumes keyboard input
bool AnyWindowOpen { get; }
bool IsGameFocused { get; }
```

- `TabBar`, `InputText`, and `CollapsingHeader` return `true` when they change during the current frame. `TabBar` clamps the selected index to `[0, labels.Length)`.
- `InputText` uses the game's input path, so Chinese IME input and Ctrl+V paste work.

### `IUiHost`

The frame driver for the **UI library**. Ordinary gameplay mods do not need to implement it.

```csharp
void NewFrame(GameTime gameTime);
void Render();
void EarlyBlockGameInput();
```

Core calls `NewFrame` before mod `PostDraw` and `Render` afterwards. `EarlyBlockGameInput` must run before the game consumes clicks; intercepting input during drawing is already too late on the main menu.

---

## 6. Client hooks

The three registries share the same shape and are obtained from `IClientServices`; call `Add` from `Load`:

```csharp
void Add(THook hook);
void Remove(THook hook);
```

The corresponding registries are `IPlayerUpdateHookRegistry`, `IMapOverlayHookRegistry`, and `IInfoAccessoryHookRegistry`. All three hook interfaces carry `[TimfHook(TimfSide.Client)]`; `Add` is rejected and logged on a dedicated server.

### `IPlayerUpdateHook`

```csharp
void OnPreUpdate();
```

Core dispatches this from a Harmony prefix on `Player.ItemCheck` (local player only).

> It intentionally does **not** hook `Player.Update`: `Update` later calls `ResetControls`, so only dispatching from `ItemCheck` makes hook changes to `controlUseItem` and mouse aim effective.

### `IMapOverlayHook`

```csharp
void OnDrawMap(MapOverlayInfo info, ref string hoverText);
```

Called by a postfix on vanilla `MapIconOverlay.Draw`; both the full-screen map and minimap are supported.

> **The vanilla SpriteBatch is already open — do not call `Begin` / `End`.**

```csharp
public struct MapOverlayInfo
{
    public Vector2 MapPosition;       // Top-left of visible map area (tile coordinates)
    public Vector2 MapOffset;         // Screen offset of map drawing (pixels)
    public Rectangle? ClippingRect;   // Minimap clip rectangle; null for full-screen map
    public float MapScale;             // Tile-to-pixel scale
    public float DrawScale;            // Suggested icon draw scale
    public float Alpha;               // 0..1
    public bool Fullscreen;            // true = full-screen map, false = minimap/overlay

    public Vector2 WorldToMap(Vector2 worldPixels);  // Matches vanilla icon placement
    public bool Contains(Vector2 mapPos);            // Visible area, respecting minimap clipping
}
```

### `IInfoAccessoryHook`

```csharp
void OnRefreshInfoAccessories(object localPlayer);
```

Called after the local player's information-accessory flags are rebuilt: once per frame from `Player.UpdateEquips`, and when the inventory opens from `Player.RefreshInfoAccs`.

The player is passed as `object` so `TIMF.Abstractions` does not reference Terraria. Cast it to `Terraria.Player` inside the hook and set the required `acc*` fields.

---

## 7. Keybinds

Keybinds are registered in vanilla `PlayerInput.KnownTriggers`, so they appear under **Settings → Keybindings** and share the normal rebinding and save path.

### `IKeybindService`

```csharp
IKeybind Register(string id, string displayName, Keys defaultKey);  // Returns the existing binding when present
void Unregister(string id);
IKeybind Get(string id);                                            // Null when absent
bool TryGet(string id, out IKeybind keybind);
```

- `id` **must be globally unique**; use a format such as `"ModId.Action"`.
- `defaultKey` applies only when the current configuration has no binding yet.

### `IKeybind`

```csharp
string Id { get; }
string DisplayName { get; }
bool Current { get; }                    // Held this frame
bool JustPressed { get; }
bool JustReleased { get; }
string CurrentBindingDisplay { get; }    // E.g. "Insert"; empty when unbound
```

---

## 8. Localization

### `IModLocalization`

Obtained through `IModContext.L`; it automatically loads JSON key/value files from this mod's `Localization/` directory.

```csharp
string CurrentLanguage { get; }                        // E.g. "en-US", "zh-Hans"
string Get(string key, string fallback = null);
string Format(string key, params object[] args);       // Get, then string.Format
bool Has(string key);
```

The `Get` fallback chain is **current language → language base (`zh-Hans` → `zh`) → en-US → en → `fallback` → key itself**.

### `ILanguageService`

Framework-level language tracker, mirroring `Terraria.Localization.Language.ActiveCulture`.

```csharp
string CurrentLanguage { get; }     // "en-US" until the game is ready
event Action LanguageChanged;       // Fires after a change (also on the first poll)
```

---

## 9. Registries, sessions, and logging

### `IServiceRegistry`

Cross-mod service resolution bus. Ordinary mods resolve services through `IModContext.Services`; framework libraries such as `TIMF.UI` and `TIMF.Pinyin` publish shared capabilities here. To publish a custom service, use the caller-assembly-bound `IModContext.ServicePublisher` instead. Direct `Register` calls are reserved for trusted framework components; ordinary mods containing that call are rejected before loading to prevent replacement of `ISecurityCenter`, reflection proxies, or UI services.

```csharp
void Register<TService>(TService instance) where TService : class;
TService GetService<TService>() where TService : class;
bool TryGetService<TService>(out TService service) where TService : class;
```

```csharp
// IMyModApi must be declared by the current mod assembly.
context.ServicePublisher.Publish<IMyModApi>(new MyModApi());
```

The publisher rejects interfaces declared by the framework or other assemblies, mismatched instances, and duplicate registrations. A mod therefore cannot seize or replace an existing service.

### `IModRegistry` · `IModInfo`

```csharp
IReadOnlyList<IModInfo> Mods { get; }                                  // Includes disabled mods, discovery/load order
bool TrySetEnabled(string id, bool enabled, out string message);       // False when missing or rejected
```

> Core registers `IModRegistry` only **after discovery**, so resolve it lazily (for example in `PostDraw`) rather than from `Load`.

```csharp
public interface IModInfo
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    TimfSide Side { get; }              // Capability side inferred from interfaces
    TimfNetProfile NetProfile { get; }
    bool IsEnabled { get; }             // User switch; false skips loading / authority activation
    bool IsLoaded { get; }              // IMod.Load completed in this process
    bool LoadsBeforeWorld { get; }      // Explicit, content, or dependency-promoted pre-world flag
    IModFeatureToggle FeatureToggle { get; } // Non-null when actionable; in-world feature switch
    bool ServerLogicActive { get; }     // Authority half activated in this session
    IModSettings Settings { get; }      // Non-null when loaded and settings are allowed
    bool HasSettings { get; }
}
```

Core-returned entries also implement the optional `IModSessionState` extension. It does not extend `IModInfo`, preserving binary compatibility for existing consumers. UI that needs session controls can test `info as IModSessionState`:

```csharp
public interface IModSessionState
{
    bool IsSessionAllowed { get; }       // Current world/server may execute; user preference unchanged
    bool CanChangeEnabled { get; }       // Whether the primary switch can change now
    string InteractionLockReason { get; }
    bool HasSettingsCapability { get; }  // Type declares a settings page even if session-locked
    bool CanOpenSettings { get; }        // Settings page is currently actionable
}
```

Enable/disable rules:

- The main menu can change the user switch for any non-framework mod.
- After entering a single-player, host, dedicated-server, or multiplayer world, primary switches for all mods with `Authority` capability (`Authority` / `Both`) are locked. A pure `Client` mod can still be toggled locally.
- Before the handshake completes, a multiplayer client disables all local `Authority` / `Both` execution. Afterwards it allows only the intersection of the server-advertised set and locally enabled, version-compatible mods.
- A local dual-side/server mod not enabled by the server is disabled only for the session; its persistent `IsEnabled` preference is not changed. Returning to the main menu restores it.
- Session disabling also gates Core's `PostDraw`, player-update, map-overlay, information-accessory, content-accessory, and custom tile/wall placement dispatch. Its settings page must be shown as unavailable and `BuildSettingsUI` must not be called.

> To decide whether vanilla compatibility is broken, use `NetProfile`; do not switch on `Side`.

### `IVanillaTextures`

Read-only shared service registered by Core. It reads decoded XNA textures from the game's live `TextureAssets` arrays, so mods do not need to reference ReLogic assemblies or write their own reflection adapter. Use it only on client drawing paths; it may return `null` before resources load, when a field is absent, or on a dedicated server.

```csharp
public interface IVanillaTextures
{
    Texture2D Get(string arrayFieldName, int index);
}
```

For example, read a vanilla NPC head texture:

```csharp
if (context.Services.TryGetService(out IVanillaTextures textures))
{
    Texture2D head = textures.Get("NpcHeadBoss", headIndex);
}
```

`arrayFieldName` is a static array field on `Terraria.GameContent.TextureAssets`, such as `Npc`, `NpcHeadBoss`, or `Projectile`. Callers must still handle a null texture.

### `ITimfSession`

Session role and authority activation state. Registered before mod `Load`; updated as `netMode` changes.

```csharp
public enum TimfSessionKind
{
    Menu = 0,               // Main menu / not yet in a world
    SinglePlayer = 1,       // netMode 0
    Host = 2,               // Host & Play / listen server (netMode 2, not dedServ)
    DedicatedServer = 3,    // Main.dedServ
    MultiplayerClient = 4,  // netMode 1
}

public interface ITimfSession
{
    TimfSessionKind Kind { get; }
    bool ServerLogicEnabled { get; }
    bool RemoteTimfConfirmed { get; }                          // True after client handshake
    IReadOnlyList<ITimfRemoteModInfo> EnabledServerMods { get; } // Host list ∩ local mods on join
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

## 10. Weather

Obtain it from `IAuthorityServices.Weather`. Core registers all vanilla atmospheric channels at startup; other mods can register custom channels (IDs such as `modid.name` are recommended).

> **Write operations should run only when `IsAuthoritative` is true.**

### `IWeatherService`

```csharp
IReadOnlyList<IWeatherChannel> Channels { get; }              // Vanilla + plugins, category then ID order
void Register(IWeatherChannel channel);                       // Register or replace by ID
bool Unregister(string id);                                   // False when unknown
bool TryGet(string id, out IWeatherChannel channel);
IReadOnlyList<IWeatherChannel> GetByCategory(WeatherCategory category);
WeatherSnapshot Capture();
bool TrySet(string channelId, WeatherValue value, WeatherSetOptions options, out string error);
bool TryApplyBundle(WeatherBundle bundle, out string error);
void SetLock(WeatherBundle bundle, bool enabled);             // Reapply after each vanilla weather tick
bool IsLockEnabled { get; }
WeatherBundle LockedBundle { get; }                           // Null when unlocked
void SyncToClients();                                         // Broadcast WorldData
```

### `IWeatherChannel`

```csharp
string Id { get; }                          // Stable ID, e.g. vanilla.atmosphere.preset
string DisplayName { get; }
WeatherCategory Category { get; }
WeatherValueKind ValueKind { get; }
IReadOnlyList<string> Choices { get; }      // Used only by Choice channels
float? Min { get; }                         // Scalar closed interval; null = unbounded
float? Max { get; }
bool CanWrite { get; }
WeatherValue Read();
bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error);
```

### Value types

```csharp
public enum WeatherCategory  { Atmosphere = 0, Wind = 1, Moon = 2, Event = 3, Other = 4 }

public enum WeatherValueKind
{
    Toggle  = 0,   // Switch (blood moon, sandstorm, rain)
    Scalar  = 1,   // Continuous value (rain 0–1, wind −1.5–1.5)
    Integer = 2,   // Discrete integer (moon phase 0–7)
    Choice  = 3,   // Named choice from IWeatherChannel.Choices
}

public struct WeatherValue
{
    public bool? BoolValue;
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
    public bool Instant = true;       // Skip fades where the channel supports it
    public bool SyncNetwork = true;   // Broadcast MessageID.WorldData
}

public sealed class WeatherBundle    // Composite change; null fields mean “leave unchanged”
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

public sealed class WeatherSnapshot   // Return value of Capture()
{
    public float WindSpeed;      public int  MoonPhase;
    public bool Raining;         public float RainIntensity;
    public bool Sandstorm;       public bool SlimeRain;
    public bool BloodMoon;       public bool PumpkinMoon;
    public bool FrostMoon;       public bool LanternNight;
    public int   CloudCount;
    public Dictionary<string, WeatherValue> Channels;   // Indexed by IWeatherChannel.Id
    public string Summary;
}
```

### Built-in channel IDs

Constants in `WeatherChannelIds`:

| Constant | Value |
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

`WeatherChannelIds.AtmospherePresets` provides the `AtmospherePreset` choices: `clear`, `cloudy`, `light_rain`, `rain`, `heavy_rain`, `storm`, `blizzard`, `sandstorm`, `windy`, and `slime_rain`.

---

## 11. Prefixes

### `IPrefixService`

Obtain it from `IAuthorityServices.Prefix`. At startup, Core enumerates all vanilla best prefixes; mods can register overrides for custom items.

```csharp
void RegisterBestPrefix(int itemType, int prefixId);
bool TryGetBestPrefixes(int itemType, out IReadOnlyList<int> prefixIds);
bool TryGetRandomBestPrefix(int itemType, out int prefixId);
```

> An item can have multiple best prefixes (for example, accessories); reforging selects one at random.

---

## 12. Custom content

Custom content lives in the separate `TIMF.Content.dll` / `TIMF.Content` namespace. A content mod must implement `IContentMod` and use `Net = TimfNetProfile.Optional` or `Required`; custom IDs cannot interoperate with a pure-vanilla peer.

### Stability contract

The following rules are stable content API contracts:

- **Content keys are persistent identities.** `ModId/InternalName` is used by ID allocation and all sidecar saves. Runtime `Type` is only a numeric index for the current process and must not be written to long-term mod data.
- **Vanilla saves remain vanilla-readable.** Mod items, tiles, walls, custom containers, NPC IDs, and custom Buff IDs are not written into `.plr` / `.wld`; without TIMF, vanilla can still read the main file and simply cannot see sidecar content.
- **Saving cannot corrupt live state.** Items, tiles, walls, chests, and NPCs temporarily removed for vanilla serialization are restored on both success and exception paths. Sidecars use same-directory temporary files, flushes, and atomic replacement.
- **A temporarily missing mod does not delete content.** Unresolved content keys remain. A conflicting old record is discarded only when the player creates new content at the same coordinate or slot.
- **Registration order is not save identity.** Stable ID tables are keyed by content key; adding another mod or changing load order must not change the meaning of released content.
- **Public types are the compatibility boundary.** Public members in `TIMF.Abstractions` and `TIMF.Content` are author-facing API. `TIMF.Core`, Harmony patches, and reflection helpers are implementation details.

Do not casually rename released `ModId` or `InternalName`. Display names, help text, and texture paths may change, but migrating persistent content keys requires a future explicit alias mechanism; reassigning numeric IDs is not a substitute.

The stability contract does not guarantee binary compatibility across arbitrary Terraria versions. After a game update, run the `ContentTestKit` array, placement, drop, save, and recipe tests, then validate production saves against a backup world.

```csharp
[TimfMod(Id = "MyContent", Net = TimfNetProfile.Required)]
public sealed class MyContentMod : IContentMod
{
    public void AddContent(IContentRegistry registry)
    {
        registry.AddItem<MyPlaceableItem>();
        registry.AddTile<MyTile>();
        registry.AddWall<MyWall>();
        registry.AddNpc<MyMerchant>();
        registry.AddBiome<MyBiome>();
        registry.AddProjectile<MyProjectile>();
        registry.AddBuff<MyBuff>();
    }

    // IMod members omitted.
}
```

### `TimfItem` / `TimfPetItem` / `TimfTile` / `TimfContainerTile` / `TimfWall`

These definitions expose stable `InternalName`, `ContentKey`, a runtime-assigned `Type`, `Texture`, and `SetStaticDefaults()`. `TimfItem.SetDefaults()` configures each item instance; `TimfTile.SetStaticDefaults()` writes tile sets such as `Main.tileSolid[Type]` and `Main.tileFrameImportant[Type]`.

Item environment flags use the expanded vanilla sets and should be assigned in `TimfItem.SetStaticDefaults()`, for example `ItemID.Sets.ItemNoGravity[Type]`, `IsLavaImmuneRegardlessOfRarity[Type]`, and `CanFishInLava[Type]`. Do not access these arrays before `SetDefaults()` or cache references from before expansion.

```csharp
public sealed class MyTile : TimfTile
{
    public static int RegisteredType { get; private set; }

    // May return a vanilla item ID or an assigned custom TimfItem.Type.
    // The default 0 means no item drops after mining.
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

`TimfTile.ItemDrop` and `ItemDropStack` use vanilla's `WorldGen.KillTile_GetItemDrops` pipeline. Entities are created only in single-player or on the server; vanilla explosion, hammer-failure, and `noItem` rules still apply, with no duplicate drops.

Items can override `AddRecipes()` and use `TimfRecipe` to register recipes containing vanilla or mod items and mod crafting stations. It runs after all content has received IDs and `SetStaticDefaults()`, so other `RegisteredType` values are safe to reference:

```csharp
public override void AddRecipes()
{
    TimfRecipe.Create(MyResult.RegisteredType, 5)
        .AddIngredient(MyMaterial.RegisteredType, 1)
        .AddTile(MyWorkbenchTile.RegisteredType)
        .Register();
}
```

The current recipe API supports a result stack, multiple ingredients, and one crafting station. Recipes registered later do not support shimmer decomposition by default, avoiding inconsistent use of an already-initialized vanilla decomposition table.

Special tiles that need anchor rules can copy vanilla `TileObjectData` through `PlacementTemplateTile`. A custom torch can return `TileID.Torches` to gain floor, side, and background-wall anchors while retaining its own tile ID and texture. Lit tiles override `ModifyLight(i, j, ref red, ref green, ref blue)`; the framework takes the component-wise maximum with vanilla ambient light.

Decorative tiles, small crystals, and other objects with limited behavior inherit `TimfTile`:

- `RightClick()` handles right-clicks; `HitWire()` handles wire pulses.
- A simple one-tile state can return `PreserveFrameData = true` and maintain `frameX/frameY`; the framework still skips vanilla framing for unknown mod IDs.
- `RandomUpdate()` runs only when vanilla samples that coordinate for a random update.
- `NearbyEffects()` handles nearby-player effects; `CanKillTile()` controls whether it can be destroyed.
- `BreaksInstantly = true` means one valid pick hit destroys it, useful for loose rocks, plants, and small crystals. Drops remain controlled separately by `ItemDrop`; the default `0` means no drop.
- A non-zero `ConveyorVelocity` pushes players, NPCs, and dropped items horizontally.
- Grass seeds inherit `TimfGrassSeedItem` and point to a `TimfGrassTile` through `GrassTileType`. `TimfGrassTile.CanGrowOn()` accepts one or more substrates and constrains both seed conversion and natural-spread targets. `CanSpreadAt()` provides framework-bounded four-neighbor spreading; mods cannot scan or bulk-rewrite the world themselves. As with vanilla grass seeds, the result is conversion of a mud/dirt tile into a grass tile, not a second layer. The framework stores the replaced substrate by coordinate in the world sidecar and restores it when grass is mined; `DefaultSubstrateTileType` is only for old saves without source records. World-generation APIs are not open yet.

`RightClick()` state changes currently run only in single-player; multiplayer clients reject local execution to prevent ghost state. A wire event received by the server runs `HitWire()` and synchronizes the tile. Client-initiated tile actions will require a versioned TIMF content-action message before they are opened.

Chest tiles inherit `TimfContainerTile`. The default copies vanilla's 2×2 chest `TileObjectData` and the hook that creates a `Chest` after placement. The definition must still mark the runtime sets as a container:

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

The framework reuses vanilla's 40-slot chest UI, renaming, and “non-empty chests cannot be destroyed” rule. An empty chest drops `ItemDrop` exactly once rather than once for each of its four tiles.

`TimfWall` provides an independent Wall ID, `Texture`, `ItemDrop`, and `SetStaticDefaults()`. Set a wall item’s `Item.createWall = MyWall.RegisteredType` in `TimfItem.SetDefaults()`. Custom wall textures use vanilla's 144×180 wall-frame layout.

`TimfPetItem` is the safe item-side pet API. Override `PetBuffType` to activate the pet buff; light pets must also override `PetSlot => TimfPetSlot.LightPet`. The framework forces `Item.buffType` and vanilla pet-category sets, so drag-and-drop and quick-equip use the normal pet/light-pet slots. Equipment state is refreshed through `Player.UpdatePet` / `UpdatePetLight`. `PetBuffDuration` and `OnPetActivated()` can adjust active-use behavior. After overriding `PetProjectileType`, the framework checks after equipment refresh whether the player already has the projectile and creates it only when the count is zero. This can point to a vanilla pet projectile or a registered `TimfProjectile` without spawning duplicates every frame. Follow, teleport, and AI remain the responsibility of the Buff/Projectile.

### Custom NPCs

NPCs inherit `TimfNpc` and are registered with `AddNpc<T>()`. The framework assigns stable NPC IDs from content keys, expands `NPCID.Sets`, `Main.npcFrameCount`, textures, name caches, and type-indexed `SceneMetrics` collections. Configure instance fields in `SetDefaults()` (`width/height`, `lifeMax/life`, `damage/defense`, `aiStyle`, `knockBackResist`, `value`, `npcSlots`, `boss`, `friendly`, `townNPC`, `noGravity/noTileCollide`, `HitSound/DeathSound`, and so on).

- `Texture` may point to a bundled PNG or reuse a vanilla or registered-item sprite as a placeholder (for example, `Content/TestSword`). Declare vertical frame count with `FrameCount`; a single-frame sprite keeps the default.
- With `RunVanillaAI = true`, vanilla `aiStyle` behavior continues after custom `AI()`. With `RunVanillaFrame = true`, vanilla `FindFrame` calculation is reused; the framework clamps the rectangle back to texture bounds so an NPC reusing a small sprite cannot sample an out-of-range frame.
- `IsTownNpc = true` sets `townNPC` / `friendly`; override `GetChat(Player)` to provide dialogue.

Town NPCs can return `TimfShopEntry` from `GetShop(Player)` (`ItemType`, `Stack`, `CustomPrice`, and optional `Condition`). The framework adds a “Shop” button and opens vanilla's shop UI with appended slots. `GetDailyQuests(Player)` returns `TimfDailyQuest` entries (required item/count, `TimfQuestReward`, and `TimfQuestStatusEffect`); the framework adds a “Quest” button and handles delivery and settlement.

An NPC with `boss = true` receives vanilla's bottom boss bar and minimap head. The framework points `NPCID.Sets.BossHeadTextures[type]` to an existing vanilla boss-head placeholder rather than expanding `NpcHeadBoss`, avoiding renderers that captured the old array length. A custom boss spawned through `NPC.NewNPC` also receives the localized “... has awoken!” broadcast that vanilla normally emits only from `SpawnBoss`.

```csharp
public sealed class MyMerchant : TimfNpc
{
    public override string DisplayName => "My Merchant";
    public override string Texture => "Content/MerchantSprite";
    public override bool IsTownNpc => true;
    public override bool RunVanillaAI => true;
    public override bool RunVanillaFrame => true;

    public override void SetDefaults()
    {
        Npc.width = 18; Npc.height = 40;
        Npc.lifeMax = 250; Npc.life = 250; Npc.defense = 10;
        Npc.aiStyle = 7; Npc.friendly = true; Npc.townNPC = true;
    }

    public override string GetChat(Player player) => "Buy something, will ya?";

    public override IReadOnlyList<TimfShopEntry> GetShop(Player player) => new[]
    {
        new TimfShopEntry { ItemType = MyMaterial.RegisteredType, Stack = 1, CustomPrice = 100 }
    };
}
```

Custom NPC drawing is owned by the framework. Vanilla `Main.DrawNPCs` filters with `type < NPCID.Count`, but that comparison was inlined into bytecode when `Terraria.dll` was compiled, so changing `NPCID.Count` at runtime cannot make it see expanded IDs. Core draws each framework NPC in a postfix with its actual texture, clamped frame, lighting color, and vanilla-compatible coordinates. IDs beyond vanilla's network-protocol assumptions require `Optional` or `Required`; content mods cannot share them with a peer that lacks the same content.

### Custom projectiles

Projectiles inherit `TimfProjectile` and are registered with `AddProjectile<T>()`. The framework assigns stable projectile IDs, expands `ProjectileID.Sets`, `Main.projFrames/projHostile/projHook/projPet`, textures, and language caches, and backfills `Player.ownedProjectileCounts` that were created during startup. Implement `SetDefaults()`, `AI()`, `OnHitNpc()`, `OnHitPlayer()`, and `OnKill()` as needed; with `RunVanillaAI = true`, vanilla `aiStyle` runs after custom `AI()`.

```csharp
public sealed class MyBolt : TimfProjectile
{
    public static int RegisteredType { get; private set; }
    public override bool RunVanillaAI => true;

    public override void SetStaticDefaults() => RegisteredType = Type;

    public override void SetDefaults()
    {
        Projectile.width = 10;
        Projectile.height = 10;
        Projectile.friendly = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 300;
        Projectile.aiStyle = 1;
    }

    public override void OnHitNpc(NPC target) => target.AddBuff(BuffID.OnFire, 180);
}
```

Set `Item.shoot = MyBolt.RegisteredType` and `Item.shootSpeed` in `TimfItem.SetDefaults()`. Projectiles are short-lived network entities and are not written to world or player saves. Projectile IDs remain within vanilla's Int16 network range; content mods still need `Optional` or `Required` and cannot exchange custom projectiles with a peer lacking the same content.

### Custom buffs and debuffs

Status definitions inherit `TimfBuff` and are registered through `AddBuff<T>()`. `IsDebuff` controls the debuff flag, `CanBeCleared` controls nurse removal, and `Save` controls whether the effect survives character exit. `Update(Player, ref buffIndex)` runs on each effective tick. The framework expands Buff collections, textures, name/description caches, and `buffImmune` for existing players/NPCs, but `TimfBuff.Update` currently targets player effects; complex custom NPC state machines are not public.

```csharp
public sealed class QuestBlessing : TimfBuff
{
    public static int RegisteredType { get; private set; }
    public override string DisplayName => "Quest Blessing";
    public override string Description => "+8 defense";
    public override void SetStaticDefaults() => RegisteredType = Type;
    public override void Update(Player player, ref int buffIndex) => player.statDefense += 8;
}
```

Apply it with `player.AddBuff(QuestBlessing.RegisteredType, durationTicks)`. Saveable custom state is temporarily cleared before vanilla writes `.plr`, then restored to the live character and written by content key to `<player>.plr.timfbuffs`. The sidecar uses temporary files, flushes, atomic replacement, and a backup. Corrupt or unknown-version sidecars are preserved and never overwritten; records for temporarily missing mods are preserved too. `Save = false` effects are not written to `.plr` or the sidecar, but the save operation does not accidentally clear their runtime state.

### Custom NPCs, shops, and daily quests

NPC definitions inherit `TimfNpc`, are registered through `AddNpc<T>()`, and receive runtime IDs, expanded NPC index arrays, textures, and bridges for `SetDefaults()`, `AI()`, `FindFrame()`, and `GetChat()`. `IsTownNpc` sets the friendly town-NPC flags; vanilla logic runs after custom callbacks only when `RunVanillaAI` / `RunVanillaFrame` explicitly returns `true`.

`GetShop()` returns `TimfShopEntry` values for items, counts, custom prices, and player conditions. `GetDailyQuests()` returns `TimfDailyQuest` values; the framework deterministically rotates one quest by world quest day and NPC content key, checks and removes the requested items, awards `TimfQuestReward` through vanilla pickup/overflow handling, and applies `TimfQuestStatusEffect` entries. Each player can complete a quest once per day. Submission is currently single-player-only; multiplayer must wait for an authoritative custom-content message protocol and may not issue rewards locally.

`SaveToWorld` controls whether an NPC is written to a sidecar; town NPCs default to enabled. During vanilla `.wld` saving, running custom NPCs are hidden and persistent instances are written by content key to `<world>.wld.timf-npcs`; live objects are restored on both success and exception. Loading resolves records back to current runtime IDs, while temporarily missing-mod records remain.

### Custom biomes

`TimfBiome.IsActive(player, sceneMetrics, content)` evaluates membership from the player's position and expanded `SceneMetrics`, not saved numeric IDs. `OnEnter()` and `OnLeave()` run once per boundary change; `Update()` runs while active from vanilla `Player.UpdateBiomes`. SceneMetrics currently dispatches only to local render players, making it suitable for client environment presentation; dedicated-server authority effects await a per-player scan pipeline. Backgrounds, music, spawn pools, and world generation are not public; mods must not rewrite these tables through reflection.

`IContentLookup` is available from `context.Services` and provides `ItemType<T>()`, `TileType<T>()`, `NpcType<T>()`, `ProjectileType<T>()`, `BuffType<T>()`, matching `Get*()` methods, `IsBiomeActive<T>()`, `RegisteredItems`, `RegisteredTiles`, `RegisteredWalls`, `RegisteredNpcs`, `RegisteredBiomes`, `RegisteredProjectiles`, `RegisteredBuffs`, and diagnostic `Report()`.

### Tile and wall save rules

Custom tile and wall IDs are never written to `.wld`. Before vanilla world serialization, Core temporarily removes them and writes complete tile state to `<world>.wld.timf-tiles` beside the world file, restores memory immediately after saving, then overlays the sidecar when the world is loaded and playable. Unresolved records remain when a content mod is removed. If the player places a vanilla tile at that coordinate, the new change wins and the old record is discarded on the next save.

### Item and container save rules

Mod items are not written to player or world saves by runtime numeric ID:

- Inventory, equipment, dyes, pet/mount slots, the four personal storage spaces (piggy bank, safe, Defender's Forge, and Void Vault), and equipment loadouts are written to `<player>.plr.timfitems`.
- Mod items in vanilla world chests, and entire custom `TimfContainerTile` containers, are written to `<world>.wld.timf-chests`.
- Custom item and container identity uses `ModId/InternalName`; reassigning IDs does not change save meaning. Vanilla items inside custom containers keep vanilla item IDs.
- During `.wld` writes, custom container entities are temporarily removed from `Main.chest`, and mod-item slots in vanilla chests temporarily become air. Live objects are restored after both successful and failed saves; sidecars use temporary files, flushes, and atomic replacement.
- When a content mod is temporarily missing, unresolved records remain and can be restored after reinstall. If the player creates new content at the missing slot or position, the current change wins and the old record cannot overwrite it.

> `InternalName` is part of save identity and must not be changed after release. Tile textures must ship with the mod and follow the same path convention as items, defaulting to `Content/<InternalName>.png`.

### Explicitly unsupported content boundaries

- Safe world-generation or biome-terrain rewrites;
- Custom NPC spawn pools, NPC custom Buff state machines, networked complex combat/quest state, and declarative background/music replacement;
- Declarative pet-follow/teleport AI templates (equipment slots, Buffs, and unique projectile lifetime are available).

These capabilities cannot be bypassed with direct Harmony, `MethodInfo.Invoke`, or writes to vanilla fixed arrays. A new pipeline first needs stable IDs, array-coverage validation, texture injection, network authority, and a content-key sidecar strategy.

---

## 13. Security and sensitive permissions

### Security-first principles

Loading a DLL only allows it to use publicly exposed game and framework capabilities; **it does not grant arbitrary local-machine permissions**. All new services and mod APIs follow these rules:

- **Deny by default.** Without verifiable authorization, sensitive operations must not run. Missing authorization UI, a non-interactive dedicated server, a timeout, or an unparseable authorization record all mean denial.
- **Least privilege.** Requests are limited to the smallest path, operation, command, and duration needed for the current feature. “Allow file access” cannot mean an entire disk, permanent writes, and arbitrary commands.
- **Explain before executing.** Before a sensitive operation, the framework shows the mod identity, behavior, target path or program, command arguments, purpose, scope, and lifetime. A mod cannot forge the authorization prompt.
- **Revocable and auditable.** Users can inspect and revoke persistent grants. The framework records grants, denials, and results without logging secrets such as tokens, passwords, or complete private files.
- **Validate the real path.** Normalize absolute paths before checking the allowed scope and prevent escapes through `..`, symbolic links, junctions, case tricks, or network paths.
- **Denial has no side effects.** A denied operation must not create a temporary file, truncate a target, start a child process, or read partial data before reporting failure.

### Operations that require authorization

These are sensitive at minimum and cannot become allowed merely because `IMod.Load()` has run:

| Operation | Default | Must be shown during authorization |
|---|---|---|
| Read files or directories outside the mod workspace | Denied | Canonical path, read scope, purpose |
| Create, modify, overwrite, move, or delete files | Denied | Target, operation, overwrite intent, purpose |
| Execute a shell, command-line program, script, or child process | Denied | Executable, complete arguments, working directory, purpose |

The default “mod workspace” contains only read-only resources shipped in `ModDirectory` / `ContentDirectory`. Even within that workspace, writing and executing remain sensitive. Fixed writes performed by the framework itself for logs, config, content-ID tables, and save sidecars are declared Core behavior, restricted to the framework directory or game-save sidecar location, and cannot be borrowed as an arbitrary file-writing channel.

Prefer one-operation or one-session authorization. Persistent grants require an explicit user choice and bind the mod's stable identity, permission kind, and canonical target. If the binary or capability expands, request again. A dedicated server accepts only precise administrator configuration; it must not auto-allow after a prompt fails to appear.

### Implemented authorization and warning UI

`IModContext.Security` provides an identity-bound proxy. A request starts as `Pending` and opens the security center. The user can deny, allow once, allow until the current TIMF process exits, or persistently allow the **exact same** file operation. Persistent grants bind the mod ID, assembly SHA-256, behavior, canonical target, overwrite intent, and purpose. Process execution supports only one-time or current-process grants, not persistent authorization. The Mod Settings home continuously displays the isolation-boundary warning and can open the security center to revoke persistent grants.

```csharp
using TIMF.Abstractions.Security;

// The first call submits a request; it does not read the file.
var examplePath = System.IO.Path.Combine(
    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "example.bin");
var request = context.Security.RequestFileRead(
    examplePath, "Import the map selected by the user");

// Query on later frames; execute through the proxy only after explicit approval.
request = context.Security.GetRequest(request.Id);
if (request.Status == SensitiveOperationStatus.Granted)
{
    byte[] bytes = context.Security.ReadAllBytes(request.Id);
}
```

Complete proxy table:

| Request | Execution after approval | Important constraints |
|---|---|---|
| `RequestFileRead(path, purpose)` | `ReadAllBytes(requestId)` | Absolute path; reject reparse points at every level |
| `RequestFileWrite(path, overwrite, purpose)` | `WriteAllBytes(requestId, data)` | Exact target, separate overwrite intent, same-directory atomic temp-file commit |
| `RequestProcess(exe, args, cwd, purpose)` | `RunProcess(requestId, timeout)` | exe/cwd must be absolute existing paths; no shell; timeout capped at 5 minutes |

Denied, cancelled, undecided, invalid, or foreign request IDs cannot execute. One-time authorization is consumed before execution and is not restored after an I/O failure. Dedicated servers currently have no interactive UI or administrator-preauthorization format, so requests are denied; requests also fail immediately when TIMF.UI is unavailable, and a decision that remains pending for five minutes is denied rather than implicitly allowed.

### Pre-load static security audit

The main assembly and same-directory private dependencies in a mod package are scanned before any mod constructor, static initializer, or `Load()` runs. The entire mod is rejected when the following traces are found:

- Direct `System.IO.File`, `Directory`, `FileStream`, and similar filesystem access (`Path`, memory streams, and in-memory text readers are exempt);
- `Process` / `ProcessStartInfo`, P/Invoke, internal calls, `calli`, `Marshal`, and native DLLs;
- Direct networking, sockets, or registry access;
- `Reflection.Emit`, dynamic assembly loading, reflection `Invoke` / `CreateDelegate`, and runtime expression compilation;
- Direct creation or control of Harmony patches;
- Direct calls to the raw service-registration entry point, environment-variable reads/writes, or runtime-compiled code;
- Bundled copies of `TIMF.Abstractions`, `TIMF.Content`, or Harmony DLLs with the same name, or assembly-identity/actual-path conflicts;
- Method bodies, metadata, or dependencies that cannot be completely resolved. Audit failure rejects loading; it does not degrade to a warning.

Rejection results are logged by Core and open the security center. The Mod Settings home shows the rejected-mod count, while the security center lists assemblies, methods, and matched APIs. There is no ordinary-mod whitelist. The only component outside the ordinary audit is the bundled `TIMF.UI`, which must match the exact relative path and SHA-256 generated by `trusted-framework-components.v1`; a replaced file or missing manifest is also rejected.

Discovery no longer instantiates `IMod` to read name/version. Simple constant properties are read from IL metadata; other cases fall back to the type name and assembly version, ensuring no mod code runs before the audit.

### Restricted storage and compatibility proxies

Ordinary configuration does not require a sensitive prompt, but it must use `IModContext.Storage`:

```csharp
if (context.Storage.ConfigExists("MyMod.json"))
    json = context.Storage.ReadConfigText("MyMod.json");
context.Storage.WriteConfigText("MyMod.json", json);

byte[] icon = context.Storage.ReadContentBytes("Images/Icon.png");
```

Config is confined to `config/mod-data/<ModId>/`, accepts only a single safe filename, and writes atomically. Packaged resources can be read only relative to this mod's `ContentDirectory`; all paths reject reparse points and directory escapes. An old `config/<ModId>.json` is copied to the new directory by Core on first use only when its filename matches the mod ID after punctuation is ignored; this preserves settings without opening another mod's config.

For private Terraria APIs, direct `MethodInfo.Invoke` and Harmony are forbidden:

- `ITerrariaReflection.Invoke` accepts only methods declared by `Terraria.exe`, whose names contain none of the sensitive file/save/load markers, and which take no string or stream parameters.
- `IModContext.Patches` installs prefix/postfix hooks only on Terraria methods that pass the same checks. Callbacks must be static methods declared by this mod assembly; transpilers, arbitrary Harmony IDs, and Core/BCL patching are not exposed.

These proxies support game compatibility; they are not general reflection entrances that bypass `Security`.

### Current isolation boundary

TIMF can reliably constrain operations executed through `IModContext.Security`. Ordinary .NET Framework mod DLLs still run fully trusted inside the game process, so the framework cannot reliably intercept direct `System.IO`, `Process`, P/Invoke, or native-code loading. Security Center and Mod Settings display this warning and do not call an existing authorization UI a process sandbox.

Supported mods must not bypass the proxy. Static audit blocks direct calls, ordinary delegates, private dependencies, native dependencies, dynamic invocation, and direct Harmony — common bypasses, not a mathematical proof that arbitrary managed code is harmless. Obfuscators, runtime vulnerabilities, or uncovered indirect calls may still escape. True isolation requires moving untrusted code out of the Terraria process.

`ISecurityCenter` exposes only pending/rejected counts, the boundary warning, and the ability to open the window. Actual approval, revocation, and audit entry points remain inside Core; a requesting mod cannot approve itself through the public service bus.

For manual validation, open **Mod Settings → Content Test Kit → Security authorization pipeline test** in the main menu. Submit a test request for the Core log, inspect the pending warning and the security-center decision, then click execute again after approval. The test reports only the byte count and never displays log contents.

---

## Appendix: TIMF.UI library

`libs/TIMF.UI` exposes only one public type, `TimfUiMod`; mod authors **should not reference it directly**.

During `Load`, it registers `IImmediateModeUi` and `IUiHost` with `context.Services`. Consumers should obtain the interfaces through `IClientServices.Ui` or `context.Services`.

Depend on it with `[TimfDependsOn("TIMF.UI")]`.
