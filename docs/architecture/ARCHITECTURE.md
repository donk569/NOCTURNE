# 《夜隙 NOCTURNE》技术架构（v1.0）

> 适用工程：`D:\unity\NOCTURNE` · Unity 2022.3.62f3c1 · URP 14.0.12 · Windows 单机
> 本文是**裁决书**，不是选项单。每条结论都给"为什么是它"和"为什么不选别的"。
> 配套文件：`docs/production/BACKLOG.md`（按天执行清单）、`docs/architecture/DEVELOPMENT_GUIDE.md`（编码规范）。

---

## 0. 一句话结论

**在现有 44 个脚本上做"分层程序集 + 组合式实体框架 + 唯一写入者原则 + 受控事件 + 拉取式存档"的渐进演进，一次也不重写。**

展开成五条支柱，后文每一条都对应到具体类名和文件：

| # | 支柱 | 一句话 | 直接解决的债务 |
|---|---|---|---|
| 1 | **分层程序集** | 用 9 个运行时 `.asmdef`（+4 个非运行时）让编译器替你守边界，而不是靠自觉 | 债 9、债 1、债 7 |
| 2 | **组合优于继承** | 一个实体 = Profile 数据 + 若干行为组件；新的鬼只加组件，不复制类 | 债 1、约束 3 |
| 3 | **数据驱动** | 阈值/时长/资源引用全在 SO 或 Inspector，代码里不出现魔法数字 | 债 5、约束 4 |
| 4 | **受控事件** | 只有三种通信方式（typed event / SO 通道 / 接口直连），永不出现字符串总线 | 债 4、约束 1 |
| 5 | **单一真值 + 单一写入者** | 谁拥有状态、谁写 Transform、谁写 Animator，各只有一个 | 债 1、债 4、约束 5 |

三条**不做**：不做联机相关的一切抽象；不做完整 ECS；不做通用框架化（不为"以后可能用到"付代价）。

---

## 0.1 产品决策（已拍板 · 2026-11-15）

以下三条已由项目负责人确认，后续开发不再反复讨论；如需变更，走 ADR 追加而不是口头改。

| 决策 | 结论 | 影响与触发器 |
|---|---|---|
| **鬼的规模** | **同屏活跃 3 只 / 全场景定义 8 只** | 见 §11.2。负责人表示"先按这个来，还不确定"——因此**必须保留扩展位**：若未来同屏活跃 > 5 只，先上 `SensesBatch`（§7 已预留，不改架构）；若 > 500 个同质实体，才重新评估 DOTS（§12 ADR-002 写死的触发条件） |
| **存档序列化** | **Newtonsoft**（`com.unity.nuget.newtonsoft-json` 3.2.x） | 见 ADR-006。由 `D02` 在 `Packages/manifest.json` 引入；`E06` 必须实测 IL2CPP 下读回，并准备 `link.xml` 防裁剪 |
| **联机** | **明确不做** | 全项目不引入任何网络抽象（§4 约束 1）；存档是唯一序列化边界 |

---

## 1. 现状基线（这是演进的起点，不是要推倒的东西）

### 1.1 硬数字

| 项目 | 数值 | 说明 |
|---|---|---|
| 运行时脚本 | 44 个 `.cs` / 12,744 行 | 其中纯运行时玩法约 **8,731 行**（已扣掉 Editor 2,656 / Debug 1,099 / TutorialInfo 258） |
| 场景 | 单场景 `SampleScene` | 卧室+客厅+阳台三区同场景，360 对象、117 Collider、**0 Rigidbody、0 NavMeshAgent**、7 灯 |
| 程序集 | **已存在 2 个 asmdef** | `NOCTURNE.Cat`（运行时）、`NOCTURNE.Cat.Tests`（EditMode）；其余全在 `Assembly-CSharp` |
| 功能完成度 | 81 项：40 完成 / 9 部分 / 32 未完成 | 喂食模块 100% 闭环，是可参考的样板实现 |
| 已验证链路 | 开场→起床→探索→拾袋→倒粮→猫吃→回窝→再唤醒 | `CatFeedingSelfTest` 24 步全通过，console 0 告警 0 错误 |

> 说明：任务书里写"没有 asmdef"，实测**已经有 2 个**（`Assets/Game/Cat/Scripts/NOCTURNE.Cat.asmdef`）。这不改变结论，反而更好办——猫域已经是一块干净的"飞地"，可以直接当分层迁移的样板。

### 1.2 已经做对的五件事（必须保住，重构时不许弄丢）

1. **决策层与导航层脱离 `MonoBehaviour`**：`CatBrain`(153) 完全**不依赖 `UnityEngine`**（文件里连 `using` 都没有，只吃 `CatSenses` 结构）；`CatNavGrid`(572) 虽然 `using UnityEngine` 用 `Vector3`/`Bounds`/`Physics.OverlapCapsule`，但它**不是 `MonoBehaviour`**、不挂场景、可由 `CatAgent` 直接 `new`。两者因此都有 EditMode 测试（`CatBrainTests` 210 行）。这是全项目质量最高的设计，新鬼的决策层必须照抄这个模式。
2. **位移唯一来源**：`applyRootMotion=False` + 0 个 `NavMeshAgent` + 只有 `CatAgent` 写 `transform.position`。这从结构上消灭了 Unity 最经典的"Animator / NavMeshAgent / CharacterController 三方抢 Transform"。
3. **开场独占控制权**：`CatOpeningController` 先缓存并禁用 `CatAgent`/`CatBodyMotion`/`CatBell`/`CatLookAt`，跳床后按原状态恢复。这是"控制权必须显式转移"的正确直觉，本文把它抽象成 `ControlLock`。
4. **交互抽象统一**：`IInteractable` + `FirstPersonController.CurrentTarget` 单一真值，12 个实现者走同一条准星射线。加新家具不再需要写新射线逻辑。
5. **可断言的自测**：`CatFeedingSelfTest` 把 24 步写进 `.workbuddy/selftest.log`，外部只看文件就知道成败。这条是后续所有重构的**安全网**，架构必须围绕"每次重构后它仍全绿"来设计。

### 1.3 两个必须写进架构的实测发现

**发现 A：`GameStateManager` 号称"全项目总闸"，实际只有 1 个消费者。**

全项目 `GameState` 相关引用共 16 处，全部在 `GameStateManager.cs`（13 处）和 `OpeningSequenceController.cs`（3 处）里。**没有任何一个系统真的在查 `IsGameplayActive`。** 审计报告里"所有时间/数值/需求系统都应该查这个"目前是一句注释。

> 架构含义：**现在扩状态机几乎零成本，但风险在于未来 20 个系统各自发明一套"游戏是否在跑"的查询**。所以扩状态机必须和"定义唯一查询入口"在同一个提交里完成（BACKLOG `A11`+`A12`）。这是一次性的窗口，错过以后每加一个系统就贵一分。

**发现 B：项目里已经并行存在 4 套"控制权"机制。**

| 机制 | 位置 | 形态 |
|---|---|---|
| `GameStateManager.IsInputLocked` | `GameStateManager.cs:43` | 状态派生，**0 个消费者** |
| `FeedingInteractionLock` | `CatFoodBagInteractable.cs:439` | `static object _owner` 单持有者锁，8 处调用，顺带改了 `FirstPersonController.interactDistance` |
| `fpc.enabled = false` | `PlayerSittingController.cs:117`、`PlayerLyingController` 路径 | 直接禁用组件 |
| `_aiSuspended` + `_transformLocked` + `_poseHold` | `CatAgent.cs`（`SuspendAi`/`SetTransformLocked`/`PlayAction`） | 实体内部三级锁，**设计正确** |

> 架构含义：`CatAgent` 的三级锁（AI 停 / Transform 锁 / 姿势保持）**就是正确答案**，只是它被关在 `CatAgent` 里、且是手写布尔量。本文把它提升为 `ControlLock`（跨系统）与 `EntityControl`（跨实体）两个通用件，把另外三套机制收敛进去。这属于"承认已有设计，把它抽出来复用"，不是新增概念。

---

## 2. 推荐架构总览

### 2.1 分层图

```
                       ┌──────────────────────────────┐
                       │  NOCTURNE.Editor / Tests      │  编辑器专用，不进包体
                       └───────────────┬──────────────┘
                                       │ 可以引用一切
┌──────────────────────────────────────▼───────────────────────────────────────┐
│  NOCTURNE.World        编排层：OpeningSequence / CatFeedingSequence / Props   │
└───┬──────────┬──────────────┬──────────────┬──────────────┬──────────────────┘
    │          │              │              │              │
┌───▼────┐ ┌───▼─────┐ ┌──────▼──────┐ ┌─────▼─────┐ ┌──────▼──────┐
│ Player │ │   Cat   │ │   Ghosts    │ │ Entities  │ │     UI      │
│  玩家  │ │  黑猫   │ │ 鬼（N 个）  │ │ 实体框架  │ │ 界面（只读）│
└───┬────┘ └───┬─────┘ └──────┬──────┘ └─────┬─────┘ └──────┬──────┘
    │          │              │              │              │
    │      ┌───▼───────┐  ┌───▼─────┐        │              │
    │      │    Nav    │  │Interaction│◄──────┴──────────────┘
    │      │ 纯 C# 网格 │  │ 交互契约 │
    │      └───┬───────┘  └────┬────┘
    │          │               │
┌───▼──────────▼───────────────▼───────────────────────────────────────────────┐
│  NOCTURNE.Core   状态机 / 控制权锁 / 时钟 / 音频服务 / 存档 / 日志 / 稳定 ID  │
└──────────────────────────────────────────────────────────────────────────────┘
```

箭头方向 = **允许的引用方向**。反向引用会被编译器直接拒绝（asmdef 不提供引用就无法 `using`）。这是本架构唯一"强制执行"的规则，其余都是约定。

### 2.2 五个支柱怎么落地

#### 支柱 1：分层程序集（用编译器守边界）

见 §5 完整依赖矩阵与迁移顺序。要点：
- `NOCTURNE.Cat` 已经存在且 `autoReferenced: true`，所以 `Assembly-CSharp` 里的 `CatFeedingSequenceController` 现在就能用 `CatAgent`——**这正是渐进迁移的技术前提**。
- `Assembly-CSharp` **不能**被 asmdef 引用。因此任何"要被新程序集使用"的类型（如 `IInteractable`、`FirstPersonController`），必须**搬进**程序集，且必须在**同一个提交里**搬完它所有的使用方。§5.3 给了严格顺序。

#### 支柱 2：组合优于继承

- **允许继承的只有两类基类**：`EntityBehaviourBase`（行为基类，提供 `OnEnter/OnExit/Tick/Timeout`）与 `EntityRig`（实体根，提供装配与缓存）。
- **禁止**：`class MirrorGhost : StalkGhost`、`class FastCat : CatAgent`、任何"为了复用代码"的继承。
- 差异化靠**换组件 + 换 Profile 参数**，见 §7.3 的三个鬼的组件配方。
- 允许的抽象手法优先级：**组合 > 接口 > 泛型 > 继承 > 复制粘贴**。复制粘贴永远排最后，但比"过早抽象"好——判断标准写在 `DEVELOPMENT_GUIDE.md` §4.2（"第三次法则"）。

#### 支柱 3：数据驱动

- 一个实体的全部可调参数放在 `EntityProfile`（SO），Inspector 只暴露**装配用**的引用。
- 现有 `CatAgent` 的 40+ 个 `[SerializeField]` 不是错，**它们就是数据驱动**，只是散在组件上。迁移时把这些字段原样搬进 `CatProfile : EntityProfile`，默认值保持一致，避免行为漂移。
- 硬编码资源路径（18 处）收敛到 `NOCTURNE.Paths` 常量类 + 一个编辑器校验器（`A21`）。

#### 支柱 4：受控事件

三种、且只有三种通信方式，判断标准见 §10：

| 机制 | 何时用 | 项目内既有正面例子 |
|---|---|---|
| C# `event`（挂在拥有状态的对象上） | 一对一或"零到多"，接收方生命周期短于发送方 | `CatFoodBowl.StateChanged` → `CatFeedingSequenceController`（**已经正确**） |
| `ScriptableObject` 通道（`GameEvent<T>`） | 一对多，发送方**不该知道**接收方是谁，且需要跨 prefab/跨场景 | 恐怖信号 `HorrorSignalEvent`（新增） |
| 接口直连（序列化字段） | 需要返回值/查询，或生命周期同生共死 | `IInteractable`、`ICatDoor`（**已经正确**） |

**明令禁止**：字符串事件总线、`SendMessage`、把 `UnityEvent` 当跨系统总线（`TriggerVolume.onEnter` 只许做 Inspector 内的本地连线）。

#### 支柱 5：单一真值与单一写入者

四条不可违反的"唯一"：

| 唯一 | 谁 | 现状 |
|---|---|---|
| Transform 位置写入者 | 每个实体只有一个 `EntityMotor`（猫是 `CatMotor`） | `CatAgent` 内部已经是唯一写入者，抽出即可保持 |
| Animator 参数写入者 | 每个实体只有一个 `EntityAnimationDriver` | `CatAgent.PlayClip/SyncAnimatorSpeed` + `CatOpeningController` 两处写 → 需要合并 |
| 骨骼二次写入者 | `CatLookAt`（头）/`CatBodyMotion`（尾）各管自己那组骨骼，且必须 `LateUpdate` | 现状正确，新鬼照抄 |
| "游戏在跑吗" 查询 | 只有 `GameStateManager` + `ControlLock`，其他系统一律向它问 | 现状未落地（发现 A），必须补 |

---

## 3. 这套架构如何消化 9 条技术债

| # | 技术债 | 架构机制 | 落地任务 | 消化时机 |
|---|---|---|---|---|
| 1 | `CatAgent` 1627 行职责过载 | 拆成 5 个组件 + 保留 `CatAgent` 作门面（调用点零改动） | `A15`~`A19` | Phase A 后半，每拆一个跑一次自测 |
| 2 | `GameState` 仅 4 态 | 追加到 9 态（**只追加不重排**）+ 派生谓词 + `ControlLock` | `A10`/`A11`/`A12` | Phase A，最先做 |
| 3 | 15 个 AudioSource 各自 `PlayOneShot`，13 个 clip 为空 | `AudioService`：按组（Master/SFX/Ambience/Voice/UI）+ 池化 + `SfxId` 枚举登记 | `A13`/`A14` | Phase A |
| 4 | `CatFoodBag` ↔ `CatFoodBowl` 双向引用 | 抽出 `IFoodContainer`（碗实现），袋只依赖接口；倒粮完成发 `PourCommitted` 事件 | `A20` | Phase A 末 |
| 5 | Editor 18 处硬编码路径 | `NOCTURNE.Paths` 常量 + `AssetPathValidator`（缺资源时工具直接报错而不是静默生成空物体） | `A21` | Phase A |
| 6 | "死代码 `CatDoorway.cs`(162)、`PlanarMirror.cs`(185)" | **`PlanarMirror` 确认死代码，删除。`CatDoorway` 不是死代码——审计报告此条有误，见下方勘误。** 它被改名为 `CatDoorPassage` 并保留 | `A02`/`A19` | Phase A |
| 7 | 3 个调试脚本 1099 行编进运行时 | `#if UNITY_EDITOR` 包裹 + 移入 `NOCTURNE.SelfTest`（Editor 平台） | `A05` | Phase A |
| 8 | 3 个禁用相机留在场景 | 删除，只留 `PlayerCam` | `A03` | Phase A |
| 9 | 没有 asmdef（实为只有猫域有） | 9 个运行时 asmdef + 4 个非运行时 | `A07`~`A09`、`A22` | Phase A 分三批 |

**共同原则：每一条都独立成一次提交，每次提交后 `CatFeedingSelfTest` 必须仍全绿。** 这是"改动边界清晰"的可执行定义。

### 3.1 勘误：审计报告 P3-2 有误（本架构已核实）

审计报告 P3-2 称"`CatDoorway.cs`（162 行）全项目零引用，建议删除"。**实测不成立**，按报告删除会直接编译失败：

| 引用位置 | 用法 |
|---|---|
| `CatAgent.cs:227` | `readonly List<CatDoorway> _doorways` |
| `CatAgent.cs:240` | `CatDoorway _pendingDoor` |
| `CatAgent.cs:604` | `new CatDoorway(door)`（在 `CollectDoors` 里由 `ICatDoor` 构造） |
| `CatAgent.cs:1090/1140/1144/1154/1167` | 关门阻挡判定 `ClosedDoorBlocking` / `FindClosedDoorAhead` |
| `CatNavGrid.cs:70/172` | `Build(..., List<CatDoorway> doorways)` / `CollectIgnored` |

它还是**纯 C# 类**（`public sealed class CatDoorway`，不继承 `MonoBehaviour`），正是"门逻辑不依赖 Unity 对象、可单测"的体现。所以本架构对它的处置是：**改名为 `CatDoorPassage`、从 `CatAgent` 里抽出成独立组件（`A19`），而不是删除**。

`PlanarMirror.cs`(185) 则**确认是死代码**：全项目唯一出现是 `MirrorPicture.cs:8` 的注释提到它。可安全删除。

> 这条勘误本身是个教训，写进 `DEVELOPMENT_GUIDE.md` §14：**删代码前必须 `grep` 类名 + 构造函数 + 泛型参数三种形态**，审计结论也要复核。

---

## 4. 这套架构如何满足 4 条未来硬约束

| 约束 | 架构回答 | 具体机制 |
|---|---|---|
| **1. 不做联机** | 全项目**不引入任何网络抽象**：没有 `Command`/`NetworkBehaviour`/`Prediction` 层，不做状态机与表现的分离。所有状态都是本地即时状态 | 反向约束：`EntityStateMachine` 只在本地跑；存档是唯一"序列化边界" |
| **2. 后续要做存档（单槽可扩多槽）** | `ISaveable` + 纯 C# DTO + `version` + 迁移链；槽位是文件路径的一层，不渗进任何游戏代码 | §9 完整方案 |
| **3. 很多"鬼"，行为差异大** | `EntityProfile` + 行为组件组合 + `UtilitySelector` 打分选行为；决策层纯 C# 可单测（照抄 `CatBrain` 模式） | §7 |
| **4. 持续导入大量新美术并赋功能** | 每类新资产对应一个"标准接入流程"：家具→`IInteractable` + `Props` 组件；生物→`EntityProfile` + 组件；道具→`IInterferable`。流程写在 `DEVELOPMENT_GUIDE.md` §4/§5 | 三条 checklist，不靠记忆 |
| **5. 每功能一提交，边界清晰** | 提交粒度由 asmdef 边界 + 任务编号共同界定：一个 BACKLOG 任务 = 一次提交 | §11 的 Phase 出口闸门 |
| **6. 每天推进一点** | `BACKLOG.md` 按 0.5/1 天粒度切分，每项自带验收标准与 commit message | `BACKLOG.md` |

---

## 5. 目录结构与 asmdef 分区

### 5.1 目标目录结构

```
Assets/Game/
├── Core/                      NOCTURNE.Core          ← 无任何 NOCTURNE.* 依赖
│   ├── Flow/                  GameStateManager.cs, GameState.cs, ControlLock.cs
│   ├── Time/                  GameClock.cs, IClock.cs
│   ├── Audio/                 AudioService.cs, SfxId.cs, SoundGroup.cs
│   ├── Save/                  ISaveable.cs, SaveService.cs, SaveFile.cs, SaveMigrator.cs, SceneId.cs
│   ├── Contracts/             IStatsSource.cs, IClock.cs, IEntityLookup.cs
│   ├── Events/                GameEvent.cs, HorrorSignalEvent.cs, HorrorSignal.cs
│   └── Diag/                  Log.cs, PerfProbe.cs
├── Nav/                       NOCTURNE.Nav           ← 不碰 MonoBehaviour/GameObject，只用数学与 Physics 静态查询
│   ├── NavGrid.cs             （由 CatNavGrid 平移改名而来）
│   ├── NavService.cs          场景唯一烘焙者 + 请求队列 + 帧预算
│   └── PathRequest.cs
├── Interaction/               NOCTURNE.Interaction
│   ├── IInteractable.cs, IInteractPrompt.cs
│   ├── IFoodContainer.cs, IInterferable.cs, ICatDoor.cs
│   └── InteractionPrompt.cs
├── Entities/                  NOCTURNE.Entities
│   ├── EntityProfile.cs (SO), EntityRig.cs, EntityControl.cs
│   ├── EntityContext.cs, EntitySenses.cs, EntityMotor.cs, EntityAnimationDriver.cs
│   ├── EntityBehaviourBase.cs, UtilitySelector.cs
│   ├── EntityStateMachine.cs  （纯 C#，提炼自 CatBrain）
│   └── EntityRegistry.cs
├── Player/                    NOCTURNE.Player
│   ├── FirstPersonController.cs（从 Assets/Art/... 搬来）
│   ├── PlayerSittingController.cs, PlayerLyingController.cs
│   ├── PlayerStats.cs, BedInteractable.cs
│   └── PlayerInteractor.cs     （从 FPC 拆出射线与提示）
├── Cat/                       NOCTURNE.Cat
│   ├── CatAgent.cs             （门面，逐步变薄）
│   ├── CatBrain.cs, CatMoods.cs, CatClips.cs
│   ├── CatMotor.cs, CatSensesSensor.cs, CatAnimatorDriver.cs, CatHunger.cs, CatDoorPassage.cs
│   ├── CatLookAt.cs, CatBodyMotion.cs, CatBell.cs, CatOpeningController.cs
│   └── CatProfile.cs (SO)      （原 40+ 个 SerializeField 的归宿）
├── Ghosts/                    NOCTURNE.Ghosts
│   ├── GhostRig.cs
│   └── Behaviour/             GazeBehaviour.cs, StalkBehaviour.cs, TeleportBehaviour.cs, InterfereBehaviour.cs
├── World/                     NOCTURNE.World          ← 编排层
│   ├── Opening/               OpeningSequenceController.cs, OpeningDialogueUI.cs, ...
│   ├── Feeding/               CatFeedingSequenceController.cs, CatFoodBowl.cs, CatFoodBagInteractable.cs, CatBedController.cs
│   ├── Horror/                HorrorDirector.cs, LightFlicker.cs, MirrorAnomaly.cs
│   └── Props/                 DrawerInteractable.cs, HingedDoor.cs, SlidingDoor.cs, ChairInteractable.cs, TriggerVolume.cs, MirrorPicture.cs
├── UI/                        NOCTURNE.UI
│   ├── CrosshairUI.cs, PromptUI.cs, HudUI.cs, PauseMenuUI.cs, MainMenuUI.cs, EndingUI.cs
│   └── ChairHoldUI.cs
├── Editor/                    NOCTURNE.Editor
│   ├── CatFeedingPropsBuilder.cs, CatLooksBuilder.cs, EditorCmdChannel.cs, BatchApply.cs, DevShot.cs
│   ├── AssetPathValidator.cs
│   └── SelfTest/              CatFeedingSelfTest.cs, CatWalkQA.cs, CatOpeningQA.cs
└── Tests/
    ├── EditMode/              NOCTURNE.Tests.EditMode
    └── PlayMode/              NOCTURNE.Tests.PlayMode
```

`Assets/Art/` 只放美术资产。当前 `Assets/Art/Environment/Bedroom/Scripts/` 下的 **11 个 `.cs`**（实测计数）全部迁走：`FirstPersonController`(416)、`ChairInteractable`(326)、`PlayerSittingController`(226)、`HingedDoor`(209)、`PlanarMirror`(185)、`MirrorPicture`(156)、`SlidingDoor`(130)、`DrawerInteractable`(119)、`ChairHoldUI`(93)、`TriggerVolume`(85)、`IInteractable`(28)。

> **迁移安全的唯一铁律**：移动 `.cs` 必须**连 `.meta` 一起移动**（用 Project 窗口拖拽，或 `git mv` 同时移动两个文件）。GUID 不变 → 场景里的 `m_Script` 引用不断。**绝对不要**"新建一个文件 → 粘贴代码 → 删旧文件"，那会让场景里 24 个挂载点全部变 Missing。

### 5.2 合法依赖矩阵（✓ = 允许引用）

| 被引用 → <br> 引用方 ↓ | Core | Nav | Interaction | Entities | Player | Cat | Ghosts | World | UI |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **Core** | — | | | | | | | | |
| **Nav** | | — | | | | | | | |
| **Interaction** | ✓ | | — | | | | | | |
| **Entities** | ✓ | ✓ | | — | | | | | |
| **Player** | ✓ | | ✓ | | — | | | | |
| **Cat** | ✓ | ✓ | | ✓ | | — | | | |
| **Ghosts** | ✓ | ✓ | ✓ | ✓ | | | — | | |
| **World** | ✓ | | ✓ | ✓ | ✓ | ✓ | ✓ | — | |
| **UI** | ✓ | | ✓ | | | | | | — |

**六条禁令**（编译器会替你执行，写在这里是为了让你看懂报错）：

1. `Core` 不许引用任何 `NOCTURNE.*`（它是地基）。
2. `Nav` 不许引用任何 `NOCTURNE.*`，连 `Core` 都不要（它只允许用 `Vector3`/`Bounds`/`Physics` 这类引擎基础 API，不许出现 `MonoBehaviour`/`GameObject`/`Find*`，以此保住 EditMode 可测性）。
3. `Cat` 和 `Ghosts` **互不引用**——这是防止"鬼抄猫"的第一道物理防线。
4. `UI` 不许引用 `Player`/`Cat`/`Ghosts`/`World`——UI 只订阅 Core 事件、只读 Core 接口（`IStatsSource`/`IClock`）。这就是"UI 不持有游戏状态"的落地方式。
5. 任何运行时程序集不许引用 `Editor` 或 `Tests`。
6. `Entities` 不许引用 `Cat`/`Ghosts`/`Player`（框架不许知道谁在用）。

**想跨层但被拦住时，只有三种正当解法**（按优先级）：
① 把契约（接口/事件类型）下沉到 `Core` 或 `Interaction`；② 由 `World` 层做中介（它是唯一允许知道所有人的层）；③ 用 SO 通道解耦。**没有第四种解法，"加个引用"不是解法。**

### 5.3 迁移顺序（每步一个提交，每步自测仍绿）

| 步 | 动作 | 关键约束 |
|---|---|---|
| 1 | 建 `Core` 目录 + 空 asmdef（`autoReferenced: true`） | 空程序集不影响任何现有代码 |
| 2 | 把**只被 Core 自己用**的类型搬进 Core（新增代码优先写进 Core） | 不动老代码，零风险 |
| 3 | 建 `Interaction` asmdef，把 `IInteractable`/`IInteractPrompt` 搬进去 | **必须同一提交内**改完 12 个实现者的 `using NOCTURNE.Interaction;`，否则编译断 |
| 4 | `Assets/Art/.../Scripts/` 全部搬到对应程序集目录 | 连 `.meta` 一起 `git mv`；搬完立刻 Play 一次确认 24 个挂载点没 Missing |
| 5 | `CatNavGrid` → `Nav/NavGrid.cs`，`NOCTURNE.Nav` asmdef | 猫域已经是 asmdef，加引用即可 |
| 6 | `Player` asmdef（`FirstPersonController` 等） | 拆 `FeedingInteractionLock` 时同步做 |
| 7 | `Entities` asmdef（新代码，无存量） | 纯新增 |
| 8 | `Ghosts` asmdef（新代码） | 纯新增 |
| 9 | `World` asmdef（把 Opening/Feeding/Props 收进来） | 最大的一次搬家，单独一个提交 |
| 10 | `UI` asmdef | 与 HUD 一起做 |
| 11 | `Editor`/`Tests` asmdef | 最后做，出错不影响运行时 |
| 12 | 全部 `autoReferenced: false`（除 Core） | 打开后 `Assembly-CSharp` 里的漏网之鱼会立刻报错——**这是本步骤的目的**，把残留一次找全 |

> 第 12 步是"真实验收"：当 `Assembly-CSharp` 里再没有一个 `.cs` 文件时，分层才算成立。目标状态：`Assembly-CSharp` 空。

### 5.4 asmdef 内容模板

以 `NOCTURNE.Cat` 为例（其余只需改 `name` 与 `references`）：

```json
{
    "name": "NOCTURNE.Cat",
    "rootNamespace": "NOCTURNE.Cat",
    "references": ["NOCTURNE.Core", "NOCTURNE.Nav", "NOCTURNE.Entities"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Nav` 用 `"noEngineReferences": false`（它仍需要 `Vector3`），但**代码里不出现 `MonoBehaviour`/`GameObject`/`Find*`**。

---

## 6. 导航：为什么共享一张网格（而不是每只鬼各烘焙一张）

### 6.1 现状成本

`CatNavGrid.Build(center, size, cell=0.12, radius=0.15, height=0.34, ...)` 的循环是**每格一次射线 + 一次胶囊重叠**（`SampleFloor` + `IsBlocked`）。区域 4.0×9.0m、格 0.12m → `ceil(4/0.12) × ceil(9/0.12) = 34 × 75 = 2,550` 格。

> 即：**每次烘焙 ≈ 2,550 次射线 + 2,550 次 `OverlapCapsule`**，外加 `CollectIgnored` 里的 `FindObjectsOfType<MonoBehaviour>(true)` 全场景扫描。

### 6.2 决策

**一张共享网格 + 多实体查询。** `NavService`（场景唯一 `MonoBehaviour`）在 `Start` 烘焙一次 `NavGrid`，所有实体（猫 + 所有鬼）通过 `INavQuery` 请求路径。

理由（带数字）：
- 5 只鬼各建一张 = **12,750 次射线 + 12,750 次重叠**的启动尖峰，且每张网格 2,550×8 数组 ≈ 100KB+ 各自一份。
- 更要命的是**一致性**：家具一动（用户会"持续导入大量新美术"），N 张网格各自 rebake 的时机不同，就会出现"猫能过、鬼不能过"的幽灵 bug。
- 单张网格 + 不同 `radius/height` 的矛盾用**分层网格**解决：`NavService` 烘焙 2 层——`Small`（r=0.15，猫）与 `Large`（r=0.40，人形鬼）。两层共享一次遍历（同一格同时算两个半径），启动成本只增加约 40%，而不是 2 倍。

### 6.3 A* 帧预算

- 路径请求进 `PathRequestQueue`，**每帧最多 2 条**（`NavService.maxPathsPerFrame`），其余排队。
- 单实体最短重规划间隔 0.4s（`CatAgent` 已有 `_scriptedRepaths < 4` 这类保护，保留）。
- 路径结果缓存：同一 `(startCell, goalCell)` 在 0.5s 内复用。
- 严格禁止在 `Update` 里同步 `FindPath`：`CatAgent` 现在的 `TryStartPath` 是同步的，猫一只没问题；**鬼多起来会卡帧**，新实体一律走队列。

### 6.4 明确拒绝

- **拒绝迁到 `NavMeshAgent`**：项目当初正是为了避免 "NavMeshAgent 与程序化位移抢 Transform" 才自建网格；且 `CatBrainTests`/`CatNavGrid` 的纯 C# 可测性会丢失（NavMesh 烘焙无法在 EditMode 单测）。
- **拒绝 A* 换流场（flow field）**：单目标多实体时流场才划算；本项目实体目标各异，A* 更简单。

---

## 7. 实体统一接入方案（猫与鬼共用）

### 7.1 七个零件

| 零件 | 职责 | 纯 C#? | 与现有代码的对应 |
|---|---|---|---|
| `EntityProfile` (SO) | 全部可调参数 + 动画/音效/资源引用 + `entityKind` 字符串 | 数据 | `CatAgent` 那 40+ 个 `SerializeField` 的归宿 |
| `EntityRig` | Prefab 根：装配组件、缓存 `Animator`/`Collider`/`HeadBone`、持有 `EntityProfile` | MonoBehaviour | `CatAgent` 的字段缓存部分 |
| `EntitySenses` | 按 `senseInterval`（复用 0.12s）采样玩家距离/可见/被注视 → 产出 `EntityContext` | MonoBehaviour（薄） | `CatAgent.UpdateSenses` |
| `EntityStateMachine` | 迟滞切换 + 一次性动作锁 + 优先级抢占 | **是** | **直接提炼自 `CatBrain`**，`CatBrain` 变成它的一个配置实例 |
| `EntityMotor` | 唯一写 Transform 者：走路径 / 直线 / 瞬移 / 停 | MonoBehaviour | `CatAgent.FollowPath/FaceDirection/PlaceAt` |
| `EntityAnimationDriver` | 唯一写 Animator 参数者：按 `Profile` 的 clip 名映射，处理 `crossFade`、`IsOneShot` | MonoBehaviour | `CatAgent.PlayClip/SyncAnimatorSpeed/EnsureAnimatorState` |
| `EntityBehaviourBase` | 行为基类：`OnEnter/OnExit/Tick(dt, in EntityContext)`、`Weight(ctx)` 打分 | 是（抽象类） | 新代码 |

外加两个通用件：
- `EntityControl`：三级锁（`AiSuspended` / `TransformLocked` / `PoseHold`），**直接把 `CatAgent` 的三级锁抽出来**。任何想动 Transform 的系统必须先 `RequestControl(reason)`。
- `EntityRegistry`：`entityId → EntityRig` 的字典，挂在 `Core`，供存档与全局查询。这消灭了 `FindObjectOfType<CatAgent>()`（现有 6 处）。

### 7.2 猫怎么接进来（不重写）

`CatAgent` **保留类名与文件**（改名会断场景里 `CAT_Nocturne` 上的引用），逐步变成门面：

```
现在：CatAgent = 感知 + 决策 + 寻路 + 移动 + 动画 + 饥饿 + 门 + 埋伏 + 脚本化移动（1627 行）
目标：CatAgent = 门面（约 200 行）
      ├── CatSensesSensor   → EntitySenses
      ├── CatBrain          → EntityStateMachine（保持不变，仍是纯 C#）
      ├── CatMotor          → EntityMotor
      ├── CatAnimatorDriver → EntityAnimationDriver
      ├── CatHunger         → 独立需求组件
      └── CatDoorPassage    → 门等待逻辑（现在混在 FollowPath 里）
```

**关键约束：对外 API 一个都不改。** `CatAgent` 现有被外部使用的成员必须原样保留为转发：

```csharp
// CatAgent 里的门面示例（外部调用点零改动）
public void SuspendAi(bool suspend) => Control.SetAiSuspended(suspend);
public bool ScriptedMove(Vector3 target, float speed) => Motor.MoveTo(target, speed);
public void SetMoodImmediate(CatMood mood) => AnimatorDriver.PlayImmediate(mood);
public Transform HeadBone => _headBone;   // CatLookAt 仍在用
public CatMood Mood => Brain.Mood;
public event Action<CatMood, CatMood> MoodChanged { add => Brain.MoodChanged += value; remove => Brain.MoodChanged -= value; }
```

外部使用者清单（**拆分过程中不许改这些文件**，否则无法判断是"拆分引入的 bug"还是"调用点改动引入的 bug"）：
`CatFeedingSequenceController`、`CatOpeningController`、`CatBedController`、`CatLookAt`、`CatBodyMotion`、`CatBell`、`CatFeedingSelfTest`、`CatWalkQA`、`CatOpeningQA`。

### 7.3 三个鬼的组件配方（证明"不复制粘贴"）

| 鬼 | 设计意图 | 组件组合 | 新代码量估计 |
|---|---|---|---|
| **凝视者**（镜子里的那个） | 只在玩家看镜子时出现；盯着玩家；玩家转头就消失 | `GhostRig` + `StaticMotor`（不动）+ `GazeBehaviour`（跟随注视）+ `ProximityTriggerBehaviour`（条件出现）+ `EntityStateMachine{Idle,Gazing,Vanish}` | 约 180 行 |
| **尾随者** | 在玩家背后保持固定距离；被回头看就冻结；灯灭时推进 | `GhostRig` + `NavGridMotor` + `StalkBehaviour`（背后追踪 + 视线检测）+ `DarknessSpeedModifier` + `EntityStateMachine{Hidden,Approaching,Frozen}` | 约 260 行 |
| **干扰者**（物品异常） | 不开门不追人，专门让抽屉自己开、灯自己闪、画换掉 | `GhostRig`（无 Model，纯逻辑）+ `InterfereBehaviour`（找最近的 `IInterferable` 施法）+ `CooldownBehaviour` | 约 150 行 |

复用统计：`NavGridMotor`/`GazeBehaviour` 的骨骼注视/`CooldownBehaviour`/`EntityStateMachine`/`EntitySenses` 全部共享；**三个鬼合计新代码约 590 行，而复制猫的做法会是 3×1600 = 4800 行。**

### 7.4 禁止事项

1. 不许 `class XxxGhost : CatAgent`。
2. 不许在鬼里 `FindObjectOfType<CatAgent>()` 读猫状态——要读就订阅 `Core/Events` 里的事件。
3. 不许新鬼自带一套 `Update` 里跑 A*；一律 `NavService` 队列。
4. 不许每只鬼建一个 `Animator`（预算见 §11）。

---

## 8. 状态机与控制权（`GameState` 扩到 9 态）

### 8.1 枚举：只追加，不重排

```csharp
public enum GameState
{
    Opening     = 0,   // 开场演出：全锁
    Lying       = 1,   // 躺卧观察：只允许转头
    StandingUp  = 2,   // 起床过渡
    Playing     = 3,   // 正常游玩
    Sleeping    = 4,   // 睡眠中（时间加速推进，玩家输入锁）
    Cutscene    = 5,   // 通用过场（开场之外的新演出都用它）
    Paused      = 6,   // 暂停（Time.timeScale = 0）
    Ended       = 7,   // 结局，输入锁死
    Loading     = 8    // 读档/加载（预留）
}
```

> **铁律**：`GameState` 的数值已经在场景里被 `GameStateManager._state` 序列化（当前值 0）。**只许在末尾追加，永不重排、永不删除**。同理适用于 `CatMood`、`FeedingPhase`、`CatFoodBowlState`、`CatFoodBagState`。这条写进 `DEVELOPMENT_GUIDE.md` §1.6。

### 8.2 派生谓词（所有系统的唯一查询入口）

```csharp
public bool IsPlayerControlled  => _state == GameState.Playing;                       // 玩家能动
public bool IsSimulationRunning => _state == GameState.Playing
                                || _state == GameState.Sleeping;                      // 时间/需求/鬼 AI 推进
public bool IsTimeAdvancing     => IsSimulationRunning;                               // 睡眠时加速
public bool IsEntityAiRunning   => _state == GameState.Playing
                                || _state == GameState.Cutscene;                      // 鬼保持在过场里也有表现
public bool IsUiModal           => _state == GameState.Paused || _state == GameState.Ended;
```

> 注意 `IsGameplayActive` 语义会变（`Sleeping` 是否算"在玩"？）。**处理办法**：保留 `IsGameplayActive` 名字但改为 `=> IsSimulationRunning`，并把每个调用点显式改成它真正想问的谓词。当前只有 1 个消费者（发现 A），改动成本极低——**这是最后的机会**。

### 8.3 `ControlLock`（跨系统控制权，替代三套旧机制）

```csharp
// 引用计数式"谁在阻止什么"，不是布尔量
public static class ControlLock
{
    public enum Reason { Cutscene, Sitting, Lying, Pouring, Holding, Sleeping, Pause, UiModal }
    public static void Acquire(Reason r, object owner);
    public static void Release(Reason r, object owner);
    public static bool Blocks(Reason r);
    public static void ReleaseAll(object owner);   // 谁 Acquire 谁 Release，OnDisable 兜底
}
```

替换映射：
| 旧 | 新 |
|---|---|
| `FeedingInteractionLock.Lock(this)` | `ControlLock.Acquire(Reason.Pouring, this)` |
| `fpc.enabled = false`（`PlayerSittingController:117`） | `ControlLock.Acquire(Reason.Sitting, this)` + `FirstPersonController` 内部查锁 |
| `lying.EnableLook(false)` 之外的硬禁 | `ControlLock.Acquire(Reason.Lying, this)` |
| `catAgent.enabled = false`（`OpeningSequenceController:130`） | `cat.Control.SetAiSuspended(true)`（实体级，不是跨系统级） |

> `FeedingInteractionLock` 顺带改了 `FirstPersonController.interactDistance` 再还原——这是典型的"用副作用做开关"。改成锁之后，`FirstPersonController` 每帧问 `ControlLock.Blocks(...)`，不再有"还原失败就永久坏掉"的风险。

---

## 9. 存档方案

### 9.1 三条原则

1. **拉取，不推送**：保存时 `SaveService` 逐个问 `ISaveable.CaptureSave()`；加载时逐个 `RestoreSave(dto)`。不用事件、不用消息——因为**顺序**和**完整性**必须可控。
2. **DTO 是纯 C#，与 MonoBehaviour 零关系**：`[Serializable] class PlayerSaveData { public float fatigue; ... }`。理由：DTO 要能被单测、能被迁移函数改写、不能随 Prefab 改结构而崩。
3. **存档里绝不出现 `Transform` 的浮点坐标（除鬼）**，用稳定 ID 映射。

### 9.2 稳定 ID（`SceneId`）

```csharp
// 挂在需要被存档引用的对象上
public sealed class SceneId : MonoBehaviour { [SerializeField] string _id; public string Id => _id; }
```
- `_id` 手工/工具生成，形如 `bedroom.nightstand.drawer_top`、`living.bowl`。
- 编辑器工具（`SceneIdValidator`）在保存时检查：**场景里所有 `SceneId` 非空、无重复**，否则打印红色错误并阻止进 Play。
- 玩家位置：不存坐标，存"最近锚点 ID + 相对朝向"（`PlayerAnchor`，例如 `bedroom.bed.left`）。这样换房间布局不会把人塞进墙里。

### 9.3 文件格式与版本

```jsonc
{
  "version": 1,                 // 每次结构性变更 +1，只增不减
  "createdAtUtc": "2026-05-01T12:00:00Z",
  "playSeconds": 3721.5,
  "gameState": "Playing",       // 字符串，不用 int（可读 + 抗重排）
  "clock": { "day": 3, "hour": 2, "minute": 15 },
  "player": { "fatigue": 0.82, "sanity": 0.44, "anchorId": "bedroom.bed.left", "yaw": 184.2 },
  "flags": ["fed_cat_first_time", "mirror_seen_once"],
  "world": { "bowl": { "amount": 0.0, "state": "EmptyAfterEating" },
             "bag":  { "state": "OnTable" },
             "mirror": { "pictureIndex": 2 } },
  "entities": [
    { "id": "cat", "kind": "cat", "hunger": 0.31, "spotId": "living.bowl", "state": "Sleeping" },
    { "id": "ghost_mirror", "kind": "gaze", "active": true,
      "pos": [1.2, 0.0, -3.4], "yaw": 90.0, "state": "Gazing", "cooldown": 12.5 }
  ]
}
```

序列化选型：**`com.unity.nuget.newtonsoft-json`（3.2.x）**。

| 备选 | 裁决 |
|---|---|
| `JsonUtility` | ❌ 不支持 `Dictionary`、不支持 `null`、**不支持多态**、不支持顶层数组。鬼的状态天生多态，硬套会逼出"一个塞满所有鬼字段的上帝类"。 |
| `System.Text.Json` | ❌ 2022.3 无官方 UPM 包，手塞 DLL 会带来 IL2CPP/AOT 的额外坑。 |
| 自定义二进制 | ❌ 单人项目，出问题时无法用文本编辑器肉眼定位，调试成本远高于收益。 |
| **Newtonsoft** | ✅ Unity 官方 NuGet 包，`Dictionary`/多态/`JObject` 迁移都好用，出问题能直接看 JSON。 |

**安全设置**：`TypeNameHandling.None` + 显式 `kind` 字符串 + 工厂方法还原。理由：`TypeNameHandling.All` 会让"重命名一个类"直接废掉所有老存档，而且反序列化任意类型有安全风险。

### 9.4 迁移链

```csharp
static JObject Migrate(JObject root)
{
    int v = root.Value<int>("version");
    if (v < 1) throw new SaveException("未知版本");
    if (v == 1) { root = SaveMigrator.V1ToV2(root); v = 2; }
    if (v == 2) { root = SaveMigrator.V2ToV3(root); v = 3; }
    return root;
}
```
- 每加一个字段：**要么**加一个迁移步骤，**要么**在 DTO 里给默认值（`public float sanity = 1f;`）。二选一，写进提交前自检清单。
- 存档保留 `.bak`（上一次成功存档）+ 加载失败时自动回退并提示。
- 写文件用 `写 .tmp → File.Replace`，防止断电产生半截文件。

### 9.5 进 vs 不进存档（明确清单）

| 进存档 | 理由 |
|---|---|
| 游戏时间（day/hour/minute） | 昼夜循环的根 |
| 玩家数值（疲劳/理智）+ 所处状态 | 生存压力的根 |
| 玩家所在锚点 ID + 朝向 | 不用坐标，抗布局改动 |
| 猫：饥饿值、当前 mood、所在 spot ID、是否在窝 | 玩家最在意"猫还在不在" |
| 猫碗 `FoodAmount` + `state`、猫粮袋 `state` | 喂食闭环的状态 |
| 每只鬼：是否激活、**世界坐标 + yaw**、状态、冷却 | 鬼没有稳定落点，坐标是唯一可行方案（唯一例外） |
| 镜子当前画面索引、灯光开/关与损坏标记 | 恐怖氛围的可见结果 |
| 已触发事件集合（字符串 flags） | 一次性事件不能重复播 |
| 结局进度/已达成结局列表 | 终局系统 |
| 随机种子 | 保证读档后随机事件序列可复现 |

| 不进存档 | 理由 |
|---|---|
| 任何家具/门/抽屉的 Transform | 由 `SceneId` + 逻辑状态还原；存坐标会在改布局后穿模 |
| Animator 参数、当前 clip 时间 | 读档时按状态重新播，从 0 开始没问题 |
| 材质实例、粒子状态、AudioSource 播放位置 | 纯表现，重进即重建 |
| 寻路中间态（路径点、卡住计时） | 读档=重新规划，比恢复更可靠 |
| 协程进度 | 用"幂等的阶段恢复"：读档时把流程置到该阶段的起点（例如睡觉存档 → 读档从"刚躺下"开始） |
| 相机/UI 的瞬时状态 | 由 `GameState` 重建 |

### 9.6 自动存档时机

1. 上床睡觉完成（进入 `Sleeping`）时；
2. 起床完成（`Sleeping → Playing`）时；
3. 喂食流程 `Sleeping` 阶段达成时；
4. 每个"里程碑 flag"写入时（例如首次看到镜子异常）；
5. 手动 `F5`/`F9`（**仅 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`**）。

存档体积估算：< 30 KB JSON。序列化耗时 < 2ms，主线程直接写足够；若实测 > 16ms（Profiler 标记 `Save.Capture`），把**文件 I/O** 挪 `Task.Run`，**DTO 捕获必须留在主线程**（DTO 读取的是 Unity 对象）。

---

## 10. 系统间通信规范（给判断标准，不给口号）

### 10.1 五个问题决定用什么

按顺序问，第一个"是"就停：

```
Q1  需要返回值 / 需要查询"你现在是什么状态"吗？
    → 是：接口直连（序列化字段注入实现方）
Q2  数据是否要进存档？
    → 是：ISaveable 拉取（不走任何事件）
Q3  接收方是刚好 1 个，且生命周期由发送方掌握吗？
    → 是：C# event（订阅在 OnEnable，退订在 OnDisable）
Q4  发送方是否"不该知道接收方是谁"，且接收方 0..N 个、可能跨 prefab？
    → 是：ScriptableObject 通道（GameEvent<T>）
Q5  以上都不是？
    → 说明你的设计还没想清楚，回去重画职责边界。
```

### 10.2 项目内既有代码的裁决（逐条）

| 现有通信 | 裁决 | 理由 |
|---|---|---|
| `CatFoodBowl.StateChanged` → `CatFeedingSequenceController` | ✅ 保持 | Q3：一对一，流程控制器掌握订阅 |
| `TriggerVolume.Entered` → `MirrorPicture` | ✅ 保持 | Q3：触发器与使用方 1:1 |
| `PlayerLyingController.GetUpFinished` → `OpeningSequenceController` | ✅ 保持 | Q3 |
| `CatFoodBagInteractable` ↔ `CatFoodBowl` 互相持有 | ❌ 改 | 违反"单向"。改 `IFoodContainer` + `PourCommitted` 事件（Q1 + Q3） |
| `CatAgent.MoodChanged` → 无消费者 | ⚠️ 保留但接上 | 未来 `CatWarningBehaviour`（恐怖预警）会订阅它 |
| `FirstPersonController.Footstep` 事件 | ✅ 保持 | 未来脚步引鬼用 |
| `GameStateManager.StateChanged` | ✅ 保持，但加类型化载荷 | 接收方会越来越多，改为 `Action<GameStateChange>`（含 prev/next/reason）避免签名再改 |
| `UnityEvent onEnter/onExit`（`TriggerVolume`） | ⚠️ 仅限 Inspector 本地连线 | 不许脚本 `AddListener` 做跨系统通信 |
| `static CatFoodBagInteractable.Held` | ❌ 改 | 静态可变全局状态 = 存档与多实例的噩梦。改为 `HeldBagId` 存在 `Core` 的一个 `RuntimeState` 里 |

### 10.3 SO 通道规范

```csharp
public abstract class GameEvent<T> : ScriptableObject
{
    public event Action<T> Raised;
    public void Raise(T value) { Raised?.Invoke(value); }
}
public sealed class HorrorSignalEvent : GameEvent<HorrorSignal> { }
public readonly struct HorrorSignal { public HorrorKind Kind; public string SourceId; public float Intensity; public Vector3 Position; public float Duration; }
```

规则：
- 通道数量上限 **6 个**。超过 6 个说明在用它当总线，回头用 Q1~Q5 重新判断。
- 通道**只传值，不传引用**（不许把 `GameObject` 塞进 `HorrorSignal`），否则引用生命周期会漏。
- 发送方 `Raise`，接收方在 `OnEnable` 订阅、`OnDisable` 退订。**一个 `Raised +=` 附近必须能在 20 行内看到 `Raised -=`。**

### 10.4 事件地狱的反面清单（出现即打回）

1. 为了"方便"加一个 `EventBus.Publish("cat_hungry")` —— **字符串，编译期不查错，禁止**。
2. 事件里再 Raise 事件超过 2 层（A→B→C）—— 调用链不可读，改成 `World` 层显式编排。
3. 用事件修改"谁拥有状态"的对象（收到事件后改别人的字段）—— 状态只能由拥有者改。
4. 用事件做"初始化顺序"依赖（Awake 时序问题）—— 用序列化字段显式连线。

---

## 11. 性能预算与红线

### 11.1 目标与预算（1080p / 中端独显 / 60 FPS）

| 指标 | 预算 | 红线（超了必须停下优化） |
|---|---|---|
| 帧时间 | 16.6 ms | 连续 30 帧 > 20 ms |
| 主线程 CPU | ≤ 8 ms | > 11 ms |
| GPU | ≤ 12 ms | > 15 ms |
| **每帧托管分配（稳定态）** | **0 B** | 任意单帧 > 2 KB |
| `Update` 中的 MonoBehaviour 数量 | ≤ 40 | > 60 |
| 同屏 `Animator`（含 `SkinnedMeshRenderer`） | ≤ 6 | > 10 |
| 每帧射线/重叠查询 | ≤ 8 | > 20 |
| 每帧 A* 请求 | ≤ 2 | > 4 |
| 同时播放 3D 音源 | ≤ 16 | > 24 |
| 实时投影灯 | ≤ 3 | > 5 |
| 场景 GameObject 数 | ≤ 600 | > 900 |

### 11.2 鬼的数量级假设（**这是全篇最需要拍板的数字**）

- **设计上限：同屏活跃 3 只，全场景定义 8 只。**
- 依据：3 只 × （1 个 `EntitySenses` 采样 8 Hz + 1 个 Motor + 1 个 Animator）≈ 3 个 `Update` + 3 个 Animator，在 §11.1 预算内很宽松。
- 若未来要 5 只以上活跃：**必须**先把 `EntitySensors` 收敛成 `SensesBatch`（一个 Manager 每帧统一算所有实体的感知，实体从快照读），这是在 §7 的框架里预留的位置，不需要改架构。
- 瞬时鬼（只出现 0.5~2 秒的"闪现场景"）**不进活跃集合**：它们是对象池里的 VFX Prefab，不跑 `EntitySenses`。这一条能把"看起来很恐怖但其实很便宜"的体验做满。

### 11.3 `Update` 数量治理（当前 17 个文件有 `Update`）

分层节流，写进 `DEVELOPMENT_GUIDE.md` §9：

| 频率 | 用途 | 实现 |
|---|---|---|
| 每帧 | 玩家移动、相机、Motor 位移 | `Update`/`LateUpdate` |
| 8 Hz（0.12s） | 感知（`CatAgent.senseInterval` 已是 0.12） | 累加器 |
| 2 Hz | 需求（饥饿/疲劳）、鬼的"决策" | 累加器 |
| 事件驱动 | UI 刷新、门开、提示、音效 | `event` / `Invoke` |

`GameClock` 提供 `Tick8Hz`/`Tick2Hz` 的**静态分发**（一个 `Update` 分发所有订阅者），把 N 个 `Update` 变 1 个。

### 11.4 对象池适用范围（不要无脑池化）

| 该池化 | 不该池化 |
|---|---|
| 猫粮颗粒（`CatFoodBowl.kibbles` 已经预生成，别改成实例化） | 家具（常驻） |
| 鬼的闪现 Prefab、碎片、纸张飘落 | 猫、玩家（单例） |
| 惊吓音效的临时 `AudioSource` | 门/抽屉（常驻） |
| 粒子系统 | UI 元素（常驻） |

阈值：**每秒创建销毁 > 2 次的对象才池化。** 否则池化带来的状态重置 bug 比 GC 成本更贵。

### 11.5 GC 热点现状与对策

现状是好的：`CatNavGrid` 全部用预分配 `List<int>`/`float[]`，`CatAgent` 的 `_path`/`_scratchPath` 都是 `readonly List<Vector3>` 复用。**要保住这个水平**，新增代码的红线：

1. `Update`/`LateUpdate` 里禁止：`new` 任何 class、装箱、字符串拼接、`foreach` 非泛型集合、`string.Format`、LINQ、`GetComponent`、`Find*`、闭包 lambda 捕获。
2. 每帧日志必须 `#if` 掉或用 `Log.Verbose` 空实现（见 §12 日志规范）。
3. 字符串只允许在"进存档/一次性事件"时拼。

自检方法：Profiler → `GC Alloc` 列，从 `Update` 开始的深度 2 层内必须全是 0 B。

### 11.6 渲染侧现状与建议（URP 14 / 2022.3）

- 7 盏灯：**最多 3 盏实时投影**，其余改 Baked（`Lightmapping` 已有场景基线，重烘焙成本可控）。这是"恐怖游戏里灯必须会闪"的前提——闪烁灯若也投影，成本翻倍。
- `Global Volume` 只允许 1 个，后处理参数改动走"预设 SO"而不是运行时 `new VolumeProfile`。
- 镜子已用 `MirrorPicture`（静态图 + Unlit），**不要退回实时反射**（`PlanarMirror` 已删除，别复活）。

---

## 12. 架构决策记录（ADR）

### ADR-001：用程序集分区强制分层边界

**背景**：44 个脚本全在 `Assembly-CSharp`（除猫域）。未来要加大量鬼与家具，最危险的不是"写不出来"，而是"鬼代码抄了猫代码、UI 直接改玩家数值、Core 反向依赖玩法"，一旦发生，单人项目很难再拉回来。

**决策**：按 §5.2 建 9 个运行时程序集（+ `Editor` / `Tests.EditMode` / `Tests.PlayMode` / `SelfTest` 共 4 个非运行时），依赖方向由编译器强制。迁移顺序见 §5.3，分 12 步、每步一个提交，最终目标 `Assembly-CSharp` 为空。

**后果**：
- ✅ 违规依赖变成**编译错误**，而不是代码评审时的口头提醒。
- ✅ 单文件改动只重编所在程序集（12k 行规模下收益有限，但随体量增长会显现）。
- ✅ "鬼不能引用猫"由编译器保证，物理上阻止了最担心的复制粘贴。
- ⚠️ 每次搬家都是"断编译"操作，必须一次提交内完成；搬家时忘记带 `.meta` 会导致场景挂载丢失（已在 §5.1 立铁律）。
- ⚠️ 13 个 asmdef 文件本身要维护引用列表，加错引用报 `CS0246` 时要能看懂（`DEVELOPMENT_GUIDE.md` §3.3 给了三种正当解法）。

**备选**：单一程序集 + 代码评审。❌ 拒绝：单人项目没有评审，靠自觉等于没有边界。

---

### ADR-002：拒绝完整 ECS / DOTS

**背景**：Unity 官方推荐 ECS 用于"大量同质实体 + 高性能"，而本项目未来要有"N 个鬼"，看起来像是 ECS 的用例。

**决策**：**不引入** Entities/DOTS 包，继续用 MonoBehaviour + 纯 C# 决策层。

**理由（逐条对齐本项目的实际约束）**：
1. **规模不匹配**：同屏活跃实体 3 只，全场景 8 只。ECS 的收益来自 10^3~10^5 量级实体；8 个实体用 ECS 是纯粹的负收益。
2. **表现层集成成本极高**：`Animator`/`SkinnedMeshRenderer` 是 DOTS 的非托管侧不支持的对象（需要用 Hybrid Renderer / Entities Graphics 绕），而猫与鬼的表现**全靠 Animator 与骨骼二次写入**（`CatLookAt` 写 `head/neck` 骨骼、`CatBodyMotion` 写尾巴）。这层改写等于把 8,700 行运行时里最脆弱的部分重写。
3. **现有资产的迁移成本**：44 个 `MonoBehaviour`（其中 `CatAgent` 1627 行、`CatOpeningController` 635 行、`CatFeedingSequenceController` 518 行）全部要重写为 `System`/`IComponentData`；`CatNavGrid` 的 `List` 与引用类型要改成 `NativeArray`/`Blob`。
4. **单人开发的学习曲线**：本项目的开发者不是资深架构背景。ECS 的调试（`Entity Debugger`、Job 安全系统、`Burst` 编译错误）会吃掉"每天推进一点"的全部时间预算。
5. **版本摩擦**：Unity 2022.3 + URP 14 + Entities 1.x 的组合，官方支持路径远不如 Unity 6 顺畅。
6. **本作的核心不是"多"，是"怪"**：恐怖游戏的价值在每只鬼的**行为差异**与**可编排性**，而不是同质实体的吞吐量。面向数据的组合式 MonoBehaviour 恰好是"行为差异大"的最佳容器。

**后果**：
- ✅ 保留 Animator/骨骼/URP 的完整能力，恐怖表现可以做到位。
- ✅ `CatBrain`/`CatNavGrid` 脱离 `MonoBehaviour` 的可测性被保留（这是最宝贵的资产）。
- ⚠️ 若某天出现"数百个同屏小实体"（例如虫群/碎片），那部分**单独**用 `Jobs`+`ComputeShader` 做表现层特例，不做全局架构迁移。
- **重新考虑的触发条件（写死在这，避免以后反复纠结）**：同屏同质实体 > 500，且经 Profiler 证实主线程 `Update` 是瓶颈。

---

### ADR-003：组合式实体框架（Profile + 行为组件），拒绝继承树

**背景**：未来要加大量行为差异极大的鬼（追逐/瞬移/凝视/干扰物品/条件出现）。最自然的做法是 `class Ghost` 然后 `class Chaser : Ghost`、`class MirrorGhost : Ghost`……；也可以复制 `CatAgent` 改。

**决策**：`EntityProfile`（数据）+ `EntityRig`（装配）+ `EntityBehaviourBase`（行为组件）+ `UtilitySelector`（打分选行为）+ `EntityStateMachine`（纯 C# 迟滞机，从 `CatBrain` 提炼）。差异化 = 换组件 + 换参数。**唯一允许的继承**是继承 `EntityBehaviourBase`。

**理由**：
- 鬼之间的差异是**正交维度**（移动方式 × 出现条件 × 危险行为 × 逃逸方式），继承树是单维度表达，必然爆炸成 2^n 个类。
- 打分选择（`Weight(ctx)`）让"多个行为同时可行"变成自然表达。而现有 `CatBrain` 的"一次性动作抢占 + 距离分档"本质就是一个特化的打分器，提炼成 `EntityStateMachine` 是**同构升级**，不是新概念。
- 可测性：行为组件的 `Weight`/`Tick` 是纯函数（`in EntityContext`），能在 EditMode 里像 `CatBrainTests` 一样断言。

**后果**：
- ✅ 3 个鬼约 590 行新代码（对比复制猫：4,800 行）。
- ✅ 加第 4 个鬼是"写 1 个组件 + 建 1 个 Profile"，半天到一天。
- ⚠️ 组合的代价是"行为分散在多个组件，看代码不如看一个巨类直观"。对策：`EntityRig` 在 Inspector 里按顺序列出所有 `EntityBehaviourBase`，并提供一个 `DebugHud`（`#if UNITY_EDITOR`）实时显示当前选中的行为与权重——**可调试性必须由工具补回来**，这条写进 `DEVELOPMENT_GUIDE.md`。
- ⚠️ 不允许为了"复用"提前抽象：见"第三次法则"。

---

### ADR-004：共享 `NavService` + 分层网格，拒绝 `NavMeshAgent`

**背景**：猫已有一套自建 A*（`CatNavGrid`，2,550 格，每格射线+胶囊）。鬼也要走位。

**决策**：`CatNavGrid` → `Nav/NavGrid.cs`（保持纯 C#），由场景唯一的 `NavService` 烘焙 **2 层网格**（`Small` r=0.15 / `Large` r=0.40），所有实体通过请求队列查询。**不引入 NavMeshAgent。**

**理由**：见 §6。核心数字：每只鬼自带网格 = 2,550 次射线 + 2,550 次胶囊重叠的启动尖峰，且家具改动后 N 张网格一致性无法保证；`NavMeshAgent` 会与 `CatAgent` 的程序化位移争 Transform（这正是项目当初避开它的原因），且烘焙结果无法在 EditMode 单测。

**后果**：
- ✅ 启动烘焙成本从 O(N 鬼) 降到 O(1)（加 1 层约 +40%）。
- ✅ 网格可被 EditMode 单测（喂一个假 `Physics` 场景或注入占位数据）。
- ⚠️ 队列化会让路径有 0~2 帧延迟：对猫的"逃跑"要保留同步快速路径（`NavService.FindPathImmediate`，仅当 `WalkableCount` 已有缓存且请求数 < 2 时）。
- ⚠️ `Large` 层 0.40 半径可能让某些门洞不可过：必须给 `NavService` 一个"门洞例外表"（现有 `ICatDoor` + `minDoorWidth` 机制迁移过来），并在 Scene 视图画出不可走格。

---

### ADR-005：`GameState` 追加到 9 态 + `ControlLock` 取代临时禁用

**背景**：现状 4 态，且全项目只有 1 个消费者（实测）；同时存在 4 套并行控制权机制（实测）。

**决策**：枚举**只追加** `Sleeping/Cutscene/Paused/Ended/Loading`；新增 5 个派生谓词作为唯一查询入口；用引用计数的 `ControlLock` 取代 `FeedingInteractionLock`、`fpc.enabled = false`、`catAgent.enabled = false`。实体内部保留三级锁并提升为 `EntityControl`。

**后果**：
- ✅ 睡眠/暂停/结局有落脚点，且加系统时不必再发明"游戏在跑吗"的判断。
- ✅ 消除"还原失败就永久坏掉"的副作用式开关（`FeedingInteractionLock` 改 `interactDistance`）。
- ⚠️ `IsGameplayActive` 语义变化，必须在同一提交里把调用点改成明确的谓词（当前只有 1 处，成本最低的时刻）。
- ⚠️ 枚举数值被场景序列化，**永不重排**（`DEVELOPMENT_GUIDE.md` §1.6 立为铁律）。

---

### ADR-006：存档 = `ISaveable` + 纯 C# DTO + Newtonsoft + 迁移链

**背景**：未来必须支持存档（单槽→多槽）。

**决策**：见 §9。关键三条：拉取式、DTO 与 MonoBehaviour 解耦、坐标用 `SceneId` 映射（鬼是唯一例外）。

**后果**：
- ✅ 加一个可存档系统 = 实现 `ISaveable` + 加一个 DTO 字段 + 决定是否要迁移函数。半天内可完成。
- ✅ 多槽只是"文件路径多一层"，游戏代码零改动（`SaveService.Slot = "slot1"`）。
- ⚠️ 新增一个依赖包（Newtonsoft）；IL2CPP 构建需要确认 `link.xml`（Newtonsoft 用反射，AOT 下可能裁剪掉类型）。**首次出包时必须实测一次存档读回**，写进 `E06` 回归清单。
- ⚠️ 鬼存坐标是妥协：改布局后鬼可能出现在墙里。对策：读档时 `NavGrid.TrySnap` 一次，贴不回去就回到它的 spawn 锚点。

**备选**：`JsonUtility` ❌（无多态/无 `Dictionary`，鬼状态会逼出上帝 DTO）；二进制 ❌（单人项目调试成本过高）。

---

### ADR-007：受控事件，拒绝全局字符串事件总线

**背景**：系统会变多（时间/需求/鬼/恐怖导演/UI/音频/存档），需要解耦。

**决策**：只有三种通信方式（C# event / SO 通道 / 接口直连），判断标准见 §10.1。禁止字符串总线、`SendMessage`、跨系统 `UnityEvent`。

**理由（为什么字符串总线在这个项目里特别危险）**：
1. **编译期零检查**：`Publish("cat_hungery")` 拼错不报错，只在运行时静默不触发。恐怖游戏的 bug 本来就难复现（依赖时序与玩家行为），这类静默失败会让调试时间翻倍。
2. **无法回答"谁在监听"**：单人开发没有 IDE 的 Find Usages 能穿透字符串。重构时只能靠 `grep` 猜。
3. **与存档冲突**：`flags` 需要"已触发事件"的稳定集合；字符串一旦拼写不一致，读档后一次性事件会重播（镜子第二次吓人就不再是吓人）。
4. **性能不是问题，可读性才是**：本项目事件频率极低（每帧 0~2 次），性能从不是选型理由。

**后果**：
- ✅ 所有通信都能被 IDE 的 Find Usages 找到。
- ✅ SO 通道上限 6 个，天然抑制"什么都发事件"。
- ⚠️ 需要一点纪律：**事件订阅必须成对**（`DEVELOPMENT_GUIDE.md` §8 给了模板与泄漏自检方法）。

---

### ADR-008：`AudioService` 统一音频入口（含分组、池、`SfxId` 登记）

**背景**：15 个 `AudioSource` 各自 `PlayOneShot`，13 个 clip 序列化为空（靠代码赋值）。加"鬼的尖叫"时无处登记，做不了音量分组与静音。

**决策**：`Core/Audio/AudioService`（一个 `MonoBehaviour`，DontDestroyOnLoad）：
- 分组：`Master / Ambience / SFX / Voice / UI`（`AudioMixer` 5 个 group 或运行时线性缩放）。
- 3D 一次性音效走**池化 `AudioSource`**（池 24 个，超过则抢最远/最弱）。
- 所有音效有 `SfxId` 枚举，`AudioCatalog`（SO）做 `SfxId → AudioClip[]`（数组支持随机变体，`CatBell` 的 pitch jitter 逻辑搬进来）。
- 场景里已有的 15 个 `AudioSource` 逐个降级为"由 `AudioService` 借用"，`clip` 为空不再是问题（由 Catalog 提供）。

**后果**：
- ✅ 一次实现，猫叫/鬼叫/门响/脚步/UI 全部复用；音量设置菜单天然可做。
- ✅ `SfxId` 是枚举 → 拼错编译报错，比"在 Inspector 里找 clip"可靠。
- ⚠️ 现有 15 个 `AudioSource` 的 3D 衰减参数（`CatBell` 的 `minDistance=0.9/maxDistance=8`）必须逐个搬到 Catalog，**否则听感会变**。迁移时用"先并行播放、对比听感、再关掉旧路径"三小步。

---

### ADR-009：调试脚本移出运行时 + 自测作为每次重构的验收闸门

**背景**：`CatFeedingSelfTest`(609)/`CatWalkQA`(275)/`CatOpeningQA`(215) 共 1,099 行编进运行时程序集，靠 `.workbuddy/selftest.on` 标记短路。同时它们是**唯一可靠的端到端验证手段**（Unity MCP 在 Play 模式会掉线）。

**决策**：
1. 三个脚本用 `#if UNITY_EDITOR` 包裹，移入 `Assets/Game/Editor/SelfTest/`（`NOCTURNE.Editor`，`includePlatforms: ["Editor"]`）。**`.workbuddy/selftest.on` 机制与日志格式完全不变**，保证现有工作流不断。
2. 把"跑一次自测 → 断言 `DONE 全流程结束` 且 `WARN 0 / ERR 0`"定为**每次重构类提交的强制验收**（`BACKLOG` 里每项任务的验收标准都引用这一条）。
3. 自测扩展规范写进 `DEVELOPMENT_GUIDE.md` §15（加步骤的模板、断言怎么写、失败怎么判）。

**后果**：
- ✅ 包体不含调试代码，编译更快。
- ✅ 重构敢做——因为有安全网。
- ⚠️ 自测只能在编辑器里跑；**出包版的冒烟测试**必须另建（`E06`：主菜单 → 开场 → 喂食 → 睡觉 → 存读档）。
- ⚠️ 自测本身会成为"最难维护的代码"（609 行里大量 `Reflection` 与字符串）。规则：**自测里允许用反射访问私有字段**（它是测试），但生产代码里用反射一律打回。

---

## 13. 演进路线与出口闸门

| Phase | 目标 | 出口闸门（不满足不进下一阶段） |
|---|---|---|
| **A 基础骨架与债务** | 分层成立、状态机 9 态、音频统一、`CatAgent` 拆分完成 | ① `Assembly-CSharp` 为空；② `CatFeedingSelfTest` 全绿；③ 死代码与禁用相机清零；④ 稳定性：连续 3 次完整跑通自测无随机失败 |
| **B 生存循环** | 时间、疲劳/理智、上床睡觉、次日 | ① 能"玩到第二天"；② `Time.timeScale=0` 暂停不积累时间；③ HUD 三项数值正确；④ 自测新增 `S` 步骤覆盖睡眠 |
| **C 鬼与恐怖** | `Entities` 框架 + 3 个鬼 + 恐怖导演 | ① 3 只鬼可由配置表开关；② 同屏 3 鬼稳定 60 FPS（Profiler 无 >20ms 帧）；③ 猫预警联动可复现；④ 鬼的状态可被存档系统读取（接口已就位） |
| **D 存档与终局** | 存读档闭环、结局 | ① 存档-退出-读档后状态与存档前逐项一致（对照表）；② 版本迁移至少有 1 次真实演练（改字段→老档能读）；③ 至少 1 个结局可达成并写入 `Ended` |
| **E 打磨** | 菜单、暂停、音频、性能、出包 | ① 出包版冒烟测试通过；② Newtonsoft + IL2CPP 存档实测通过；③ 包体与帧率达标 |

---

## 14. 已知风险与知识缺口（诚实清单）

| # | 风险/缺口 | 影响 | 处置 |
|---|---|---|---|
| 1 | Unity 2022.3 **没有 `Awaitable`**（Unity 6 才有），`UniTask` 未安装 | 异步只能靠协程 | 全项目用协程 + 显式超时；**不装 UniTask**（新增依赖与 IL2CPP 风险，收益不足）。见 `DEVELOPMENT_GUIDE.md` §11 |
| 2 | `JsonUtility` 无多态/无 `Dictionary` | 存档 DTO 设计受限 | ADR-006 选 Newtonsoft |
| 3 | Newtonsoft + IL2CPP 的裁剪 | 出包后存档可能抛 `MissingMethod` | `E06` 必须实测；准备 `link.xml` |
| 4 | Unity MCP 在 Play 模式掉线（已知现象，`CatFeedingSelfTest` 注释记录） | 无法从外部逐帧驱动 | 自测"游戏内自驱动 + 写日志"模式保留，不依赖 MCP |
| 5 | 猫的 Animator 剪辑绑定不能动（`Cat_Run` 资源保留但禁用） | 改 controller 会断 `fileID` | 鬼**不复用 `AC_Cat.controller`**，各自独立 controller |
| 6 | 工作区当前有**70 项未提交的删除**（`git status` 实测：`Assets/Art/Environment` 50、`Assets/Art/Cat` 16、`Assets/Art/material` 2、`Assets/fonts/STXINWEI SDF.asset` 2） | 下一次提交会把它们混进来，"一功能一提交"当场破功；若其中某个仍被引用，会导致资源丢失 | **`A01` 第一件事**：确认是审计时的资源清理 → 单独提交（`chore(art): 提交资源清理结果`）。已抽查：被删的 `STXINWEI SDF.asset`（GUID `399847463615f5844a19a9cd4aef6d0b`）在 `SampleScene` 与 `CAT_Nocturne.prefab` 中引用数均为 **0**（已由 `STXINWEI SDF Dynamic.asset` 替代），安全 |
| 7 | 单场景 360 对象，`SceneId` 需要人工/工具生成 | 存档 ID 漏标会导致"读档后某物状态丢失" | `SceneIdValidator` 在保存/进 Play 前强制校验，缺 ID 直接报错 |
| 8 | 鬼的数量级（3 活跃 / 8 定义）是**假设** | 若用户要 10 只同屏，需提前上 `SensesBatch` | 见 §11.2，先按 3 做，保留扩展位 |
| 9 | 本文未实测性能数据（只有静态分析） | 预算数字是"经验值" | `A23` 先测基线并写入 `docs/perf/baseline.md`，之后所有优化对照它 |
| 10 | **审计报告存在至少 1 处错误结论**（P3-2 把 `CatDoorway` 判为死代码，实为在用） | 照报告执行会断编译 | 所有"删除类"任务必须先复核引用（`A02` 的验收标准里明确写了）；本文 §3.1 已勘误 |

---

## 15. 附：本架构新增/改动类型总表（供 BACKLOG 对照）

| 类型 | 层 | 新增/改造 | 主要任务编号 |
|---|---|---|---|
| `GameState`（枚举） | Core | 改造：4→9 态追加 | A11 |
| `GameStateManager` | Core | 改造：加谓词、控 `ControlLock` | A11/A12 |
| `ControlLock` | Core | 新增 | A10 |
| `GameClock` / `IClock` | Core | 新增 | B01 |
| `AudioService` / `SfxId` / `AudioCatalog` | Core | 新增 | A13/A14 |
| `SaveService` / `ISaveable` / `SaveFile` / `SaveMigrator` / `SceneId` | Core | 新增 | D01~D05 |
| `IStatsSource` | Core | 新增（接口在 Core，实现在 Player） | B03 |
| `GameEvent<T>` / `HorrorSignalEvent` | Core | 新增 | C05 |
| `Log` | Core | 新增 | A07 |
| `NavGrid`（原 `CatNavGrid`） | Nav | 平移改名 + 参数化 | A09 |
| `NavService` / `PathRequest` | Nav | 新增 | C02 |
| `IFoodContainer` / `IInterferable` | Interaction | 新增 | A20 / C10 |
| `EntityProfile` / `EntityRig` / `EntityControl` | Entities | 新增 | C01 |
| `EntitySenses` / `EntityMotor` / `EntityAnimationDriver` | Entities | 新增 | C01 |
| `EntityStateMachine`（纯 C#） | Entities | 新增（提炼自 `CatBrain`） | C01 |
| `EntityBehaviourBase` / `UtilitySelector` | Entities | 新增 | C01 |
| `EntityRegistry` | Entities | 新增 | C01 |
| `CatMotor` / `CatSensesSensor` / `CatAnimatorDriver` / `CatHunger` / `CatDoorPassage` | Cat | 新增（从 `CatAgent` 抽出） | A15~A19 |
| `CatProfile` (SO) | Cat | 新增 | A19 |
| `GhostRig` / 4 个 Behaviour | Ghosts | 新增 | C03/C09/C10 |
| `HorrorDirector` / `LightFlicker` / `MirrorAnomaly` | World | 新增 | C04/C06/C07 |
| `PlayerStats` / `BedInteractable` | Player | 新增 | B03/B05 |
| `PlayerInteractor` | Player | 新增（从 FPC 拆） | A10 |
| `HudUI` / `PauseMenuUI` / `MainMenuUI` / `EndingUI` / `CrosshairUI` | UI | 新增（替代 `OnGUI`） | B04/C14/E01/E02 |
| `AssetPathValidator` / `SceneIdValidator` | Editor | 新增 | A21/D04 |
