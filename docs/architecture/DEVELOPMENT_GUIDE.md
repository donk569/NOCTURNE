# 《夜隙 NOCTURNE》开发规范（DEVELOPMENT_GUIDE v1.0）

> 这份文件是**写代码时的唯一裁判**。和它冲突的习惯，一律以它为准。
> 配套：`docs/architecture/ARCHITECTURE.md`（为什么这么设计）、`docs/production/BACKLOG.md`（今天做什么）。
> 适用范围：Unity 2022.3.62f3c1 / URP 14.0.12 / C# 9（**不是** C# 10+）。

---

## 0. 三条最高优先级（其余规则都是它们的推论）

1. **不让编译器替你守的规则，就不算规则。** 能做成 asmdef 引用 / 类型系统 / 枚举的，绝不做成"记得就好"。
2. **每个状态只有一个写入者。** 出现第二个写入者时，先设计控制权转移，再写代码。
3. **每次提交后自测必须全绿。** 红着的提交不许留在主分支上。

---

## 1. 命名

### 1.1 文件与类

| 对象 | 规则 | 例子 |
|---|---|---|
| 一个 `.cs` 文件只放一个 `public` 类型 | 文件名 = 类型名 | `CatMotor.cs` → `public sealed class CatMotor` |
| `MonoBehaviour` 文件名**必须**与类名完全一致 | Unity 序列化要求 | `GameStateManager.cs` |
| 接口 | `I` + 名词，文件同名 | `IInteractable.cs`、`IFoodContainer.cs` |
| 抽象基类 | `...Base` | `EntityBehaviourBase` |
| 纯 C# 逻辑类 | 名词，不加 `Manager`/`Helper`/`Util` | `CatBrain`、`NavGrid`、`EntityStateMachine` |
| 枚举 | 单数名词 | `GameState`、`CatMood`、`SfxId` |
| 事件载荷 struct | 名词 + `Signal`/`Change`/`Args` | `HorrorSignal`、`GameStateChange` |
| ScriptableObject 类 | 名词 + `Profile`/`Config`/`Catalog` | `CatProfile`、`HorrorConfig`、`AudioCatalog` |
| 静态工具类 | 名词 | `CatPoseUtility`（已有，保留）、`Paths` |

**禁止**的词：`Manager`（除非它真的管理一个有生命周期的对象集合，如 `SaveService`/`AudioService` 用 `Service` 更好）、`Helper`、`Util`、`Utils`、`Common`、`Misc`、`Data`（单独用没信息量）、`Base2`、`New`、`Temp`。

### 1.2 字段

| 种类 | 规则 | 例子 |
|---|---|---|
| `[SerializeField]` 私有字段 | `_camelCase` | `[SerializeField] float _hunger;` |
| public 字段（**仅** Inspector 用的引用/配置，历史遗留可留） | `camelCase` | `public Transform player;` |
| 新增代码的 Inspector 字段 | **优先** `[SerializeField] private` + 只读属性 | 见 §1.3 |
| 私有运行时状态 | `_camelCase` | `float _senseTimer;` |
| 常量 | `UPPER_SNAKE`（项目既有风格）或 `PascalCase`，**一个文件内统一** | `CatClips.IDLE`、`const float MinRadius` |
| 静态只读 | `PascalCase` | `static readonly int BaseMapId` |
| 布尔 | 肯定式，`is/has/can/should` 前缀 | `_isPouring`、`CanInteract`；**禁止** `notReady`、`disableX` |

> 为什么用 `_` 前缀：`CatAgent` 已经在用（`_path`/`_doorways`），全项目统一，不要混风格。

### 1.3 新增 Inspector 字段的标准写法

```csharp
[Header("感知")]
[Tooltip("感知刷新间隔（秒）。8Hz 足够，别每帧射线")]
[SerializeField, Range(0.02f, 0.5f)] float _senseInterval = 0.12f;
public float SenseInterval => _senseInterval;
```

- 每个 `[SerializeField]` **必须**有 `[Tooltip]`。没有 Tooltip 的字段，三个月后的你不知道它的单位。
- 有单位的必须写单位（`（秒）`、`（米）`、`（m/s）`）。
- 需要范围限制的用 `[Range]`，不要靠代码 `Mathf.Clamp` 兜。

### 1.4 方法

| 种类 | 规则 |
|---|---|
| public 方法 | 动词开头，`PascalCase`：`SetState`、`TryStartPath`、`CaptureSave` |
| 返回 bool 的尝试型 | `Try` + 动词：`TrySnap`、`TryGetWalkablePointNear` |
| 私有方法 | 同上，`PascalCase`（项目既有风格，不用 camelCase） |
| 每帧回调 | 只有 `Update`/`LateUpdate`/`FixedUpdate` 三个名字，**不许**自定义 `MyUpdate` 再被别人调 |
| 协程 | `Co` + 动词，或直接动词（项目既有：`CoSequence`、`SleepRoutine`）——**一个文件内统一** |

### 1.5 事件

| 规则 | 例子 |
|---|---|
| 过去式，表示"已经发生" | `StateChanged`、`GetUpFinished`、`PourCommitted`、`MoodChanged` |
| 不用 `On` 前缀（`On` 只给 Unity 回调） | ❌ `OnStateChanged` → ✅ `StateChanged` |
| 载荷用强类型，不用 `object`/`string` | ✅ `event Action<CatMood, CatMood> MoodChanged` |
| 需要 3 个以上参数时，定义 struct | `event Action<GameStateChange>` |

### 1.6 枚举：只许在末尾追加（铁律）

```csharp
public enum GameState
{
    Opening = 0, Lying = 1, StandingUp = 2, Playing = 3,   // 已序列化，数值锁定
    Sleeping = 4, Cutscene = 5, Paused = 6, Ended = 7, Loading = 8  // 只能往后加
}
```

理由：枚举值被序列化进场景（`GameStateManager._state`）与 Prefab（`CatSpot.mood` 等）。重排 = 老场景里"睡觉"变成"警觉"这类静默错误。

**同样适用于**：`CatMood`、`FeedingPhase`、`CatFoodBowlState`、`CatFoodBagState`、`SfxId`（新增音效只能追加）。

**也不许删**：删掉中间一个值会让后面所有值语义偏移。真要废弃，加 `[Obsolete]` 并保留空位。

### 1.7 不许重命名已挂载的 `MonoBehaviour` 类

挂载了组件的类（`GameStateManager`、`CatAgent`、`CatFeedingSequenceController`、`FirstPersonController`…）**永远不改类名**。改名会让场景里 `m_Script` 的 `fileID` 指向失效，24 个挂载点可能全变 Missing。

要"改名"的正确做法：**新建一个类做门面**，旧类保留并转发。

```csharp
// 想叫 CatMotor，但旧代码到处是 catAgent.MoveTo(...)
// → 新类 CatMotor，CatAgent 里留：
public void MoveTo(Vector3 p) => _motor.MoveTo(p);   // 转发，调用点零改动
```

---

## 2. 目录与文件放置

### 2.1 想加东西 → 放哪

| 你要加的东西 | 目录 | 程序集 |
|---|---|---|
| 新状态/新时钟/新存档字段 | `Assets/Game/Core/<域>/` | `NOCTURNE.Core` |
| 新接口（被别人实现的契约） | `Assets/Game/Interaction/` 或 `Core/Contracts/` | `NOCTURNE.Interaction` / `Core` |
| 新寻路算法/网格 | `Assets/Game/Nav/` | `NOCTURNE.Nav` |
| 新的实体零件（感知/位移/动画驱动/行为基类） | `Assets/Game/Entities/` | `NOCTURNE.Entities` |
| 玩家相关（移动/坐/躺/数值/交互执行） | `Assets/Game/Player/` | `NOCTURNE.Player` |
| 猫的任何东西 | `Assets/Game/Cat/` | `NOCTURNE.Cat` |
| 鬼的任何东西 | `Assets/Game/Ghosts/`（行为进 `Ghosts/Behaviour/`） | `NOCTURNE.Ghosts` |
| 家具/道具的交互脚本 | `Assets/Game/World/Props/` | `NOCTURNE.World` |
| 开场/喂食/恐怖导演等"流程编排" | `Assets/Game/World/<Opening\|Feeding\|Horror>/` | `NOCTURNE.World` |
| 界面 | `Assets/Game/UI/` | `NOCTURNE.UI` |
| 编辑器工具、自测 | `Assets/Game/Editor/` | `NOCTURNE.Editor` |
| 测试 | `Assets/Game/Tests/EditMode|PlayMode/` | `NOCTURNE.Tests.*` |
| **美术资源** | `Assets/Art/`（**禁止放 `.cs`**） | — |

### 2.2 文件长度红线

| 行数 | 处置 |
|---|---|
| ≤ 300 | 正常 |
| 301–500 | 可以接受，但提交信息里说明为什么 |
| **> 500** | **必须拆**。拆分方式：按"谁写什么"拆（感知/位移/动画/需求），不按"代码前半段/后半段"拆 |
| > 800 | 视为债务，登记到 BACKLOG |

现状参考：`CatAgent` 1627 = 待拆（`C04`–`C06`）；`CatOpeningController` 635 = 待评估；`CatNavGrid` 572 = 算法类，可接受。

### 2.3 移动/删除 `.cs` 的铁律

- **移动**：用 Unity Project 窗口拖拽，或 `git mv 旧路径/X.cs 新路径/X.cs` **同时** `git mv 旧路径/X.cs.meta`。GUID 不变 → 场景引用不断。
- **绝不允许**："新建文件 → 粘贴代码 → 删除旧文件"。这会产生新 GUID，场景里所有挂载点变 Missing。
- **删除前必须三查**（审计报告曾因此出错）：
  ```bash
  grep -rn "ClassName" Assets --include=*.cs        # 1. 普通引用
  grep -rn "new ClassName" Assets --include=*.cs    # 2. 构造函数
  grep -rn "List<ClassName>\|ClassName>" Assets --include=*.cs  # 3. 泛型参数
  ```
  三种形态都为空，才算死代码。**同时**要在场景/Prefab 里搜一次（`.unity`/`.prefab` 文本里有 `m_Script` GUID）。
  > 实例教训：`CatDoorway` 是纯 C# 类（不是 `MonoBehaviour`），在场景里搜不到，但被 `CatAgent` 与 `CatNavGrid` 的泛型参数用了 10 次。它不是死代码。

---

## 3. 分层依赖铁律

### 3.1 合法依赖矩阵

| 引用方 ↓ / 被引用 → | Core | Nav | Interaction | Entities | Player | Cat | Ghosts | World | UI |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Core | — | | | | | | | | |
| Nav | | — | | | | | | | |
| Interaction | ✓ | | — | | | | | | |
| Entities | ✓ | ✓ | | — | | | | | |
| Player | ✓ | | ✓ | | — | | | | |
| Cat | ✓ | ✓ | | ✓ | | — | | | |
| Ghosts | ✓ | ✓ | ✓ | ✓ | | | — | | |
| World | ✓ | | ✓ | ✓ | ✓ | ✓ | ✓ | — | |
| UI | ✓ | | ✓ | | | | | | — |

### 3.2 六条禁令（编译器会执行）

1. `Core` 不能引用任何 `NOCTURNE.*`。
2. `Nav` 不能引用任何 `NOCTURNE.*`（连 Core 都不行）。它要能"扔进任何项目还能编译"。
3. **`Cat` 与 `Ghosts` 互不引用**。鬼想读猫的状态？订阅事件，不许 `FindObjectOfType<CatAgent>()`。
4. `UI` 不能引用 `Player`/`Cat`/`Ghosts`/`World`。只能引用 `Core`（事件 + 接口）与 `Interaction`（只读文案）。
5. 运行时程序集不能引用 `Editor`/`Tests`。
6. `Entities` 不能引用 `Cat`/`Ghosts`/`Player`。框架不知道谁在用它。

### 3.3 想跨层被拦住时的三种正当解法

按优先级，**只有这三种**：

**① 契约下沉**（首选）：把接口/事件类型放到更底层。

```csharp
// ❌ UI 想读玩家疲劳 → UI 引用 Player（违规）
// ✅ 接口在 Core，实现在 Player
// Core/Contracts/IStatsSource.cs
public interface IStatsSource { float Fatigue01 { get; } float Sanity01 { get; } }
// Player/PlayerStats.cs
public sealed class PlayerStats : MonoBehaviour, IStatsSource { ... }
// Core/Contracts/ServiceHub.cs  （唯一允许的静态注册点）
public static class ServiceHub { public static IStatsSource Stats; public static IClock Clock; }
// UI/HudUI.cs
var f = ServiceHub.Stats.Fatigue01;   // ✅ 只看接口
```

**② `World` 做中介**：`World` 是唯一允许认识所有人的层。猫要触发一个鬼事件？`World` 里写 10 行编排，而不是让猫去引用鬼。

**③ SO 通道**：发送方与接收方都不认识对方，靠 `GameEvent<T>` 资产连接。

**没有第四种。**"给 Cat 加一个 Ghosts 引用"不是解法。

### 3.4 `NOCTURNE.Core` 允许的静态单例（上限 5 个）

| 允许 | 理由 |
|---|---|
| `GameStateManager.Instance` | 已有，全项目唯一总闸 |
| `ServiceHub`（静态字段容器） | 装 `IStatsSource`/`IClock`/`IAudioService` 等接口，非具体类型 |
| `AudioService.Instance` | 跨场景常驻 |

**游戏层（Player/Cat/Ghosts/World）禁止新增 `static Instance`。** 需要引用就序列化字段连线，或用 `EntityRegistry`/`ServiceHub` 查。

> 现状反例：`CatFoodBagInteractable.Held`（`static` 可变全局）。它会让"两个袋子"或多槽存档出现幽灵状态。已在 `A16` 处理。

---

## 4. 新增一个"鬼"的标准 checklist

### 4.1 决策：先问三个问题

1. 它**动**吗？（不动 → `StaticMotor`；沿网格走 → `NavGridMotor`；瞬移 → `TeleportMotor`）
2. 它**怎么出现**？（距离触发 / 看某物触发 / 时间触发 / 条件组合）
3. 它**做什么**？（让自己被看到 / 靠近 / 干扰物品 / 改变环境）

三个答案各自对应一个组件的选择，**不要写一个 `XxxGhost` 类把所有事都干了**。

### 4.2 "第三次法则"（什么时候才该抽象）

写第 1 个鬼：直接写具体行为组件，别抽接口。
写第 2 个鬼：如果和第 1 个有 60% 以上重复，把重复部分**下移到 `Entities`**。
写第 3 个鬼：如果还在抄，说明抽象错了——停下来重设计，而不是继续抄。

**禁止**："我预感以后会有 10 个鬼，所以先设计一套行为树/GOAP/HTN"。本项目的鬼行为差异大但**数量少**，打分选择（`UtilitySelector`）足够。

### 4.3 标准步骤（12 步）

```
□ 1. 写设计三行：它动吗 / 怎么出现 / 做什么
□ 2. 选组件：Motor + 出现条件 Behaviour + 主行为 Behaviour + EntityStateMachine 状态集
□ 3. 建 Profile：Assets/Art/Ghosts/<Name>/SO_Ghost<Name>.asset，填 entityKind
□ 4. 写 Behaviour：继承 EntityBehaviourBase，实现
       OnEnter(in EntityContext) / OnExit() / Tick(in EntityContext, float dt) / Weight(in EntityContext)
□ 5. 决策逻辑抽纯 C#：如果 Weight/Tick 里有 if-else 分支超过 3 个，把判定搬进一个
       <Name>Logic 纯 C# 类，并在 Tests/EditMode 断言它（照抄 CatBrainTests 的模式）
□ 6. 建 Prefab：Assets/Art/Ghosts/<Name>/GHOST_<Name>.prefab
       根节点挂 EntityRig + 行为组件 + Collider（如需）
□ 7. 骨骼二次写入（如需注视/抖动）：独立的 LateUpdate 组件，只管自己那几根骨头，
       禁止和 Animator 抢同一个 transform 的 rotation
□ 8. 音效：在 AudioCatalog 里追加 SfxId（枚举末尾追加）+ 填 clip
□ 9. 调试：加 #if UNITY_EDITOR 的 OnDrawGizmosSelected（画感知范围/当前目标/权重）
□ 10. 接入 HorrorConfig：在鬼配置表里加一条（enabled / 出现条件 / 权重）
□ 11. 场景验证：单独开关这只鬼，跑 3 分钟，看 Profiler 帧时间与 Animator 数
□ 12. 提交：feat(ghosts): 第 N 个鬼——<名字>
```

### 4.4 硬性限制

| 限制 | 数值 | 为什么 |
|---|---|---|
| 同屏活跃鬼 | ≤ 3 | 见 ARCHITECTURE §11.2 |
| 每只鬼的 `Update` | ≤ 1（用 `EntityRig` 统一分发） | `Update` 数量预算 ≤ 40 |
| 每只鬼的 `Animator` | 1（`cullingMode = CullUpdateTransforms`） | Animator 预算 ≤ 6 |
| 每只鬼的路径请求 | ≤ 1 次 / 0.4s，且走 `NavService` 队列 | A* 预算 ≤ 2/帧 |
| 鬼引用猫/玩家的方式 | 只能通过 `EntityContext` 的快照，不许持有 `Transform` 引用超过 1 帧 | 防止生命周期漏洞 |

---

## 5. 新增一个可交互物的标准步骤

```
□ 1. 实现 IInteractable（必须）：
       string Prompt { get; }
       bool CanInteract { get; }
       void Interact();
□ 2. 需要自定义整句提示（不是 "[E] xxx"）时，额外实现 IInteractPrompt：
       string FullPrompt { get; }
□ 3. 对象上必须有 Collider（层要与 FirstPersonController.interactMask 匹配）
□ 4. 长按类交互不要塞进 Interact()：用 ControlLock.Acquire(Reason.Xxx, this) 表达
       "我现在占用控制权"，松开时 Release
□ 5. 有状态变化就发事件（event Action<旧,新>），不要让别人轮询你
□ 6. 要让鬼也能"动"它：额外实现 IInterferable
□ 7. 要进存档：额外实现 ISaveable（DTO 里只放逻辑状态，不放 Transform）
□ 8. 音效走 AudioService.PlayAt(transform, SfxId)
□ 9. Inspector 里 4 个字段必须有 Tooltip：交互距离 / 提示文案 / 是否一次性 / 音效
□ 10. 在场景里按 E 验证：远距离无提示、近距离有提示、交互后状态正确、再交互行为正确
□ 11. 提交：feat(world): <物体> 可交互
```

**反例提醒**：`CatFoodBowl` 的 `Interact()` 是空实现（它靠长按左键倒粮）。这是**允许**的——空实现 + `IInteractPrompt` 表达"我不吃 E 键"。但必须在类注释里写清楚为什么。

---

## 6. 新增一个 UI 的标准步骤

```
□ 1. 决定层级：Canvas 的 sort order
       开场/黑屏 = 100；模态（暂停/结局）= 90；HUD = 10；提示 = 20
□ 2. 新建 Canvas Prefab 放 Assets/Art/UI/（资产）→ 脚本放 Assets/Game/UI/（代码）
□ 3. 脚本只做"订阅 + 显示"，不许持有游戏状态：
       ❌ float _fatigue;  _fatigue += dt;      // UI 不算数值
       ✅ ServiceHub.Stats.Fatigue01            // UI 只读接口
□ 4. 订阅成对：OnEnable += / OnDisable -=（见 §8）
□ 5. 刷新节流：数值类 2Hz（累加器），提示类事件驱动，禁止每帧 SetText
□ 6. 文本用 TMP，字体资产复用现有（Assets/fonts），不要每处新建
□ 7. 数值格式统一：百分比保留 0 位小数（"疲劳 72%"），时间用 HH:mm
□ 8. 缩放：以 1920×1080 为基准，Canvas Scaler 用 Scale With Screen Size / Match 0.5
□ 9. 关闭/打开时不要 Destroy/Instantiate，用 SetActive 或 CanvasGroup.alpha
□ 10. 1080p 与 720p 各截一张图，确认不遮挡准星、不出屏
□ 11. 提交：feat(ui): <界面名>
```

**禁用 `OnGUI`（新代码）**：现有 3 处 `FirstPersonController.OnGUI`、`CatFoodBowl.OnGUI`、`CatFoodBagInteractable.OnGUI` 是历史遗留，已排 `E04` 迁移。新 UI 一律 uGUI。

---

## 7. ScriptableObject 配置规范

```csharp
[CreateAssetMenu(menuName = "NOCTURNE/Entities/Cat Profile", fileName = "SO_CatProfile")]
public sealed class CatProfile : EntityProfile
{
    [Header("移动")]
    [Tooltip("闲逛速度（m/s），要和 Walk 剪辑的爪速匹配，否则脚底打滑")]
    public float walkSpeed = 0.34f;
    ...
}
```

**四条铁律**：

1. **SO 只存配置，不存运行时状态。**
   ❌ `public float currentHunger;` —— 这是运行时状态，必须放组件。
   ✅ `public float hungerPerSecond = 0.009f;`
   > 理由：SO 是共享资产。编辑器里跑一次 Play 就把默认值改了，是这类 bug 的经典来源。

2. **资产命名**：`SO_<类型>_<用途>.asset`，放 `Assets/Art/<域>/SO/`。代码里**不许** `Resources.Load`，一律序列化引用或 `AudioCatalog` 这类"总表 SO"。

3. **运行时不许 `ScriptableObject.CreateInstance` 造配置**。要么用资产，要么用普通类。唯一例外：编辑器工具生成资产。

4. **改 SO 的值必须能在 Play 中生效或明确标注"重启生效"**。如果需要 `Awake` 缓存，在 `OnValidate` 里清缓存：
   ```csharp
   void OnValidate() { _cachedRadius = -1f; }   // 强制下次重算
   ```

**什么时候用 SO、什么时候用普通 `[SerializeField]`**：

| 用 SO | 用普通字段 |
|---|---|
| 多个对象共享同一套配置（猫的 Profile、鬼的配置） | 单个对象独有的参数（某扇门的开门角度） |
| 需要在多个 Prefab 间引用同一份（AudioCatalog） | 只在这一个 Prefab 里用 |
| 需要策划/自己"填表"（`HorrorConfig`） | 临时调试参数 |

---

## 8. 事件订阅必须成对

### 8.1 标准模板（复制这段，不要手写）

```csharp
void OnEnable()
{
    if (GameStateManager.Instance != null)
        GameStateManager.Instance.StateChanged += OnStateChanged;

    if (_bowl != null)
        _bowl.StateChanged += OnBowlStateChanged;
}

void OnDisable()
{
    if (GameStateManager.Instance != null)
        GameStateManager.Instance.StateChanged -= OnStateChanged;

    if (_bowl != null)
        _bowl.StateChanged -= OnBowlStateChanged;
}
```

### 8.2 强制自检（提交前）

```bash
# 每个 "+=" 必须有一个对应的 "-="，数量必须相等
grep -c "\.\w* +=" Assets/Game/**/*.cs
grep -c "\.\w* -=" Assets/Game/**/*.cs
```

对不上就是泄漏。**订阅在 `Start`、退订在 `OnDisable` 是错的**（`Start` 可能在 `OnEnable` 之后，且组件被禁用后 `Start` 不会再跑）。

### 8.3 为什么必须是 `OnEnable`/`OnDisable`

- `OnEnable`/`OnDisable` 是**成对**的（禁用时 `OnDisable` 会跑）。
- `Awake`/`OnDestroy` 在"对象被 `SetActive(false)` 但没销毁"时不配对 → 泄漏。
- 静态事件（`GameStateManager.Instance.StateChanged`）必须判断 `Instance != null`：`OnDisable` 可能发生在单例被销毁之后。

### 8.4 事件泄漏的症状（遇到就知道是这个问题）

- 第二次进游戏，提示显示两份 / 音效播两次。
- 切场景后 `MissingReferenceException`。
- `OnDisable` 里没退订 → 回主菜单再开始，一次操作触发两次逻辑。

---

## 9. `Update` 使用规范

### 9.1 频率分层（能用事件就不用轮询）

| 频率 | 用途 | 写法 |
|---|---|---|
| 每帧 | 玩家移动、相机、`EntityMotor` 位移 | `Update` / `LateUpdate` |
| 8 Hz（0.12s） | 感知（射线/距离/视线） | 累加器，见下 |
| 2 Hz（0.5s） | 需求（饥饿/疲劳）、鬼的决策、HUD 刷新 | 累加器 |
| 事件驱动 | 门开、提示、音效、状态切换、UI 文案 | `event` |
| 一次 | 初始化、烘焙、装配 | `Awake`/`Start` |

### 9.2 累加器模板（不要用 `InvokeRepeating`）

```csharp
const float SensePeriod = 0.12f;
float _senseTimer;

void Update()
{
    _senseTimer -= Time.deltaTime;
    if (_senseTimer <= 0f)
    {
        _senseTimer = SensePeriod;   // 用常数重置，不要 +=，避免漂移累积
        UpdateSenses();
    }
    TickMovement(Time.deltaTime);    // 移动必须每帧
}
```

> 为什么不用 `InvokeRepeating`：它按字符串方法名反射、无法静态检查、难以传参、暂停时行为不直观。

### 9.3 禁止事项

| 禁止 | 替代 |
|---|---|
| `Update` 里的空 `if` 大段（禁用时也不 return 早期） | 首行 `if (!GameStateManager.Instance.IsSimulationRunning) return;` |
| 每个组件自己 `Update` 查同一个全局状态 | 由一个 `Manager` 分发（`GameClock.Tick8Hz`），避免 N 次虚调用 |
| `Update` 里 `GetComponent`/`Find*`/`Camera.main` | `Awake` 缓存 |
| `Update` 里 `new` 任何东西 / 字符串拼接 / LINQ | 预分配 + 复用（现有 `CatNavGrid` 已示范） |
| 在 `Update` 里写 `transform.position` 且同时有 `Rigidbody` | 本项目 0 个 `Rigidbody`，保持这个状态 |
| 用 `Update` 做"延迟 N 秒后执行" | 协程 `WaitForSeconds` |

### 9.4 `LateUpdate` 只给屏幕/骨骼相关

- 相机跟随、头部注视（`CatLookAt`）、尾巴（`CatBodyMotion`）用 `LateUpdate`，保证在 `Animator` 求值之后。
- **同一个 transform 的 rotation 不许被两个系统写**。`CatLookAt` 写 `head/neck`，`CatBodyMotion` 写 `tail.*`，互不重叠——这是正确示范，新实体照抄。

---

## 10. 对象池规范

### 10.1 什么时候池化（阈值：> 2 次/秒的创建销毁）

| 池化 | 不池化 |
|---|---|
| 猫粮颗粒、碎片、纸张、鬼的闪现 Prefab | 家具、门、猫、玩家、UI |
| 一次性 `AudioSource`（由 `AudioService` 内部池化） | 常驻音源 |
| 伤害/提示飘字 | 场景固定物件 |

### 10.2 规范

```csharp
public sealed class VfxPool : MonoBehaviour
{
    [SerializeField] GameObject _prefab;
    [SerializeField] int _prewarm = 8;

    readonly Stack<GameObject> _idle = new Stack<GameObject>();

    public GameObject Rent()
    {
        var go = _idle.Count > 0 ? _idle.Pop() : Instantiate(_prefab, transform);
        go.SetActive(true);
        return go;
    }

    public void Return(GameObject go)
    {
        go.SetActive(false);
        go.transform.SetParent(transform, false);   // 归位父节点，防止漂在场景里
        _idle.Push(go);
    }
}
```

必须做到：
1. **`Return` 时重置状态**：`Rigidbody.velocity = 0`、粒子 `Clear()`、`Animator.Rebind()`、协程停止。**不重置是池化 bug 的头号来源。**
2. **预留预热**（`_prewarm`），避免运行时第一次爆发卡顿。
3. **池的父节点固定**，`Return` 时 `SetParent` 回去。
4. 池化的对象**不许**持有对场景对象的长期引用（会被下一个使用者看到幽灵状态）。

---

## 11. 协程 vs UniTask

### 11.1 结论：**用协程，不装 UniTask，不用 async/await**

| 方案 | 裁决 | 理由 |
|---|---|---|
| **协程（`IEnumerator` + `StartCoroutine`）** | ✅ 采用 | 项目已在用（`CatFeedingSequenceController.CoSequence`、`PlayerLyingController`、`CatBedController.SleepRoutine`）；跟 `Time.timeScale` 天然配合（`WaitForSeconds` 受暂停影响）；错误能在 Console 看到 |
| `UniTask` | ❌ 不引入 | 新增第三方依赖 + IL2CPP 风险 + 单人对 `PlayerLoop` 调试不熟。收益（省 GC、可取消）在本项目用不上 |
| `async/await` + `Task` | ❌ 禁止 | 2022.3 **没有 `Awaitable`**（Unity 6 才有）；`Task` 不在主线程、拿不到 `transform`；异常会被吞 |
| `Awaitable` | ❌ 不可用 | 2022.3 无此类型 |

### 11.2 协程规范

```csharp
// ✅ 标准结构：可取消 + 有超时 + 失败有日志
IEnumerator CoApproach()
{
    float t = 0f;
    const float Timeout = 8f;
    while (Vector3.Distance(transform.position, _target) > _arriveDistance)
    {
        t += Time.deltaTime;
        if (t > Timeout)
        {
            Log.Warn(CatLogTag, $"接近超时，放弃目标 {_target}");
            yield break;                     // 超时用 yield break，不要死循环
        }
        yield return null;
    }
}
```

规则：
1. **嵌套不超过 2 层**（`yield return StartCoroutine(...)`）。3 层以上改成状态机（`FeedingPhase` 就是正确示范）。
2. **谁 `StartCoroutine` 谁负责停**：组件 `OnDisable` 时 `StopAllCoroutines()`（Unity 会自动停，但要意识到"停了之后状态是半截的"，所以关键流程要有"重入保护"）。
3. **不许在协程里 `while(true)` 没有退出条件**。
4. **`WaitForSeconds` 的分配**：高频循环里缓存 `static readonly WaitForSeconds Wait01 = new WaitForSeconds(0.1f)`。低频（每流程一次）无所谓。
5. **协程不许跨场景存活**：需要跨场景的用 `DontDestroyOnLoad` 的 `MonoBehaviour` + 明确的生命周期。

### 11.3 流程编排的正确形态

超过 3 个阶段的流程（开场、喂食）**不要**写成一个大协程：

```csharp
// ✅ 阶段机 + 每阶段一个协程（CatFeedingSequenceController 已是这个形态，照抄）
enum Phase { Waiting, Running, Eating, Returning }
Phase _phase;
IEnumerator CoSequence()
{
    yield return CoWaitForFood();
    SetPhase(Phase.Running);
    yield return CoRunToBowl();
    ...
}
```

好处：可以在任何阶段保存进度（存档），可以在 Inspector 看到当前阶段（`_phase` 序列化）。

---

## 12. 日志规范

### 12.1 格式

```csharp
public static class Log
{
    public static void Info(string tag, string msg);
    public static void Warn(string tag, string msg, Object ctx = null);
    public static void Error(string tag, string msg, Object ctx = null);
}
```

- **`tag` 是常量**，定义在各自域里：`const string CatLogTag = "Cat";`、`"Nav"`、`"Feeding"`、`"Save"`、`"Ghost"`、`"Audio"`。
- 输出格式：`[Cat] 猫进窝贴垫：垫面 y=0.3200，修正 +0.0031m`（现有 `CatBedController` 已是这个风格，保持）。
- **带数据**，不要"出错了"。`Log.Warn("Nav", $"尝试贴到不可走格 {p}，已拒绝")`。

### 12.2 级别

| 级别 | 什么时候用 | 例子 |
|---|---|---|
| `Info` | 状态机的关键转移、一次性里程碑 | 阶段切换、存档完成、鬼激活 |
| `Warn` | 配置异常但能继续 | "没找到垫子表面，用兜底值"、"寻路失败重试" |
| `Error` | 数据不一致、必须修的 bug | "SceneId 重复"、"AudioCatalog 缺 clip" |
| **不许用 `Debug.Log`**（直接调用） | — | 统一走 `Log`，方便以后一键关掉 |

### 12.3 每帧日志的红线

- `Update` 里的日志必须 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包裹，或用 `if (logEnabled)` 开关（`CatAgent.logRings` 是正确示范）。
- 现有 `CatAgent.logPhase`/`CatBell.logRings` 保留，但新增代码**默认关闭**。

### 12.4 出包剥离

`Log.Info` 在 `Release` 下应为空实现（`[Conditional("UNITY_EDITOR")]` 或 `#if`），避免字符串拼接的 GC 与信息泄漏。

---

## 13. 禁用 API 清单

| API | 为什么禁 | 替代 |
|---|---|---|
| `GameObject.Find("X")` | 全场景字符串遍历；改名即崩；性能差 | 序列化字段 / `EntityRegistry` / `ServiceHub` |
| `FindObjectOfType<T>()` / `FindObjectsOfType<T>()` | 场景全扫，随对象数线性增长 | 同上。现有 6 处（`CatBedController.Awake`、`CatAgent.CollectDoors`、`SleepRoutine` 等）标为"待清理"，新代码禁止 |
| `Camera.main`（每帧） | 内部是 `FindGameObjectWithTag` | `Awake` 缓存一次（`FirstPersonController:99` 已是正确做法） |
| `GetComponent<T>()`（每帧） | 反射式查找 | `Awake` 缓存 |
| `SendMessage` / `BroadcastMessage` | 字符串 + 反射，无法静态检查 | C# event / 接口 |
| `Resources.Load` | 隐式依赖、无法追踪引用、打包难优化 | 序列化引用 / SO 总表（`AudioCatalog`） |
| `OnGUI`（新代码） | 每帧 IMGUI 重建，有 GC | uGUI |
| `string.Format` / 字符串插值（每帧） | 堆分配 | 缓存或 `Log` 剥离 |
| LINQ（每帧） | 堆分配 + 闭包 | 手写循环 |
| `foreach` 遍历非泛型 `IEnumerable` | 装箱 | 用 `List<T>` / 数组索引 |
| `public static` 可变字段 | 全局状态，无法测试、存档污染 | `ServiceHub` 的接口字段 / 实例字段 |
| `Time.timeScale = 0` 之外的"暂停"手段 | 会让 `Update` 里的计时与 `WaitForSeconds` 不一致 | 统一走 `GameState.Paused` + `timeScale` |
| `async void` | 异常吞掉 | 协程 |
| `Object.Instantiate` 高频调用 | GC 尖峰 | 对象池（§10） |
| `Animator` 上的 `applyRootMotion = true` | 与程序化位移抢 Transform（项目已 0 个） | 保持 `false`，位移由 Motor 写 |
| 反射（生产代码） | IL2CPP 裁剪、性能、无法追踪 | 接口/委托。**例外**：编辑器工具与自测里允许 |

> 检查方法：`git diff --name-only` + `grep -nE "Find\(|FindObjectOf|GetComponent<|Resources\.Load|SendMessage"`。

---

## 14. 提交规范

### 14.1 粒度：一个 BACKLOG 任务 = 一次提交

**一次提交只能属于一种性质**：`feat` / `fix` / `refactor` / `perf` / `test` / `docs` / `chore` / `build`。

不许出现"顺手改了个 typo + 加了新功能 + 重构了一个类"的混合提交。理由：出问题时 `git revert` 要能精准摘掉一块。

### 14.2 信息格式（Conventional Commits）

```
<type>(<scope>): <中文简述，不超过 40 字>

[可选 3~6 行：为什么这么改 / 关键决策 / 不这么改会怎样]
```

**`type`**：`feat` `fix` `refactor` `perf` `test` `docs` `chore` `build`
**`scope`（只能用这些）**：`core` `nav` `entities` `player` `cat` `ghosts` `world` `ui` `audio` `save` `editor` `repo` `scene` `build` `docs`

示例：

```
feat(entities): 建立组合式实体框架骨架

EntityStateMachine 从 CatBrain 提炼，保持纯 C#；
EntityControl 沿用 CatAgent 的三级锁语义（AI/Transform/Pose）。
新增 8 条 EditMode 单测覆盖迟滞与一次性锁。
```

### 14.3 提交前自检清单（13 项，逐条打勾）

```
□ 1. 编译 0 错误 0 警告（Unity Console 清空后重新编译一次）
□ 2. CatFeedingSelfTest 跑过，日志末尾 RESULT: PASS，WARN 0 / ERR 0
□ 3. git status 只含本任务的文件（没有 .meta 漏、没有场景意外改动）
□ 4. 本次如果真的动了场景/Prefab：Play 一次，Hierarchy 里 0 个 Missing 组件
□ 5. 新增的 [SerializeField] 都有 Tooltip
□ 6. 新增的 event += 都有对应 -=（数量相等）
□ 7. 新增的 public 方法/类型有 XML 注释（至少一行说"干什么"）
□ 8. 没有新增 GameObject.Find / FindObjectOfType / GetComponent（在 Update 里）
□ 9. 没有新增 OnGUI
□ 10. 新增枚举值都在末尾（没有重排）
□ 11. 没有重命名已挂载的 MonoBehaviour 类
□ 12. 如果加了存档字段：要么写了迁移函数，要么给了默认值
□ 13. 提交信息符合 §14.2 格式，且正文说明了"为什么"
```

### 14.4 分支与回滚

- 单人开发：直接在 `main` 上提交，但**每个阶段（A/B/C/D/E）打一个 tag**：`git tag phase-a-done`。
- 重构类任务（🔁）失败时：`git reset --hard HEAD` 重来比"在废墟上打补丁"快。这就是"一任务一提交"的价值。
- **`git commit --amend` 只在还没推送时用**；`git reset --hard` 之前先 `git stash` 或另开分支。

---

## 15. 自测扩展规范（如何给 `CatFeedingSelfTest` 加步骤）

### 15.1 它是什么、不是什么

- 它是**端到端回归测试**，跑真实场景、真实时间，产出 `.workbuddy/selftest.log`。
- 它**不是**单元测试。逻辑判定放 `Tests/EditMode`（`CatBrainTests` 是范例），端到端只验证"串起来能不能用"。

### 15.2 加一个步骤的标准动作

```csharp
// 1. 先写日志行（格式：段号 + 一句话 + 关键数值）
Log("S1 上床交互：prompt=" + prompt + " canInteract=" + can);

// 2. 触发动作（尽量用真实入口，不要直接改私有字段）
var bed = FindObjectOfType<BedInteractable>();
bed.Interact();

// 3. 等条件成立，带超时（不要裸 WaitForSeconds 一大段）
yield return WaitUntil(() => GameStateManager.Instance.State == GameState.Sleeping, 5f);

// 4. 断言（失败必须让 RESULT 变 FAIL）
Assert("S2 进入 Sleeping", GameStateManager.Instance.State == GameState.Sleeping);
AssertNear("S3 时间推进 ≥ 6h", hoursAdvanced, 6f, 0.5f);
AssertLess("S4 疲劳归零", ServiceHub.Stats.Fatigue01, 0.02f);

// 5. 截图（每段一张，便于事后肉眼复核）
yield return Shot("s_sleeping");
```

### 15.3 断言规范

| 规则 | 说明 |
|---|---|
| **每条断言必须能被一句话说清** | 说不清说明这个断言测的不是行为 |
| **必须带数值** | `AssertNear("时间推进", actual, 6f, 0.5f)` 而不是 `Assert("时间推进了")` |
| **容差要写出理由** | 0.5h 容差是因为自测按帧推进有抖动 |
| 失败继续跑 | 一条失败不 `yield break`，攒到最后统一报，一次能看到全部问题 |
| **末尾统一输出** | `RESULT: PASS` / `RESULT: FAIL(3 项)` + 失败清单 |

### 15.4 什么算通过

```
[OK] 24 步全部执行
[OK] RESULT: PASS
[OK] WARN 0
[OK] ERR 0
```

**三个条件缺一不可。** 有 `WARN` 也算不通过——因为警告往往就是下一个 bug 的早期信号（现有 13 个空 clip 这类问题，就是靠"0 警告"标准被发现的）。

### 15.5 新增步骤时的禁止事项

1. **不许为了绕过 bug 而放宽断言**。断言失败先查是实现错了还是断言错了，写在提交信息里。
2. **不许在自测里 `#if` 掉功能代码**（例如"自测模式下跳过动画"）。自测必须跑真实路径。
3. **不许让自测总时长超过 8 分钟**。超了说明该拆成两条用例（"喂食链路"与"睡眠链路"分开）。
4. 自测里**允许用反射**读私有字段（它是测试）；生产代码里反射一律打回。
5. 自测里 `FindObjectOfType` **允许**（它是测试入口）；生产代码里禁止。

---

## 16. 代码自查 20 条（写完一个功能，逐条自问）

```
□ 1. 这个类超过 500 行了吗？超过就该按"谁写什么"拆
□ 2. 有没有第二个系统在写同一个 Transform / Animator 参数？
□ 3. 我引用的程序集，在 §3.1 矩阵里是合法的吗？
□ 4. 我在 Update 里 new 了吗？拼字符串了吗？
□ 5. 我加的字段有 Tooltip 和单位吗？
□ 6. 我加的事件有配对的退订吗？
□ 7. 我的枚举值加在末尾了吗？
□ 8. 我改了已挂载类的名字吗？（不该改）
□ 9. 我用了 Find* / GetComponent / Camera.main 吗？
□ 10. 我的协程有超时和退出条件吗？
□ 11. 我的失败路径有日志吗？日志带数据吗？
□ 12. 这个状态有三个以上系统在抢吗？需要 ControlLock 吗？
□ 13. 我加的 SO 里有运行时状态吗？（不该有）
□ 14. 新对象需要池化吗？（> 2 次/秒才池化）
□ 15. 我的类能写 EditMode 单测吗？如果逻辑复杂却测不了，说明逻辑和 Unity 耦合了
□ 16. 我把"决策"和"表现"分开了吗？
□ 17. 如果这个功能明天要加第二个变体，我需要复制粘贴吗？（需要 → 抽象错了）
□ 18. 自测能覆盖这个功能吗？加步骤了吗？
□ 19. 提交信息说明了"为什么"，不只是"做了什么"吗？
□ 20. 如果三个月后的我看到这段代码，能在 2 分钟内看懂它在干什么吗？
```

---

## 17. 快速索引（卡住时看哪一节）

| 症状 | 看 |
|---|---|
| `CS0246: 找不到类型` | §3.3（三种正当解法） |
| 场景里组件变 Missing | §2.3（移动文件必须带 `.meta`） |
| 第二次进游戏逻辑跑两遍 | §8.4（事件泄漏） |
| 帧率掉 / GC 尖峰 | §9.3、§11.3（`Update` 与协程分配） |
| 不知道新东西放哪个目录 | §2.1 |
| 不知道该不该抽接口 | §4.2（第三次法则） |
| 想加个大框架 | ARCHITECTURE §12 ADR-002/003（先看拒绝了什么） |
| 不知道今天做什么 | `BACKLOG.md` 附录 B |
