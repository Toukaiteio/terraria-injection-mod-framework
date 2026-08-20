# Side and Protocol Model

Language: **English** | [简体中文](side-model.zh-CN.md)

TIMF describes a mod with **two orthogonal axes**. Understanding both is a prerequisite for writing a mod and for understanding the framework's loading behavior.

## 1. Two axes

| Axis | Type | Question it answers | Determined by |
|---|---|---|---|
| **Capability** | `TimfSide` | Which Terraria process role owns this code? | Implemented capability interfaces (inferred automatically) |
| **Protocol** | `TimfNetProfile` | Must the remote peer install the same code? | `[TimfMod(Net = ...)]` (defaults to `Vanilla`) |

The axes are independent and combine into all valid states:

| `TimfSide` | `TimfNetProfile` | Typical use |
|---|---|---|
| `Client` | `Vanilla` | Client QoL: auto-healing, aim assistance, map icons |
| `Authority` | `Vanilla` | Vanilla-safe host logic: drop multipliers, weather control |
| `Authority` | `Optional` / `Required` | World logic that needs both sides to install the mod |
| `Both` | `Vanilla` | Vanilla-safe host logic **plus its own UI/overlay** |
| `Both` | `Optional` / `Required` | Handshake-required logic with its own client interface |

## 2. The capability axis mirrors vanilla's design

Vanilla Terraria has **no side enum**. It has two independent runtime facts:

| Predicate | Meaning |
|---|---|
| `!Main.dedServ` | This process has a local player whose screen and input can be used |
| `Main.netMode != 1` | This process owns world simulation authority |

These facts are orthogonal and form a 2-bit space:

| Process | Local player | World authority |
|---|---|---|
| Single-player (`netMode 0`) | ✓ | ✓ |
| Multiplayer client (`netMode 1`) | ✓ | ✗ |
| Host / listen server (`netMode 2`) | ✓ | ✓ |
| Dedicated server (`dedServ`) | ✗ | ✓ |

`TimfSide` directly mirrors this 2-bit space, so it is a `[Flags]` enum rather than a list of named combinations. `Both` literally means `Client | Authority`:

```csharp
[Flags]
public enum TimfSide { None = 0, Client = 1, Authority = 2, Both = Client | Authority }
```

### `Authority` does not mean “server”

This is the most common misunderstanding. Vanilla compiles world-simulation code into the **client binary** as well, then gates it at runtime with `if (Main.netMode != 1)`. `TimfSide.Authority` uses the same meaning:

> **`Authority` means “this is world logic”, not “this process is a server”.**

Whether the current process can actually write to the world is a separate runtime question:

```csharp
if (context.Authority.IsAuthoritative)
{
    // Only single-player / host / dedicated server enters here.
}
```

A mod using the `Optional` or `Required` protocol profile also activates on a multiplayer client after a successful handshake (for mirroring or prediction). `IsAuthoritative` is then `false`, so `OnAuthorityActivate` does **not** mean that the mod may write world state.

## 3. The protocol axis is TIMF-specific

Vanilla has no equivalent concept. This is purely part of TIMF's handshake protocol and must therefore remain independent of the capability axis.

```csharp
public enum TimfNetProfile { Vanilla = 0, Optional = 1, Required = 2 }
```

The three values form a strictness ladder:

| Value | In handshake directory | If absent | Can a vanilla client join your world? |
|---|---|---|---|
| `Vanilla` | No | — | **Yes** |
| `Optional` | Yes | Do not kick; simply do not activate | Yes |
| `Required` | Yes | **Kick** (including versions that are too old) | No |

**The default is `Vanilla`**: breaking vanilla compatibility requires explicit opt-in. Adding `IAuthorityMod` does not suddenly make your host kick players.

## 4. Loading and activation rules

All behavior follows from the two axes; there are no special cases for individual enum values:

| Behavior | Rule |
|---|---|
| Pre-world loading | `[TimfMod(LoadBeforeWorld = true)]`, content mods, and hard dependencies of pre-world mods → load after injection and before the main menu |
| Default loading time | Mods not promoted to pre-world loading → load when entering a world and unload when returning to the main menu |
| Authority activation | `Side` includes `Authority` → activate the authority half according to single-player/host/dedicated-server state or the handshake result |
| Delay for pure authority mods | `Side == Authority` (no `Client` bit) → even after discovery, wait for authority activation to load; unload when deactivated |
| Mirror activation on multiplayer clients | `Net >= Optional` and handshake succeeds |
| Included in handshake directory | `Net >= Optional` |
| Kick a missing peer | `Net == Required` |
| Skip on dedicated servers | `Side` does not include `Authority` |

## 5. `Side` is an assertion, not an override

When `[TimfMod(Side = ...)]` is specified, it must **exactly match** the side inferred from the implemented interfaces or loading fails.

```csharp
// ✅ Consistent — Side documents the intended contract
[TimfMod(Id = "HighLight", Side = TimfSide.Client)]
public sealed class HighLightMod : IClientMod, IModSettings, IModFeatureToggle { }

// ❌ Loading fails — IAuthorityMod is not implemented, but the mod claims an authority half
[TimfMod(Id = "Bad", Side = TimfSide.Both)]
public sealed class BadMod : IClientMod { }
```

Interfaces remain the single source of truth; an attribute cannot silently change classification. To give a mod a capability, implement the corresponding interface.

## 6. Common pitfalls

**`IAuthorityLifecycle` is not a capability marker.** It only provides `OnAuthorityActivate` / `OnAuthorityDeactivate` callbacks. Implementing it alone does not make a mod an authority-side mod and does not affect side inference. Capabilities are declared by `IAuthorityMod` / `IClientMod`.

**`IModSettings` does not count as client capability.** Although it carries `[TimfHook(TimfSide.Client)]`, that attribute answers “in which process can this hook be dispatched?”, while capability inference answers “does this mod *require* a client half?”. `IModSettings` is an opportunistic client surface and is simply not called on a dedicated server. Therefore an `IAuthorityMod + IModSettings` mod is still pure `Authority` side and keeps delayed-loading semantics.

**`IModFeatureToggle` is not the primary mod switch.** The primary switch is managed by `IModRegistry.TrySetEnabled` and locks `Authority` / `Both` mods after entering a world. A feature switch is a mod-owned configuration state that can be changed safely in-world; it does not call `Load` / `Unload`. `IModInfo.FeatureToggle` is non-null only when the mod is loaded and the current session permits the operation.

**Use pre-world loading sparingly.** It is intended for content registration, shared service publication, and infrastructure that must be available from the main menu. Ordinary UI, map overlays, and gameplay logic should stay in the default world phase so menu transitions do not carry unnecessary initialization cost.

**To decide whether vanilla compatibility is broken, inspect `NetProfile`, not `Side`.** This is exactly why the two axes are separate.

**The authority set is frozen after entering a world.** The main menu can change primary mod switches; after entering a single-player, host, dedicated-server, or multiplayer world, primary switches for `Authority` and `Both` are locked. This prevents clients, hosts, and saves from using different mod sets in one session. Pure `Client` mods can still be toggled locally.

**The server's set decides which dual-side/server mods are active when joining.** Before `HostHello`, a multiplayer client disables all local `Authority` / `Both` execution by default. After the handshake, it enables only the intersection of “server-advertised set” and “locally enabled, version-compatible set”. Local mods not enabled by the server do not permanently change user preferences; they are marked unavailable for the current session and their primary switches and settings pages are locked. The session gate is removed on returning to the main menu.

## 7. Design history

Early versions used one four-value enum: `Client / Server / Both / Plugin`. This flattened two orthogonal concepts into one axis: `Client` / `Server` / `Both` described process roles, while `Plugin` described “does not enter the handshake and remains vanilla-compatible”. The latter is actually a property of `Server`, not a fourth peer role.

The consequences included: `Both` could not express the perfectly reasonable combination “client + vanilla-safe authority” (the framework instead forced the code to be split into two mods); `RequiredOnJoin` was silently forced to `false` for `Plugin` (an invalid state could be represented and then quietly rewritten); and five overlapping, non-nested predicate functions appeared — the classic sign that one enum was encoding multiple booleans.

With two axes, invalid states are unrepresentable at the type level, loading behavior can be derived from the rule tables, and `Both` no longer needs to be explained as a third tier: it literally means `Client | Authority`.
