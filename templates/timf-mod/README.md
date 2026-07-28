# MOD_DISPLAY_NAME

A Terraria mod for the TIMF injection framework, generated from the `timf-mod` template.

## Prerequisites

- **.NET SDK** (builds a `net48` assembly; Windows recommended).
- **TIMF Mod SDK** — the `ModSDK` folder shipped with a TIMF release. Point the build at it via the
  `TIMF_SDK` environment variable (recommended) or `-p:TimfSdkDir=<path>`:

  ```powershell
  setx TIMF_SDK "C:\path\to\ModSDK"   # once, then reopen the shell
  ```

- **A Terraria.exe compile reference** — a legal copy you own (never redistributed). Auto-detected
  from common Steam paths, or set it explicitly:

  ```powershell
  setx TIMF_TERRARIA "C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe"
  ```

## Build

```powershell
dotnet build -c Release
```

On success the mod is packaged into a drop-in folder:

```
dist/MyMod/
  MyMod.dll
  MyMod.default.json
  Localization/en-US.json
  Localization/zh-Hans.json
```

Copy that `MyMod` folder into your TIMF home's `Mods/` directory to install, then launch the game
through `TIMF.Launcher.exe`. (Disable the auto-package step with `-p:TimfPackageOnBuild=false`.)

## What's inside

- **ModEntry.cs** — the `IMod` entry type. Implements `IClientMod` (client features: keybind +
  per-frame `PostDraw`) and `IModSettings` (a page in the Mod Settings hub). Side is inferred as
  `Client`.
- **ModConfig.cs** — minimal config persisted through the framework's confined `IModStorage`
  (no direct `System.IO`, so it passes the security audit).
- **Localization/** — `en-US` / `zh-Hans` string catalogs, resolved via `context.L`.
- **MyMod.default.json** — the default config shipped with the mod.

## Notes on the sandbox

TIMF statically verifies your compiled assembly **before loading it** and rejects direct file,
process, network, native, dynamic-code or reflection-escape APIs. Stay within the framework
services (`context.Storage`, `context.Security`, `context.Patches`, `context.Client`, `context.L`)
and your mod will pass. Sensitive file/process access must go through `context.Security`, which
prompts the user for approval.
