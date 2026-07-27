# 侧别与协议模型

TIMF 用**两根正交的轴**描述一个 mod。理解这两根轴是写 mod 的前提，也是理解框架加载行为的钥匙。

## 1. 两根轴

| 轴 | 类型 | 回答的问题 | 由什么决定 |
|---|---|---|---|
| **能力轴** | `TimfSide` | 这段代码属于哪个 Terraria 进程角色？ | 实现的能力接口（自动推断） |
| **协议轴** | `TimfNetProfile` | 加入的对端需要装同样的代码吗？ | `[TimfMod(Net = ...)]`（默认 `Vanilla`） |

两者独立取值，组合出全部合法状态：

| `TimfSide` | `TimfNetProfile` | 典型场景 |
|---|---|---|
| `Client` | `Vanilla` | 客户端 QoL：自动治疗、准星、地图图标 |
| `Authority` | `Vanilla` | 原版安全的主机逻辑：掉落倍率、天气控制 |
| `Authority` | `Optional` / `Required` | 需双方同装的世界逻辑 |
| `Both` | `Vanilla` | 原版安全的主机逻辑 **+ 自带 UI/overlay** |
| `Both` | `Optional` / `Required` | 需握手，且有自己的客户端界面 |

## 2. 能力轴对齐原版设计

原版 Terraria **没有**侧别枚举，它只有两个独立的运行时事实：

| 判据 | 含义 |
|---|---|
| `!Main.dedServ` | 本进程有本地玩家可绘制 / 读输入 |
| `Main.netMode != 1` | 本进程拥有世界模拟权 |

这两者是**正交**的，构成一个 2 bit 空间：

| 进程 | 有本地玩家 | 有世界权威 |
|---|---|---|
| 单人（netMode 0） | ✓ | ✓ |
| 联机客户端（netMode 1） | ✓ | ✗ |
| 主机 / listen（netMode 2） | ✓ | ✓ |
| 专用服（dedServ） | ✗ | ✓ |

`TimfSide` 就是这个 2 bit 空间的直接镜像，所以它是 `[Flags]` 而不是一串具名组合——`Both` 字面上就是 `Client | Authority`：

```csharp
[Flags]
public enum TimfSide { None = 0, Client = 1, Authority = 2, Both = Client | Authority }
```

### `Authority` 不等于「服务器」

这是最容易误解的一点。原版把世界模拟代码**也编译进客户端二进制**，只用 `if (Main.netMode != 1)` 在运行时门控。`TimfSide.Authority` 沿用同样的含义：

> **`Authority` = 「这段代码是世界逻辑」，而不是「这个进程是服务器」。**

当前进程此刻能否真的写世界，是另一个问题，由运行时回答：

```csharp
if (context.Authority.IsAuthoritative)
{
    // 只有单人 / 主机 / 专用服会进这里
}
```

对 `Optional`/`Required` 协议档的 mod，握手成功后它**也会在联机客户端上激活**（用于镜像/预测），此时 `IsAuthoritative` 为 `false`。所以 `OnAuthorityActivate` 触发**不代表**你有权写世界。

## 3. 协议轴是 TIMF 独有的

原版没有任何对应概念——它纯粹是 TIMF 握手协议层的东西，因此**必须**独立于能力轴。

```csharp
public enum TimfNetProfile { Vanilla = 0, Optional = 1, Required = 2 }
```

三个值是一条严格性阶梯：

| 值 | 进握手目录 | 缺失时 | 原版客户端能加入你的房间吗 |
|---|---|---|---|
| `Vanilla` | 否 | —— | **能** |
| `Optional` | 是 | 不踢，仅不启用 | 能 |
| `Required` | 是 | **踢出**（含版本过低） | 不能 |

**默认是 `Vanilla`**：破坏原版兼容性必须显式 opt-in。加一个 `IAuthorityMod` 接口不会让你的主机突然开始踢玩家。

## 4. 加载与激活规则

全部行为都从这两根轴推导，没有针对具体枚举值的特判：

| 行为 | 规则 |
|---|---|
| 客户端半边加载时机 | `Side` 含 `Client` 且非专用服 → 启动即加载 |
| 权威半边加载时机 | `Side` 含 `Authority` → 进入会话时加载 |
| 纯权威 mod 是否延迟加载 | `Side == Authority`（无 `Client` 位）→ 是，停用时卸载 |
| 在联机客户端上镜像激活 | `Net >= Optional` 且握手成功 |
| 进握手目录 | `Net >= Optional` |
| 踢掉缺失的对端 | `Net == Required` |
| 专用服上跳过 | `Side` 不含 `Authority` |

## 5. `Side` 是断言，不是覆盖

`[TimfMod(Side = ...)]` 写了就必须与接口推断的结果**完全一致**，否则加载失败。

```csharp
// ✅ 一致 —— Side 起自文档化作用
[TimfMod(Id = "HighLight", Side = TimfSide.Client)]
public sealed class HighLightMod : IClientMod, IModSettings { }

// ❌ 加载失败 —— 没实现 IAuthorityMod 却声称有权威半边
[TimfMod(Id = "Bad", Side = TimfSide.Both)]
public sealed class BadMod : IClientMod { }
```

这样接口就是唯一真相，不存在「用 attribute 悄悄改变分类」的路径。要让 mod 具备某个能力，就去实现对应接口。

## 6. 几个容易踩的点

**`IAuthorityLifecycle` 不是能力标记。** 它只提供 `OnAuthorityActivate` / `OnAuthorityDeactivate` 回调。单独实现它**不会**让 mod 变成权威侧，也不影响侧别推断。能力只由 `IAuthorityMod` / `IClientMod` 声明。

**`IModSettings` 不计入客户端能力。** 它虽然标着 `[TimfHook(TimfSide.Client)]`，但那回答的是「这个钩子能在哪个进程被派发」，而能力推断回答的是「这个 mod 是否*需要*一个客户端半边」。`IModSettings` 只回答前者——它是机会性的客户端表面，在专用服上不被调用即可。因此一个 `IAuthorityMod + IModSettings` 的 mod 仍是纯 `Authority` 侧，保持延迟加载语义。

**判断「是否破坏原版兼容」要看 `NetProfile`，不要 switch `Side`。** 这正是两根轴分开的意义。

**进入世界后，权威集合必须冻结。** 在主菜单可以修改模组主开关；进入单人、主机、专用服或联机世界
后，`Authority` 与 `Both` 的主开关都会锁定，避免客户端、主机和存档在同一会话中使用不同的模组集合。
纯 `Client` 模组不改变世界或协议，仍可本地切换。

**加入服务器时由服务器集合决定双端/服务端模组。** 联机客户端在 HostHello 前先默认禁止所有本地
`Authority` / `Both` 执行；握手后只放行“服务器公布集合 ∩ 本地已启用且版本兼容集合”。服务器没有
启用的本地模组不会永久改写用户偏好，只在当前会话标记为不可用，同时锁住主开关和设置页。回到主菜单
后解除会话门闩。

## 7. 设计沿革

早期版本用单个四值枚举 `Client / Server / Both / Plugin`。它的问题是把两个正交概念拍平进了一根轴：`Client`/`Server`/`Both` 描述进程角色，而 `Plugin` 描述的是「不进握手、原版兼容」——后者其实是 `Server` 的一个属性，不是与之平级的第四种角色。

后果包括：`Both` 无法表达「客户端 + 原版安全权威」这个完全合理的组合（框架会硬性报错要求拆成两个 mod）；`RequiredOnJoin` 对 `Plugin` 被静默强制为 false（非法状态可表示但被偷偷改写）；以及五个互相重叠、互不嵌套的谓词函数——这是「一个枚举在编码多个布尔」的典型信号。

拆成两根轴后，非法状态在类型层面就不可表达，加载行为全部可从规则表推导，`Both` 也不再需要解释成第三档，它字面就是 `Client | Authority`。
