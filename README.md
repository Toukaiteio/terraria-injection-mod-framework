# TIMF — Terraria Injection Mod Framework

Language: **English** | [简体中文](README.zh-CN.md)

An injection-based mod framework for **vanilla Terraria 1.4.5.x** that **does not depend on tModLoader**.

Its side model follows vanilla Terraria's own design: **capability interfaces → automatic side inference → side-scoped service gates**, rather than asking authors to declare a hand-written side enum.

The framework puts **stability and security first**. Sensitive local permissions are denied by default. Reading outside the workspace, writing files, and running shells or processes must go through an explicit framework authorization request; before execution, the framework explains the target, purpose, and scope to the user or server administrator. The permission proxy is not yet a public API, so mods must not bypass the framework to call these system capabilities directly.

> For personal learning and private single-player or self-hosted server use only. The **TIMF** version is shown above the game version in the lower-left corner of the main menu (drawn by the framework itself).

---

## Documentation

| Topic | English (default) | 中文 |
|---|---|---|
| Write a mod | **[Writing a TIMF Mod](docs/writing-a-mod.md)** | [编写一个 TIMF 模组](docs/writing-a-mod.zh-CN.md) |
| API reference | **[API Reference](docs/api-reference.md)** | [API 参考](docs/api-reference.zh-CN.md) |
| Side and protocol model | **[Side and Protocol Model](docs/side-model.md)** | [侧别与协议模型](docs/side-model.zh-CN.md) |

Recommended path: **Writing a TIMF Mod** → look up details in the **API Reference** → read the **Side and Protocol Model** when loading or activation timing is unclear.

---

## Quick start

```powershell
.\build.ps1 Release           # Framework + TIMF.UI + Bootstrap → dist\
.\build-mods.ps1 Release      # mods\*   → dist\Mods\<Id>\
.\build-examples.ps1 Release  # examples\* (CI / public samples)

.\dist\TIMF.Launcher.exe      # Launch
```

`build-mods.ps1` depends on the output of `build.ps1`; **run the latter first**.

Requirements: the .NET SDK, the vendored `lib/xna` references, a `Terraria.exe` reference, and 32-bit MinGW (for compiling Bootstrap). The repository deliberately does not contain a game binary or machine-specific toolchain paths.

### Fresh clone and local paths

After cloning, provide the local-only inputs before the first build:

```powershell
# Compile-time game reference: use your own legal Terraria installation.
$env:TIMF_TERRARIA = "<path-to-your-Terraria.exe>"

# Bootstrap compiler: an i686 / 32-bit g++ executable.
$env:TIMF_MINGW_GPP = "<path-to-i686-g++.exe>"
# Alternatively set TIMF_MINGW_ROOT to the MinGW/MSYS2 root that contains it.

.\build.ps1 Release
.\build-mods.ps1 Release
```

The compile-time `Terraria.exe` must match the version of the game you launch. For the current setup, use the 1.4.5.7 executable; an older reference such as 1.4.5.6 can compile successfully but fail at runtime when hooks call changed game APIs.

For a persistent Windows setting, use `setx` and open a new terminal afterwards. `TIMF_TERRARIA_SERVER` is the optional equivalent used by `TIMF.Launcher.exe --server`. `Directory.Build.props` and `sdk/TIMF.Mod.props` also accept an explicit MSBuild property when an environment variable is not suitable; neither file contains a fixed Steam or user directory.

The launcher can receive the executable path as its first argument, use `TIMF_TERRARIA`, or read the local-only `dist\timf.json` (`gamePath` / `serverPath`). The `dist\timf.json`, `dist\config\`, and `dist\logs\` files are machine state and must not be committed or included in a shared archive. To restore a mod's first-run defaults after reusing an old `dist`, remove its persisted config; for Grand Design+, remove `dist\config\GrandDesignPlus.json` and `dist\config\mod-data\GrandDesignPlus\GrandDesignPlus.json` before launching again.

The `mods\` directory is also intentionally gitignored: it contains local/private mod sources and is not recreated by a fresh clone. Keep any local mod sources or patches separately, or use the tracked `examples\` projects as the public buildable samples. A fresh clone with no separately restored `mods\` directory will make `build-mods.ps1` report that there are no local mods to build.

Press **F9** in game to open the Mod Settings hub. Logs are written to `dist\logs\`.

### Standalone mod development (Mod SDK + `dotnet new` template)

Mods can be developed entirely **outside this repository** with the **Mod SDK** included in a release package. `build.ps1` assembles a distributable SDK in `dist\ModSDK\`, including reference assemblies, shared build props, and a `dotnet new` template:

```powershell
setx TIMF_SDK      "<extracted ModSDK path>"    # One-time; restart the terminal afterwards
setx TIMF_TERRARIA "<your own Terraria.exe>"   # A legitimate game copy; never distributed with the SDK

dotnet new install <ModSDK>\templates\timf-mod
dotnet new timf-mod -n MyMod --display "My Mod"
cd MyMod
dotnet build -c Release        # Produces a deployable dist\MyMod\ (DLL + localization + default config)
```

The generated skeleton includes configuration, localization, and an `IClientMod` implementation. `net48` / `x86`, framework and Terraria/XNA references, and post-build packaging are provided by `TIMF.Mod.props`; the mod `.csproj` only needs one `Import`. The build output is scanned by the pre-load security audit. Copy the entire `dist\MyMod\` directory to `Mods\` in the TIMF home directory to install it.

---

## Architecture

```
TIMF.Launcher  →  Starts Terraria.exe and injects TIMF.Bootstrap.dll
TIMF.Bootstrap →  Hosts CLR 4.0 in the game process and calls TIMF.Core.Loader.Initialize
TIMF.Core      →  Discovery / side inference / topological sort / service registration / sessions and handshake
TIMF.UI        →  Library mod implementing IClientMod and exposing IImmediateModeUi
```

| Path | Description |
|---|---|
| `src/TIMF.Abstractions` | Public API: capability interfaces, sides, hooks, and services |
| `src/TIMF.Core` | Loader, SideClassifier, session/handshake, and Harmony patches |
| `src/TIMF.Launcher` | Launcher and injection |
| `src/TIMF.Bootstrap` | Native x86 CLR host |
| `libs/TIMF.UI` | Immediate-mode UI library (`IClientMod`) |
| `libs/TIMF.Pinyin` | Shared Chinese pinyin search library mod (reused by examples such as `CreativeMode`) |
| `examples/*` | Public example sources and best practices |
| `mods/*` | Locally built and deployed mods (gitignored) |
| `dist/` | Build output |

---

## Core concepts at a glance

TIMF describes a mod with **two orthogonal axes**. See the [Side and Protocol Model](docs/side-model.md) for the complete explanation.

**Capability axis — `TimfSide`**: which process role the code belongs to, inferred automatically from implemented interfaces, mirroring vanilla's `!Main.dedServ` / `Main.netMode != 1`:

| Implemented interface | Inferred `TimfSide` |
|---|---|
| `IClientMod` | `Client` |
| `IAuthorityMod` | `Authority` |
| Both | `Both` (`Client \| Authority`) |

**Protocol axis — `TimfNetProfile`**: whether the remote peer must install the same code, defaulting to `Vanilla`:

| Value | Included in handshake directory | Can a vanilla client join your world? |
|---|---|---|
| `Vanilla` (default) | No | **Yes** |
| `Optional` | Yes | Yes (the mod is disabled if absent on the peer) |
| `Required` | Yes | **No** (the peer is kicked) |

```csharp
// Client-side presentation enhancement
[TimfMod(Id = "HighLight", Side = TimfSide.Client)]
public sealed class HighLightMod : IClientMod, IModSettings, IModFeatureToggle { }

// Vanilla-compatible authority logic (drops / economy / weather) — Net defaults to Vanilla
[TimfMod(Id = "LootRates")]
public sealed class LootRatesMod : IAuthorityMod, IModSettings, IAuthorityLifecycle, IModFeatureToggle { }

// If both peers must install the mod, declare Net = TimfNetProfile.Required on the authority mod.
```

> `[TimfMod(Side = ...)]` is an **assertion**, not an override: when present, it must match the side inferred from interfaces or loading fails. Interfaces are the single source of truth.

`IModFeatureToggle` is a mod's **in-world feature switch**. It changes only the mod's own configuration state; it does not load, unload, or re-patch the mod. It is useful for temporarily disabling major functionality after entering a world. The Mod Settings hub shows the switch when the mod implements the interface and the current session permits the operation; the mod's primary enabled switch remains a main-menu setting.

Mods load by world phase by default: they load when entering a world and unload when returning to the main menu. Only content mods, mods declaring `[TimfMod(LoadBeforeWorld = true)]`, and their hard dependencies are prepared after injection and before entering a world.

**Side-scoped services** — `IModContext` distributes capabilities by side; the unavailable half is either `null` or protected by a runtime gate:

| Property | Dedicated server | Multiplayer client | Single-player / host |
|---|---|---|---|
| `context.Client` | **null** | Available | Available |
| `context.Authority.IsAuthoritative` | `true` | **`false`** | `true` |
| `context.Security` | Denied without interaction | Can request | Can request |

Client hook registries such as `IPlayerUpdateHook` reject `Add` on a dedicated server and log the reason.

Sensitive file and process operations must first show an exact target and obtain user authorization, then execute through the `context.Security` proxy. The security center supports deny, once, current TIMF process, exact persistent authorization, and revocation. Mod DLLs still run inside the same fully trusted game process; the framework cannot reliably intercept `System.IO`, `Process`, or native calls that bypass the proxy. The settings hub continuously displays this isolation-boundary warning.

As defense in depth before loading, Core scans the IL and metadata of a mod's main DLL and private dependencies **without loading the assemblies**. Direct file, process, network, P/Invoke, dynamic invocation, native dependency, and direct Harmony traces cause the mod to be rejected before any constructor executes, with the findings shown in the security center. A mod that repeatedly throws at runtime is automatically disabled by the watchdog while remaining resident and no longer receiving hooks. Normal configuration uses per-mod isolated `context.Storage`; Terraria private methods and patches go through restricted reflection and patch proxies. Static auditing blocks common bypasses but is not equivalent to a process-level sandbox.

Cross-mod services are also identity-bound: an ordinary mod can publish only interfaces declared by its own assembly through `context.ServicePublisher`. It cannot replace framework, security, or UI services; direct calls to the raw service-registration entry point are rejected by the pre-load audit.

---

## Example mods

`examples/` maintains the following 10 public examples as current API best-practice references; `build-examples.ps1` builds all of them.

| Examples | Side / protocol | Description |
|---|---|---|
| BossCursor · HighLight · LowHealthWarning | `Client` | HUD, lighting, and status-display enhancements |
| WorldMapIcons | `Client` | Map overlay example (`IMapOverlayHook`) |
| I-Have-My-Phone-Anyway | `Client` | Information accessory example (`IInfoAccessoryHook`) |
| CreativeMode | `Client` | Item spawning plus Chinese/pinyin search (depends on `TIMF.Pinyin`) |
| ModSettingsHub | `Client` | F9 settings hub: primary switches, feature switches, and settings-page state |
| ContentTestKit | `Both` / `Required` | Pre-world registration and tests for custom items, tiles, walls, furniture, containers, grass/biomes, projectiles, buffs/debuffs, pet items, NPC shops/quests, and security authorization |
| **LootRates** | `Authority` / `Vanilla` | Host drop and coin multipliers; **vanilla clients can join** |
| **WeatherControl** | `Authority` / `Vanilla` | Native weather, wind, moon phase, and event control/locking through vanilla `WorldData` synchronization |

---

## License and notice

- For **learning and research** and personal use in **single-player / self-hosted servers** only. Do not use it to undermine multiplayer fairness or bypass anti-cheat systems.
- You must provide a legitimate Terraria client. This repository **does not contain or distribute** game binaries (`Terraria.exe` is gitignored).
- If you use injection technology to modify a commercial game, you are responsible for complying with its ToS and applicable local law.
