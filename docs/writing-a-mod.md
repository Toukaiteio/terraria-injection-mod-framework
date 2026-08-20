# Writing a TIMF Mod

Language: **English** | [简体中文](writing-a-mod.zh-CN.md)

The shortest path from a new project to a working mod. For API details, see the [API Reference](./api-reference.md); for side concepts, see the [Side and Protocol Model](./side-model.md).

> **Security requirement:** Being loaded does not grant a mod local machine permissions. Do not read files outside `ModDirectory` / `ContentDirectory`, write files independently, or invoke a shell, script, or `Process.Start`. Such actions must submit an exact request through `context.Security` and execute through the same framework proxy after the user authorizes it. TIMF explicitly warns that the current same-process .NET DLL arrangement is not a reliable sandbox, so supported mods must not bypass the proxy. Before executing any mod code, the loader scans the main DLL and private dependencies; direct file/process/network/PInvoke, dynamic invocation, and direct Harmony traces cause the entire mod to be rejected.

## 1. Create a project

Choose either approach.

### Option A: Mod SDK template (recommended for standalone development)

Use the **Mod SDK** from a release package (`dist\ModSDK\` produced by `build.ps1`) to generate a skeleton in one command. `net48` / `x86`, framework and Terraria/XNA references, and post-build packaging are provided by `TIMF.Mod.props`; no manual references are needed:

```powershell
setx TIMF_SDK      "<ModSDK path>"            # One-time; restart the terminal afterwards
setx TIMF_TERRARIA "<your own Terraria.exe>"  # One-time; a legitimate game copy

dotnet new install <ModSDK>\templates\timf-mod
dotnet new timf-mod -n MyMod --display "My Mod"
cd MyMod
dotnet build -c Release          # Produces a deployable dist\MyMod\
```

The generated `MyMod` already includes configuration, localization, and an `IClientMod` skeleton. Jump directly to [step 4](#4-get-services) to implement behavior.

### Option B: `mods\` inside this repository

Create a class library under `mods\<Id>\`:

- Target **`net48`** and platform **`x86`** (the game is a 32-bit process).
- Reference `TIMF.Abstractions` from `src\TIMF.Abstractions\bin\Release\net48\TIMF.Abstractions.dll`.
- Add `Terraria.exe` and the XNA assemblies under `lib\xna` when needed.

Keeping the directory name, csproj name, and `[TimfMod(Id=...)]` value aligned is easiest: `build-mods.ps1` requires exactly one `<Name>.csproj` in each mod directory.

## 2. Choose a side

Start with one question: **where does this logic run?**

```
Only changes the local player's view or input (UI, aim assistance, map icons, automatic item use)
  └─→ IClientMod

Changes world state (drops, NPCs, weather, economy)
  ├─ Changes expressible in vanilla packets ────────→ IAuthorityMod (default is enough)
  └─ Requires the peer to install the same code ─────→ IAuthorityMod + [TimfMod(Net = Optional/Required)]

Needs both (for example, vanilla-safe host logic + its own settings UI/overlay)
  └─→ IClientMod + IAuthorityMod
```

> The default protocol profile is `Vanilla`, so vanilla compatibility is preserved. Raise it to `Optional` / `Required` only when the peer must install the same code — `Required` makes your host kick pure-vanilla players.

## 3. Minimal skeleton

### Client mod

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

            // Client is null on a dedicated server — always check it.
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
            // Called every frame before the local player's ItemCheck.
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            // Draw controls only; do not call Begin/End.
            ui.Text(_ctx.L.Get("Settings.Hint", "Hello"));
        }

        public void PostDraw(GameTime gameTime) { }
    }
}
```

### Authority mod (vanilla-compatible)

```csharp
[TimfMod(Id = "LootBoost")]                      // No Net: defaults to Vanilla
public sealed class LootBoostMod : IAuthorityMod, IAuthorityLifecycle
{
    public string Name => "Loot Boost";
    public string Version => "1.0.0";

    public void Load(IModContext context) { }
    public void Unload() { }

    public void OnAuthorityActivate(IModContext context)
    {
        // Activation does not mean authority. Handshake-profile mods also activate on clients.
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

> A pure `Authority` mod is **loaded lazily**: it loads when a session (single-player / host / dedicated server) begins and unloads when the session ends.

### In-world feature switch

If the mod needs to temporarily disable its main functionality after entering a world, implement `IModFeatureToggle` and bind the property to the mod's own configuration:

```csharp
public sealed class MyMod : IClientMod, IModSettings, IModFeatureToggle
{
    // _config and SaveConfig() represent your own configuration layer;
    // this example shows only the toggle contract.
    public bool FeatureEnabled
    {
        get { return _config.Enabled; }
        set
        {
            _config.Enabled = value;
            SaveConfig();
        }
    }
}
```

`FeatureEnabled` should change only lightweight configuration state; drawing, hooks, and gameplay logic should read it on each frame or callback. It does not call `Load`, `Unload`, or reapply patches. The F9 settings hub displays it through `IModInfo.FeatureToggle`, while the primary mod switch remains a main-menu-only setting. `SaveConfig()` represents your own save routine; use `context.Storage` for its implementation.

### Authority mod that requires both peers

```csharp
[TimfMod(Id = "MyRules", Net = TimfNetProfile.Required)]
public sealed class MyRulesMod : IAuthorityMod, IAuthorityLifecycle { /* ... */ }
```

## 4. Get services

```csharp
public void Load(IModContext context)
{
    // Side-scoped services (preferred)
    var ui       = context.Client?.Ui;            // null when TIMF.UI is not installed
    var keybinds = context.Client?.Keybinds;
    var weather  = context.Authority.Weather;     // Authority is never null

    // Cross-mod service bus
    if (context.Services.TryGetService(out IWeatherService w)) { }

    // Publish interfaces declared by this assembly; framework and other mod services cannot be replaced.
    context.ServicePublisher.Publish<IMyModApi>(new MyModApi());

    // Localization/*.json belonging to this mod
    var title = context.L.Get("Window.Title", "My Mod");
}
```

`IModRegistry` is the exception: Core registers it only after discovery, so resolve it lazily (for example, in `PostDraw`) instead of retrieving it from `Load`.

## 5. Common capabilities

| Goal | Use |
|---|---|
| Change local-player input every frame | `IPlayerUpdateHook` + `IClientServices.PlayerUpdate` |
| Draw on the map or minimap | `IMapOverlayHook` + `IClientServices.MapOverlay` |
| Add an information accessory effect | `IInfoAccessoryHook` + `IClientServices.InfoAccessories` |
| Register a hotkey in the vanilla settings UI | `IKeybindService.Register("MyMod.Toggle", ...)` |
| Provide a settings page | `IModSettings` |
| Toggle main functionality in-world | `IModFeatureToggle` (configuration switch; does not load/unload) |
| Change weather | `IAuthorityServices.Weather` |
| Reuse vanilla textures | `context.Services.GetService<IVanillaTextures>()` |
| Read external files, write files, or run a process | Request and proxy through `IModContext.Security` |
| Save this mod's config / read packaged resources | `IModContext.Storage` (restricted directory; no sensitive authorization required) |
| Invoke a private Terraria method | `ITerrariaReflection` |
| Apply a compatibility patch to a Terraria method | `IModContext.Patches` (prefix/postfix only) |
| Publish this mod's cross-mod interface | `IModContext.ServicePublisher.Publish<T>()` |
| Depend on another mod | `[TimfDependsOn("OtherMod", MinVersion = "1.2.0")]` / `[TimfLoadAfter("OtherMod")]` |

## 6. Localization

Create `Localization\en-US.json` and `Localization\zh-Hans.json` under the mod directory with flat key/value pairs:

```json
{ "Window.Title": "My Mod", "Settings.Hint": "Hello" }
```

Read values with `context.L.Get(key, fallback)`. The fallback chain is **current language → language base → en-US → en → fallback → key name**, so keep the key sets in each language file consistent or values will silently fall back.

## 7. Build and debug

The repository maintains only these 10 mods as public examples: `BossCursor`, `ContentTestKit`, `CreativeMode`, `HighLight`, `I-Have-My-Phone-Anyway`, `LootRates`, `LowHealthWarning`, `ModSettingsHub`, `WeatherControl`, and `WorldMapIcons`. They live under `examples\`; other mods are not part of the public example set.

```powershell
.\build.ps1 Release           # Build the framework first; mods use its output
.\build-examples.ps1 Release # Build and deploy only the public examples
.\build-mods.ps1 Release     # Build and deploy mods\* → dist\Mods\<Id>\
```

`build.ps1` also builds and deploys the bundled `TIMF.UI` and `TIMF.Pinyin` library mods. `CreativeMode` uses the latter for Chinese/pinyin search; if the library is missing, the example falls back to ordinary name and ID search.

Then run `dist\TIMF.Launcher.exe`. Logs are in `dist\logs\`.

Press **F9** to open the Mod Settings hub (provided by `ModSettingsHub`) to inspect each mod's side/protocol profile and enabled state, and to open its settings page.

After entering a world, primary switches for `Authority` / `Both` mods are locked; only pure `Client` mods remain locally toggleable. When joining a server, dual-side/server mods not enabled by the server show “server disabled”. The framework does not dispatch their normal hooks or open their settings pages. This affects only the current session and does not overwrite the enabled preferences saved in the main menu.

## 8. Checklist

- [ ] Did you check `context.Client` before using it? It is null on a dedicated server.
- [ ] Before changing world state, did you check `context.Authority.IsAuthoritative`?
- [ ] Did `Unload` remove every hook registered by the mod?
- [ ] Does `IModSettings.BuildSettingsUI` avoid calling `Begin` / `End`?
- [ ] If a feature needs an in-world temporary disable, did you implement `IModFeatureToggle` instead of changing the primary enabled switch?
- [ ] Is `LoadBeforeWorld = true` used only for content/services that must be ready before entering a world?
- [ ] Does `IMapOverlayHook.OnDrawMap` avoid calling `SpriteBatch.Begin` / `End`?
- [ ] Are key IDs globally unique, using a format such as `"ModId.Action"`?
- [ ] Do sensitive file/process operations go only through `context.Security`, with a specific, user-readable purpose?
- [ ] Did you avoid direct calls to `File` / `Directory` / `Process` / PInvoke / Harmony / `MethodInfo.Invoke`?
- [ ] Do you really need `Net = Required`? It kicks vanilla players.
- [ ] Is content `InternalName` treated as a permanent save identity rather than changed with the display name?
- [ ] Are custom NPC rewards, shops, and world state decided only by the host/server?
- [ ] Did you avoid persisting runtime Item/Tile/Wall/NPC/Projectile/Buff numeric IDs, using content keys or framework sidecars instead?
- [ ] Are custom buffs registered through `TimfBuff`, with `Save = true` when they must survive saves?
- [ ] Are projectile or quest states created and rewards issued only on the authority side, rather than decided independently by multiplayer clients?
- [ ] Did you avoid currently unsupported world generation, custom NPC state machines, spawn pools, and biome music/background rewrites?
