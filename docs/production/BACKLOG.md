# 《夜隙 NOCTURNE》开发任务清单（BACKLOG v1.0）

> 配套：`docs/architecture/ARCHITECTURE.md`（怎么设计）、`docs/architecture/DEVELOPMENT_GUIDE.md`（怎么写代码）
> 粒度：**一项任务 = 一次提交**。`0.5` = 半天，`1` = 一天（按有效编码时间算，不含环境折腾）。
> 排序：严格按依赖拓扑。**不许跳序做**，除非明确标了"可并行"。

---

## 0. 怎么用这份清单

**每天开工**：
1. `git status` + `git log -3`，确认工作区干净、上一次提交是你以为的那次。
2. 打开本表，找到第一个未打勾的任务，读它的"验收"。
3. 只做这一项。做完 → 跑自测 → 提交 → 打勾 → 收工。

**每次提交前必跑**（详见 `DEVELOPMENT_GUIDE.md` §14）：编译器 0 错误 0 警告 → `CatFeedingSelfTest` 全绿 → `git status` 只含本任务的文件。

**三种标记**：
- 🔒 **闸门任务**：它的验收同时是阶段出口条件，不过不许进下一阶段。
- ⚠️ **高风险任务**：会动到场景/Prefab 引用，必须连 `.meta` 一起处理，做完立刻 Play 验证 24 个挂载点没 Missing。
- 🔁 **重构任务**：只改内部实现，对外行为必须完全一致，唯一验收标准 = 自测 24 步全绿且日志 `WARN 0 / ERR 0`。

---

## 阶段总览

| Phase | 主题 | 任务数 | 天 | 出口闸门 |
|---|---|---|---|---|
| **A** 基础骨架与债务 | 分层成立、状态机 9 态、音频统一、喂食解耦、自测闸门 | 20 | **15.5** | `Assembly-CSharp` 为空 + 自测全绿 |
| **B** 生存循环 | 时间、疲劳/理智、上床睡觉、次日 | 12 | **11.0** | 能玩到第二天、暂停不积累时间 |
| **C** 鬼与恐怖 | Entities 框架、`CatAgent` 拆分、3 个鬼、恐怖导演 | 16 | **18.0** | 同屏 3 鬼 60 FPS、配置可开关 |
| **D** 存档与终局 | `ISaveable`、DTO、迁移、存读档、结局 | 11 | **10.5** | 存档-读档逐项一致、老档可迁移 |
| **E** 打磨 | 菜单、混音、uGUI、出包 | 9 | **8.0** | 出包版冒烟通过、IL2CPP 存档实测 |
| | **合计** | **68** | **63** | |

> 63 天是**有效编码日**。每天投入半天 ≈ 4.5 个月；全天投入 ≈ 2.5 个月。
> **最小可玩路线**：A → B（约 26.5 天）即可得到"开场→喂食→睡觉→次日喂食"的完整循环，可对外演示。

### 关于任务顺序的一处刻意调整（与审计报告路线图不同）

审计报告把"扩展 `GameState`、`TimeSystem`、疲劳/理智"排在前面，把 `CatAgent` 拆分放在更晚。本文做了两处调整，理由如下：

1. **`CatAgent` 拆分从 Phase A 挪到 Phase C**（`C04`~`C06`）。理由：拆出来的 `CatMotor`/`CatSensesSensor`/`CatAnimatorDriver` 与 `Entities` 框架里的 `EntityMotor`/`EntitySenses`/`EntityAnimationDriver` 是同一批东西。先在 A 拆一遍、C 再改成继承框架 = 白干两遍。**先建框架，再拆猫**，一次到位。
2. **分层与 `ControlLock` 提到 Phase A 最前**（`A07`~`A13`）。理由：`GameStateManager` 现在只有 1 个消费者（实测），扩状态的代价是历史最低点；一旦 B 阶段的 6 个系统都开始查 `IsGameplayActive`，再改就要动 6 个文件 6 次。

---

## Phase A — 基础骨架与债务（15.5 天 / 20 项）

| 编号 | 任务 | 天 | 前置 | 涉及文件 | 验收（可测） | commit |
|---|---|:--:|---|---|---|---|
| **A01** | 工作区清理与基线提交 | 0.5 | — | `.gitignore`、**70 项未提交删除**（`Assets/Art/Environment` 50、`Assets/Art/Cat` 16、`Assets/Art/material` 2、`Assets/fonts/STXINWEI SDF.asset` 2） | ① 抽查确认删除是有意的（已验：被删字体的 GUID 在 `SampleScene`/`CAT_Nocturne.prefab` 引用数均为 0）；② `.gitignore` 含 `.workbuddy/`、`Temp/`、`Logs/`、`Cacheuv/`、`unity_v1*/`、`generated-images/`；③ `Development_Progress_Report.md` 一并入库（它是现状基线）；④ `git status` 干净；⑤ 打开工程 0 编译错误、Play 后 UI 文字正常显示（验证字体没删错） | `chore(repo): 提交资源清理结果并补齐 .gitignore` |
| **A02** | 删死代码 `PlanarMirror`（**必须先复核**） | 0.5 | A01 | `Assets/Art/Environment/Bedroom/Scripts/PlanarMirror.cs` | ① 删前 `grep PlanarMirror` 结果只剩注释；② 删除后 0 编译错误；③ Play 一次场景，`PR_BodyMirror_Glass` 正常 | `chore(world): 删除死代码 PlanarMirror` |
| **A03** | 删 3 个禁用相机 | 0.5 | A01 | `SampleScene`（`Main Camera`/`CheckCam`/`PreviewCam`） | ① Hierarchy 里 Camera 只剩 `PlayerCam`；② `Camera.main` 在运行时非空；③ 自测全绿 | `chore(scene): 删除 3 个调试遗留相机` |
| **A04** | 重复命名抽屉改名 | 0.5 | A01 | `SampleScene`（`Drawer_Top`×2/`Drawer_Bottom`×2） | ① 改名 `Drawer_Desk_Top/Bottom`、`Drawer_Nightstand_Top/Bottom`；② 5 个 `DrawerInteractable` 的 Inspector 引用全部非空；③ 逐个按 E 能开 | `chore(scene): 消除抽屉重名` |
| **A05** | 调试脚本移出运行时 | 0.5 | A01 | `CatFeedingSelfTest.cs`(609)、`CatWalkQA.cs`(275)、`CatOpeningQA.cs`(215) | ① 三文件整体 `#if UNITY_EDITOR` 包裹；② `.workbuddy/selftest.on` 机制不变、日志格式不变、自测仍全绿；③ 输出程序的 `Assembly-CSharp.dll` 不含这三个类型（用 `ildasm` 或构建日志确认） | `chore(tests): 把自测与 QA 脚本移出运行时程序集` |
| **A06** | 三份文档入库 | 0.5 | — | `docs/architecture/ARCHITECTURE.md`、`docs/production/BACKLOG.md`、`docs/architecture/DEVELOPMENT_GUIDE.md` | ① 三文件存在；② 独立提交（不与代码混）；③ commit message 见右 | `docs: 建立技术架构、任务清单与开发规范` |
| **A07** 🔒 | 建 `Core` 程序集 + 日志 + 目录 | 0.5 | A06 | `Assets/Game/Core/NOCTURNE.Core.asmdef`、`Core/Diag/Log.cs` | ① asmdef 存在且 `references: []`、`autoReferenced: true`；② `Log.Info/Warn/Error` 带 `[域]` 前缀；③ 全工程 0 编译错误（空程序集不影响现有代码） | `feat(core): 建立 Core 程序集与统一日志入口` |
| **A08** 🔒 | `GameState` 扩到 9 态 + 唯一查询入口 | 1 | A07 | `GameStateManager.cs`（迁到 `Core/Flow/`）、`GameState.cs` | ① 枚举数值 0~8，前 4 个**数值不变**；② 5 个派生谓词（`IsPlayerControlled`/`IsSimulationRunning`/`IsTimeAdvancing`/`IsEntityAiRunning`/`IsUiModal`）存在且有 XML 注释；③ 全工程 `IsGameplayActive` 调用点（当前 0 处真实调用）改为明确谓词；④ 场景里 `OpeningSystems/GameStateManager` 的 `_state` 仍是 0（未丢引用） | `refactor(core): GameState 扩至 9 态并收敛查询入口` |
| **A09** 🔒 | `ControlLock` + 替换三套旧机制 | 1 | A08 | `Core/Flow/ControlLock.cs`、`CatFoodBagInteractable.cs:439`、`PlayerSittingController.cs:117`、`FirstPersonController.cs`、`PlayerLyingController.cs` | ① `ControlLock.Acquire/Release/Blocks` 引用计数（同 reason 重复 Acquire 不重复计数）；② `FeedingInteractionLock` 类删除；③ 坐椅子期间 `WasdLocked==true` 且离开后恢复；④ 倒粮期间不能交互、松手后能交互；⑤ 自测全绿 | `refactor(core): 用 ControlLock 统一控制权，移除 FeedingInteractionLock` |
| **A10** | 从 `FirstPersonController` 拆出 `PlayerInteractor` | 0.5 | A09 | `Player/PlayerInteractor.cs`（新）、`FirstPersonController.cs` | ① 射线/`CurrentTarget`/提示/交互键全搬走；② `FPC.CurrentTarget` 保留为转发属性（外部零改动）；③ 12 个可交互物逐个验证可用；④ 自测 G 段（拿袋）通过 | `refactor(player): 拆出 PlayerInteractor` |
| **A11** ⚠️🔒 | `Interaction` 程序集 + 12 实现者迁移 | 1 | A10 | `Assets/Game/Interaction/`（asmdef + `IInteractable.cs` + `IInteractPrompt.cs`）、12 个实现者 | ① asmdef `references: [NOCTURNE.Core]`；② 两个接口文件**连 .meta 一起 `git mv`**；③ 12 个实现者加 `using NOCTURNE.Interaction;`；④ Play 后 12 个物体 Inspector 引用无 Missing；⑤ 自测全绿 —— **本任务必须在单次提交内完成** | `refactor(interaction): 建立 Interaction 程序集并迁移 12 个实现者` |
| **A12** ⚠️ | 搬家：`Assets/Art/.../Scripts` → `Player/` 与 `World/Props/` | 1 | A11 | 9 个 `.cs` + `.meta`；`NOCTURNE.Player.asmdef`、`NOCTURNE.World.asmdef` | ① 每个文件用 `git mv` 连 `.meta` 一起移；② `Assets/Art/Environment/Bedroom/Scripts/` 目录为空后删除；③ Play 后场景 24 个挂载点 0 Missing；④ 自测全绿 | `refactor: 把脚本从 Assets/Art 迁到对应程序集目录` |
| **A13** 🔒 | `Nav` 程序集：`CatNavGrid` → `NavGrid` | 1 | A12 | `Assets/Game/Nav/NavGrid.cs`（由 `CatNavGrid.cs` 改名）、`NOCTURNE.Nav.asmdef`、`CatAgent.cs` 引用处 | ① `NavGrid` 仍**不是** `MonoBehaviour`、不碰 `GameObject`/`Find*`（只用 `Vector3`/`Bounds`/`Physics`），保持可 `new` 与可单测；② `radius/height/cell` 保持参数化；③ `CatAgent.BuildNavigation` 调用照旧；④ 猫能正常走位（自测 E 段 walk 段通过）；⑤ `CatBrainTests` 仍编译通过 | `refactor(nav): CatNavGrid 泛化为 NavGrid 并独立程序集` |
| **A14** | `AudioService` + 分组 + 池 | 1 | A07 | `Core/Audio/AudioService.cs`、`Core/Audio/SoundGroup.cs` | ① `Play(SfxId, position?)` / `PlayAt(Transform, SfxId)` 两个入口；② 5 个分组音量可读可写，`Master=0` 时全静音；③ 池 24 个 `AudioSource`，超出抢最远；④ 在 `GameRoot` 上常驻（`DontDestroyOnLoad`） | `feat(audio): 建立 AudioService（分组 + 池化）` |
| **A15** | `AudioCatalog` + `SfxId` + 13 个空 clip 登记 | 1 | A14 | `Core/Audio/SfxId.cs`、`Core/Audio/AudioCatalog.cs`(SO)、`AudioCatalog.asset` | ① `SfxId` 覆盖现有全部 11 个音频文件 + UI/鬼预留位；② `AudioCatalog` 里每个 `SfxId` 至少 1 个 clip，缺失的显示黄色警告；③ 现有 15 个 `AudioSource` 至少 5 个改走 `AudioService`（先做猫叫/猫粮/门/抽屉/脚步）；④ 声音听感不变（逐个对比） | `feat(audio): 建立 SfxId 与 AudioCatalog，消灭空 clip` |
| **A16** 🔁 | 喂食解耦：`IFoodContainer` + `PourCommitted` | 1 | A11 | `Interaction/IFoodContainer.cs`（新）、`CatFoodBowl.cs`、`CatFoodBagInteractable.cs` | ① 袋不再持有 `CatFoodBowl` 具体类型（只持 `IFoodContainer`）；② 碗不再反向引用袋；③ 倒粮完成发 `event Action<IFoodContainer,float> PourCommitted`；④ 自测 H 段（倒粮）与 I 段（吃）全绿 | `refactor(feeding): 用 IFoodContainer 解开猫粮袋与碗的双向依赖` |
| **A17** | Editor 硬编码路径收敛 | 1 | A12 | `Editor/NOCTURNE.Paths.cs`（新）、`CatLooksBuilder.cs`(18 处)、`CatFeedingPropsBuilder.cs`、`Editor/AssetPathValidator.cs`（新） | ① 18 处字面量全部改为 `Paths.*` 常量；② 新建 `AssetPathValidator`：菜单 `NOCTURNE/校验资源路径`，缺文件时红色报错并列出**完整期望路径**；③ 跑一次校验 0 报错 | `refactor(editor): 资源路径常量化并加校验器` |
| **A18** 🔒 | 自测闸门脚本化 | 1 | A05 | `Editor/SelfTest/CatFeedingSelfTest.cs`、`docs/testing/SELFTEST.md`（新） | ① 自测日志末尾打印 `RESULT: PASS` 或 `RESULT: FAIL(<原因>)`；② 写一个 `docs/testing/SELFTEST.md`：怎么开启标记文件、看哪几个字段、什么算通过；③ 连续跑 3 次，3 次都 PASS（排除随机失败） | `test(selftest): 自测输出结构化结果并补文档` |
| **A19** | 性能基线测量 | 0.5 | A18 | `docs/perf/baseline.md`（新） | ① 记录：开场/探索/喂食/猫睡觉 4 个场景的 `FPS`、主线程 ms、`GC Alloc`/帧、活动 `Animator` 数、`Update` 数；② 记录机器配置与分辨率；③ 之后每阶段末尾对照一次 | `docs(perf): 记录性能基线` |
| **A20** 🔒 | 收尾：`Assembly-CSharp` 清空 | 1 | A12/A13/A16/A17 | 全部 asmdef 的 `autoReferenced` | ① `Assets/` 下除 `TextMesh Pro`/`TutorialInfo` 外无 `.cs` 直接落在默认程序集；② 所有 asmdef `autoReferenced: false`（`Core` 可留 true）；③ 编译 0 错误；④ 自测全绿；⑤ 依赖非法时能复现"加引用才通过"的报错（自查 §5.2 矩阵） | `refactor: 完成程序集分区，清空 Assembly-CSharp` |

**Phase A 出口闸门**：A07/A08/A09/A11/A13/A18/A20 全部通过 + 自测连续 3 次全绿 + `docs/perf/baseline.md` 存在。

---

## Phase B — 生存循环（11 天 / 12 项）

| 编号 | 任务 | 天 | 前置 | 涉及文件 | 验收（可测） | commit |
|---|---|:--:|---|---|---|---|
| **B01** 🔒 | `GameClock`（游戏内时钟） | 1 | A08 | `Core/Time/GameClock.cs`、`Core/Time/IClock.cs`、`GameRoot` 挂载 | ① 1 游戏分 = 1 实时秒（可配 `minutesPerRealSecond`）；② `Day/Hour/Minute` 可读；③ `IsSimulationRunning==false` 时**不推进**（`Opening`/`Paused`/`Ended` 下停表）；④ `Sleeping` 下按 `sleepTimeScale` 加速；⑤ EditMode 单测：给定 dt 序列断言时间推进正确 | `feat(core): 加入 GameClock 游戏内时钟` |
| **B02** | 时间驱动光照与天空 | 1 | B01 | `World/TimeOfDayLighting.cs`（新）、7 盏灯、`Sky_Night.mat`、`Global Volume` | ① 6:00~18:00 为昼、其余为夜（`IClock.IsNight`）；② 窗光/环境光随小时数插值；③ 手电只在夜里默认开；④ 拨到 2:00 和 12:00 截图对比明显不同 | `feat(world): 昼夜光照随时间变化` |
| **B03** 🔒 | `PlayerStats`（疲劳/理智）+ `IStatsSource` | 1.5 | B01 | `Player/PlayerStats.cs`、`Core/Contracts/IStatsSource.cs`、`Core/Contracts/ServiceHub.cs` | ① 疲劳随时间上升、奔跑加成、床上恢复；② 理智在夜间/黑暗中下降、白天缓慢回升；③ 接口定义在 `Core`，实现在 `Player`；④ EditMode 单测：`StatModel`（纯 C# 部分）能断言曲线；⑤ 数值不出现硬编码（全 `[SerializeField]`） | `feat(player): 加入疲劳与理智数值` |
| **B04** | 状态门控接入 | 0.5 | B01/B03 | `CatAgent.cs`（`Update` 首行）、`PlayerStats.cs`、`GameClock.cs` | ① `Paused` 时三者一律不 tick；② `Opening` 时不 tick；③ 在 `Update` 里插 `if (!GameStateManager.Instance.IsSimulationRunning) return;` 的位置统一；④ 自测全绿（开场期间猫仍不动） | `refactor: 所有推进系统统一由 IsSimulationRunning 门控` |
| **B05** | HUD（时间/疲劳/理智） | 1 | B03 | `UI/HudUI.cs`、`UI/HudCanvas.prefab` | ① 三项数值实时显示，风格与 `OpeningCanvas` 一致；② 只在 `Playing`/`Sleeping` 显示；③ 数值变化用 2Hz 节流刷新（不每帧）；④ 1080p 下不遮挡准星 | `feat(ui): 加入 HUD` |
| **B06** | 上床交互 `BedInteractable` | 1 | B05 | `Player/BedInteractable.cs`（新）、`PlayerLyingController.cs`、`SampleScene`（床） | ① 夜间靠近床显示 `[E] 睡觉`；② 白天靠近显示"还不困"或不可交互；③ 躺下复用 `PlayerLyingController` 的镜头线性插值；④ 躺下后 `GameState==Sleeping`；⑤ 走廊里按 E 无反应 | `feat(player): 上床睡觉交互` |
| **B07** 🔒 | 睡眠流程：推进时间 + 数值恢复 | 1 | B06 | `Player/SleepController.cs`（新）、`GameClock`、`PlayerStats` | ① 睡眠 6~8 小时推进时间（受 `sleepTimeScale` 控制）；② 疲劳清零、理智回填；③ 起床后 `Playing`；④ 睡眠中 `IsPlayerControlled==false`；⑤ 自测新增 S 段通过 | `feat(player): 睡眠推进时间与数值恢复` |
| **B08** | 睡眠质量与熬夜惩罚 | 0.5 | B07 | `Player/SleepController.cs`、`PlayerStats.cs` | ① 22:00 前睡 / 2:00 后睡，次日初始疲劳不同（可配表）；② 熬夜降低理智上限（当天下限）；③ 数值可通过 Inspector 调配 | `feat(player): 睡眠质量与熬夜惩罚` |
| **B09** | 猫需求唤醒玩家 | 1 | B07 | `CatAgent.cs`（饥饿部分）、`Player/SleepController.cs`、`AudioService` | ① 睡眠中猫饥饿 > 阈值 → 猫走到床边叫 → 玩家被"叫醒"（提前结束睡眠）；② 唤醒不推进到次日；③ 叫醒有音频与提示；④ 自测可脚本触发 | `feat(cat): 猫饿了会唤醒玩家` |
| **B10** | 修复 P2-1（猫不跟随） | 0.5 | — | `CatFeedingSequenceController.cs`（`WaitingForFood` 分支） | ① `WaitingForFood` 阶段猫保持 2~3m 跟随；② 自测 D 段断言改为"玩家到客厅后猫移动 > 0.5m"并通过；③ 不影响 E 段（跑到饭盆） | `fix(feeding): 等待喂食阶段猫跟随玩家` |
| **B11** | 自测新增 S 段（睡眠→次日） | 1 | B07/B09 | `Editor/SelfTest/CatFeedingSelfTest.cs` | ① 新增步骤：触发睡觉 → 断言时间推进 ≥ 6h → 断言疲劳 ≈ 0 → 断言回到 `Playing` → 断言可再次喂猫；② 日志行格式与既有 24 步一致；③ `RESULT: PASS` | `test(selftest): 覆盖睡眠与次日循环` |
| **B12** | 暂停 | 1 | B05 | `Core/Flow/PauseController.cs`、`UI/PauseMenuUI.cs`、`GameStateManager` | ① Esc 进 `Paused` + `Time.timeScale=0`；② 暂停时时钟/需求/鬼全停（B04 已保证）；③ 可继续/退出到主菜单（主菜单未做则先只做"继续"）；④ 暂停时鼠标解锁 | `feat(ui): 暂停菜单` |

**Phase B 出口闸门**：B01/B03/B07 通过 + 能连续玩"开场→喂食→睡觉→次日再喂" + 暂停 10 秒后游戏时间不前进 + 自测 `RESULT: PASS`。

---

## Phase C — 鬼与恐怖（18 天 / 16 项）

| 编号 | 任务 | 天 | 前置 | 涉及文件 | 验收（可测） | commit |
|---|---|:--:|---|---|---|---|---|
| **C01** 🔒 | `Entities` 框架骨架 | 2 | A20 | `Entities/`（`NOCTURNE.Entities.asmdef`、`EntityProfile.cs`、`EntityRig.cs`、`EntityControl.cs`、`EntityContext.cs`、`EntityStateMachine.cs`、`EntityBehaviourBase.cs`、`UtilitySelector.cs`、`EntityRegistry.cs`）+ `Tests/EditMode/EntityStateMachineTests.cs` | ① `EntityStateMachine` 纯 C#，有迟滞/一次性锁/优先级抢占，EditMode 单测 ≥ 8 条全绿；② `EntityControl` 三级锁（AI/Transform/Pose）语义与 `CatAgent` 现有三级锁一致；③ `EntityProfile` 是 SO 且含 `entityKind`；④ `EntityRegistry` 可按 id 查 Rig；⑤ 不改任何现有运行时代码 | `feat(entities): 建立组合式实体框架骨架` |
| **C02** | `EntitySenses` / `EntityMotor` / `EntityAnimationDriver` | 1.5 | C01 | `Entities/EntitySenses.cs`、`EntityMotor.cs`、`EntityAnimationDriver.cs` | ① `EntitySenses` 按 `senseInterval` 采样产出 `EntityContext`（8Hz）；② `EntityMotor` 是唯一写 Transform 者，支持"走路径/直线/瞬移/停"；③ `EntityAnimationDriver` 是唯一写 Animator 者，处理 `crossFade` 与一次性动作；④ 各自有 `#if UNITY_EDITOR` 的 `DebugHud` 显示当前状态 | `feat(entities): 加入感知、位移与动画驱动` |
| **C03** 🔒 | `NavService`：共享两层网格 + 请求队列 | 1.5 | A13/C01 | `Nav/NavService.cs`、`Nav/PathRequest.cs`、`Nav/NavGrid.cs` | ① 场景唯一烘焙者，`Small`(r=0.15)/`Large`(r=0.40) 两层共享一次遍历；② 每帧最多 2 条 A*，超出的排队；③ `FindPathImmediate` 供猫逃跑用；④ 烘焙耗时打印到 Console；⑤ 场景视图画出不可走格（Gizmo） | `feat(nav): NavService 共享网格与请求队列` |
| **C04** 🔁⚠️ | `CatAgent` 拆分 1：`CatSensesSensor` + `CatMotor` | 1 | C02/C03 | `Cat/CatSensesSensor.cs`、`Cat/CatMotor.cs`、`CatAgent.cs` | ① `CatAgent` 行数从 1627 → ≤ 1100；② 对外 API 一个都不改（见 ARCHITECTURE §7.2 清单）；③ 自测 24 步全绿；④ 猫走路/逃跑/追门无肉眼变化；⑤ 对比 A19 基线，`GC Alloc` 不增加 | `refactor(cat): 抽出感知与位移组件` |
| **C05** 🔁⚠️ | `CatAgent` 拆分 2：`CatAnimatorDriver` + `CatHunger` + `CatDoorPassage` | 1 | C04 | `Cat/CatAnimatorDriver.cs`、`CatHunger.cs`、`CatDoorPassage.cs`（`CatDoorway` 改名并入）、`CatAgent.cs` | ① `CatAgent` ≤ 600 行；② `CatDoorway.cs` 内容迁入 `CatDoorPassage`（**不是删除**）；③ 关门阻挡/开门等待行为不变；④ 饥饿推进与原来一致（同参数下 2 分钟饿一次）；⑤ 自测全绿 | `refactor(cat): 抽出动画、饥饿与门通行组件` |
| **C06** 🔁⚠️ | `CatAgent` 拆分 3：`CatProfile` SO + 门面收尾 | 1 | C05 | `Cat/CatProfile.cs`(SO)、`CatProfile.asset`、`CatAgent.cs`、`CatBrain.cs` | ① `CatAgent` ≤ 300 行，只剩装配/转发；② 40+ 个 `SerializeField` 的**默认值与迁移前逐一相等**（写个对比表格自查）；③ `CatBrain` 改为 `EntityStateMachine` 的配置实例（或保留但标注"新代码不要模仿"）；④ **猫的行为必须与迁移前一致**：自测 24 步 + 3 次连续跑全绿 | `refactor(cat): CatAgent 收敛为门面，参数迁入 CatProfile` |
| **C07** 🔒 | 第一个鬼：凝视者 | 1.5 | C02/C03 | `Ghosts/GhostRig.cs`、`Ghosts/Behaviour/GazeBehaviour.cs`、`Ghosts/Behaviour/ProximityTriggerBehaviour.cs`、`Prefab GHOST_Gazer` | ① 玩家看镜子 → 鬼出现在镜中 → 玩家转头 → 消失；② 鬼有独立 `Animator`（不复用 `AC_Cat`）；③ 全程跑 `EntitySenses`/`EntityMotor`（不写自定义位移）；④ 同屏 1 鬼，帧时间不超基线 +1ms | `feat(ghosts): 第一个鬼——凝视者` |
| **C08** 🔒 | 恐怖导演 + `HorrorSignalEvent` 通道 | 1 | C07 | `World/Horror/HorrorDirector.cs`、`Core/Events/GameEvent.cs`、`Core/Events/HorrorSignalEvent.cs`、`Core/Events/HorrorSignal.cs` | ① `HorrorDirector` 按"时间 + 理智 + 冷却"权重选事件，不认识任何具体执行者；② `HorrorSignalEvent` 通道 `Raise` 后订阅者响应；③ 有 `HorrorConfig` 配置表（间隔范围/强度/开关）；④ 全通道总数 ≤ 6（ARCHITECTURE §10.3 硬约束） | `feat(world): 恐怖事件导演与信号通道` |
| **C09** | 环境异常：灯光 + 镜子 | 1 | C08 | `World/Horror/LightFlicker.cs`、`World/Horror/MirrorAnomaly.cs`、`MirrorPicture.cs`（复用 `SetPicture`） | ① 灯闪 3 种模式（闪断/渐暗/单灯灭）可配；② 镜面画面可被换成 `alternativePictures` 里任一；③ 两种异常都能被 `HorrorDirector` 单独触发（编辑器按钮） | `feat(world): 灯光与镜子异常` |
| **C10** | 门/抽屉异常 + `IInterferable` | 1 | C08 | `Interaction/IInterferable.cs`、`World/Props/HingedDoor.cs`、`SlidingDoor.cs`、`DrawerInteractable.cs` | ① 三类道具实现 `IInterferable`（`CanInterfere/MarkInterfered`）；② 鬼能让最近的抽屉自己开一条缝、让门自己关上；③ 玩家交互后状态一致（不会卡在两个状态） | `feat(world): 门与抽屉的鬼干预接口` |
| **C11** 🔒 | 第二个鬼：尾随者 | 1.5 | C10 | `Ghosts/Behaviour/StalkBehaviour.cs`、`Ghosts/Behaviour/DarknessModifier.cs`、`Prefab GHOST_Stalker` | ① 玩家背对它时沿 `NavGrid` 逼近，保持 3~5m；② 玩家转头正对它 → 冻结（`Frozen`）；③ 全黑时速度更快；④ 不穿墙（路径来自 `NavService`）；⑤ 同屏 2 鬼帧时间不超基线 +2ms | `feat(ghosts): 第二个鬼——尾随者` |
| **C12** | 第三个鬼：干扰者 | 1 | C11 | `Ghosts/Behaviour/InterfereBehaviour.cs`、`Prefab GHOST_Interferer`（无模型） | ① 找最近 `IInterferable` 施法，有冷却；② 无 `Renderer`，只有逻辑与音效；③ 可由配置表开关；④ 同屏 3 鬼帧时间不超基线 +3ms | `feat(ghosts): 第三个鬼——干扰者` |
| **C13** | 猫预警联动 | 1 | C12 | `Cat/CatAlertBehaviour.cs`、`CatAgent.cs`、`HorrorSignalEvent` | ① 鬼出现前 2~5 秒，猫进 `Alert` 并朝鬼的方位看；② 预警强度随鬼的 `Intensity`；③ 不破坏原有距离驱动的 `CatBrain` 行为；④ 自测不受影响 | `feat(cat): 猫会预警鬼` |
| **C14** | 理智与恐怖事件联动 | 0.5 | C08/B03 | `PlayerStats.cs`、`HorrorDirector.cs` | ① 每次恐怖事件扣理智（按强度）；② 理智低时事件频率上升；③ 理智 0 触发"临界"效果（先只做屏幕暗化 + 音效） | `feat: 理智与恐怖事件联动` |
| **C15** | 鬼的配置与开关 | 0.5 | C12 | `World/Horror/HorrorConfig.cs`(SO)、`GhostSpawnEntry` | ① 每只鬼有 `enabled`/出现条件/权重；② 编辑器菜单可一键只开某只鬼做测试；③ 配置改动不需要改代码 | `feat(ghosts): 鬼的配置表与开关` |
| **C16** 🔒 | 性能复核（3 鬼同屏） | 1 | C15 | `docs/perf/phase-c.md`（新）、`Profiler` | ① 3 鬼同屏 + 猫 + 玩家：主线程 ≤ 11ms、`GC Alloc` 稳定态 0 B/帧、`Animator` ≤ 6；② 与 `baseline.md` 逐项对照成表；③ 超标项给出优化清单（不在这里做，进 E05） | `perf: Phase C 性能复核` |

**Phase C 出口闸门**：C01/C03/C07/C08/C11/C16 通过 + 3 只鬼可独立开关 + 猫的行为与拆分前完全一致（自测全绿）+ 3 鬼同屏不超预算。

---

## Phase D — 存档与终局（10.5 天 / 11 项）

| 编号 | 任务 | 天 | 前置 | 涉及文件 | 验收（可测） | commit |
|---|---|:--:|---|---|---|---|---|
| **D01** 🔒 | `ISaveable` + `SaveService` 骨架 | 1 | B07 | `Core/Save/ISaveable.cs`、`SaveService.cs`、`SaveRegistry.cs` | ① `ISaveable` 有 `string SaveId`、`object CaptureSave()`、`void RestoreSave(object)`；② `SaveService` 注册/注销在 `OnEnable/OnDisable`，场景切换不残留；③ 保存/加载顺序稳定（按 `SaveId` 排序）；④ EditMode 单测：注册 3 个 mock，断言捕获顺序与数量 | `feat(save): 建立 ISaveable 与 SaveService` |
| **D02** 🔒 | DTO + Newtonsoft + 原子写入 | 1 | D01 | `Core/Save/SaveFile.cs`、`SaveDto/*.cs`、`Packages/manifest.json`（加 `com.unity.nuget.newtonsoft-json`） | ① `SaveFile` 含 `version/createAtUtc/playSeconds/gameState/clock/player/flags/world/entities`；② `JsonSerializerSettings{TypeNameHandling=None}`；③ 写 `.tmp` → `File.Replace`，保留 `.bak`；④ 单测：序列化→反序列化后逐字段相等 | `feat(save): 存档文件结构与原子写入` |
| **D03** ⚠️ | `SceneId` + 校验器 + 关键对象标注 | 1 | D02 | `Core/Save/SceneId.cs`、`Editor/SceneIdValidator.cs`、`SampleScene` | ① 玩家锚点/床/猫碗/猫粮袋/镜子/猫窝/3 个鬼的 spawn 点都有非空唯一 `SceneId`；② 校验器在进 Play 前自动跑，缺 ID 或重复时**红色报错并阻止**；③ 现存 12 个可交互物也标注 | `feat(save): 稳定 SceneId 与校验器` |
| **D04** | 玩家/时间/数值存档 | 1 | D03 | `PlayerSaveable.cs`、`ClockSaveable.cs`、`PlayerStatsSaveable.cs` | ① 存：时间、疲劳、理智、锚点 ID、朝向；② 读：位置由锚点还原，不出现穿模；③ 对照表逐项验证（手写一张 10 行的表，存档前后逐项打勾） | `feat(save): 玩家与时间存档` |
| **D05** | 猫与喂食状态存档 | 1 | D04 | `CatSaveable.cs`、`BowlSaveable.cs`、`BagSaveable.cs`、`MirrorSaveable.cs` | ① 存：猫饥饿/mood/所在 spot/是否在窝；碗 `FoodAmount`+`state`；袋 `state`；镜子画面索引；② 读：猫回到窝并播 `Cat_Sleep`；碗按 `FoodAmount` 还原到正确视觉（粮堆缩放/颗粒数）；③ 读档后能直接继续喂食流程 | `feat(save): 猫与喂食状态存档` |
| **D06** | 鬼状态存档 | 1 | D05/C12 | `GhostSaveable.cs`、`EntityRegistry.cs` | ① 存：每只鬼 `active/pos/yaw/state/cooldown`；② 读：先 `NavGrid.TrySnap`，贴不回去则回 spawn 锚点；③ 读档后鬼不会瞬移进墙（用 3 个坐标各测一次） | `feat(save): 鬼状态存档` |
| **D07** | `flags` 已触发事件 | 0.5 | D05 | `Core/Save/SaveFlags.cs`、`HorrorDirector.cs`、一次性事件各处 | ① `flags` 是 `HashSet<string>`，存为数组；② 一次性事件触发前查 flag，触发后写入；③ 读档后镜子不会第二次"首次惊吓" | `feat(save): 一次性事件 flags` |
| **D08** 🔒 | 版本迁移框架 + 真实演练 | 1 | D07 | `Core/Save/SaveMigrator.cs`、`Tests/EditMode/SaveMigrationTests.cs` | ① `Migrate(JObject)` 链式调用；② **真实演练一次**：故意删一个字段→存档→加回字段→读档，断言走 `V1ToV2` 且不崩；③ 单测覆盖 `version=1` 存档能读进 `v2` 代码 | `feat(save): 存档版本迁移框架` |
| **D09** | 存读档 UI + 自动存档 | 1 | D08 | `UI/SaveMenuUI.cs`、`SaveService.cs` | ① 存档/读档各一个按钮可用；② 自动存档 4 个时机（睡觉完成/起床/喂食完成/里程碑 flag）实际写入；③ UI 显示"最后存档时间" | `feat(ui): 存读档界面与自动存档` |
| **D10** | 多槽扩展 | 0.5 | D09 | `SaveService.cs`（`Slot` 字段）、`UI/SaveMenuUI.cs` | ① 槽位只是路径层 `saves/slot1.json`，**游戏代码零改动**；② 3 个槽互不干扰（交叉存读验证） | `feat(save): 支持多存档槽` |
| **D11** 🔒 | 结局系统 | 1.5 | D10/C14 | `Core/Flow/EndingController.cs`、`UI/EndingUI.cs`、`GameState.Ended` | ① 至少 1 个可达成结局（条件可从 `HorrorConfig` 配）；② 进入 `Ended` 后输入锁死、时钟停；③ 结局 UI 显示文案 + 统计（天数/喂猫次数/理智）；④ 结局后可回主菜单 | `feat: 结局系统` |

**Phase D 出口闸门**：D01/D02/D08/D11 通过 + "存档→退出→读档→逐项对照表全勾" + 老版存档能读进新版代码 + 至少 1 个结局可达。

---

## Phase E — 打磨（8 天 / 9 项）

| 编号 | 任务 | 天 | 前置 | 涉及文件 | 验收（可测） | commit |
|---|---|:--:|---|---|---|---|
| **E01** | 主菜单 | 1 | D11 | `UI/MainMenuUI.cs`、`MainMenu.unity`（或覆盖层） | ① 开始/继续/设置/退出可用；② 返回菜单不残留游戏状态（时钟停、`ControlLock` 清空、`Registry` 清空）；③ 菜单进游戏走 `Opening` | `feat(ui): 主菜单` |
| **E02** | 设置菜单 | 1 | E01/A14 | `UI/SettingsUI.cs`、`PlayerPrefs` | ① 主音量/SFX/Ambience 三滑条实时生效；② 鼠标灵敏度、Y 轴反转生效；③ 重启后保留 | `feat(ui): 设置菜单` |
| **E03** | `AudioMixer` 分组 + 混音 | 1 | E02/A15 | `Audio/Main.mixer`、`AudioService.cs` | ① 5 个 group 真实生效（不是线性缩放）；② 猫叫/鬼叫/环境音分属不同 group；③ 静音 0 dB 与 -80 dB 边界不爆音 | `feat(audio): AudioMixer 分组混音` |
| **E04** | `OnGUI` → uGUI 迁移 | 1 | B05 | `UI/CrosshairUI.cs`、`UI/PromptUI.cs`、`UI/PourProgressUI.cs`、`FirstPersonController.cs`、`CatFoodBowl.cs`、`CatFoodBagInteractable.cs` | ① 3 处 `OnGUI` 全部删除（`grep -c "void OnGUI"` == 0）；② 准星/提示/倒粮进度条功能一致；③ 稳定态 `GC Alloc` 仍为 0 | `refactor(ui): 用 uGUI 替换 OnGUI 绘制` |
| **E05** | 渲染与灯光优化 | 1 | C16 | `SampleScene` 灯光、`Global Volume`、`docs/perf/phase-e.md` | ① 实时投影灯 ≤ 3，其余改 Baked 并重烘；② 帧时间比基线下降且画质肉眼无退化（截图对比）；③ 输出优化前后对照表 | `perf: 灯光与后处理优化` |
| **E06** 🔒 | 出包 + IL2CPP 存档实测 | 1 | D11 | `ProjectSettings`、`link.xml`、`docs/build/RELEASE.md` | ① Windows x64 IL2CPP 出包成功；② **出包版**存档→退出→重进→读档成功（Newtonsoft 反射未被裁剪）；③ 冒烟清单 12 项全过（开场/移动/交互/喂食/睡觉/鬼/存档/读档/设置/结局/退出/重进） | `build: 首次可玩包体` |
| **E07** 🔒 | 全链路回归自测 | 1 | E06 | `Editor/SelfTest/CatFeedingSelfTest.cs`（扩展到全链路） | ① 覆盖：开场→喂食→睡觉→次日→鬼出现→存档→读档→结局；② 连续 3 次 `RESULT: PASS`；③ 总时长 < 8 分钟（否则要拆分用例） | `test(selftest): 全链路回归` |
| **E08** | 包体与资源压缩 | 0.5 | E06 | 贴图导入设置、音频压缩、`ProjectSettings/QualitySettings` | ① 贴图按用途分档（1024/512/256）；② 音频短音效用 ADPCM、长环境音用 Vorbis；③ 包体记录并对比前一版 | `chore(build): 资源压缩与包体优化` |
| **E09** | 文档收尾 | 0.5 | 全部 | `README.md`、`docs/PLAYTEST.md`、`docs/KNOWN_ISSUES.md` | ① README 含操作说明/构建步骤/目录结构；② 已知问题列表（含 `Cat_Run` 禁用说明）；③ 新人（未来的你）能照 README 跑起来 | `docs: 收尾文档` |

**Phase E 出口闸门**：E06/E07 通过 + 出包版冒烟 12 项全过 + IL2CPP 存档实测通过。

---

## 附录 A：任务依赖拓扑（关键路径）

```
A01 ─┬─ A02/A03/A04/A05 ─┐
     └─ A06 ─ A07 ─ A08 ─ A09 ─ A10 ─ A11 ─ A12 ─┬─ A13 ─┐
                                                  ├─ A16  │
                                                  └─ A17  │
                        A05 ────────────────── A18 ─ A19  │
                                                 └────── A20 ─┐
                                                              │
B01 ─ B02 ────────────────────────────────────────────────────┤
B01 ─ B03 ─┬─ B04 ─────────────────────────────────────────────┤
           ├─ B05 ─ B06 ─ B07 ─┬─ B08                           │
           │                   ├─ B09 ─ B11                     │
           │                   └─ B12                           │
B10（可任意时刻插入）────────────────────────────────────────────┤
                                                               ▼
C01 ─ C02 ─┬─ C03 ─┬─ C07 ─ C08 ─┬─ C09 ─┐
           │        │            └─ C14 ─┤
           │        └─ C11 ─ C12 ─┬─ C13 │
           └─ C04 ─ C05 ─ C06     └─ C15 ─ C16
                                       │
D01 ─ D02 ─ D03 ─ D04 ─ D05 ─ D06 ─ D07 ─ D08 ─ D09 ─ D10 ─ D11
                                                          │
E01 ─ E02 ─ E03 ─ E04 ─ E05 ─ E06 ─ E07 ─ E08 ─ E09 ◄──────┘
```

**关键路径**（决定总工期）：`A01→A06→A07→A08→A09→A10→A11→A12→A20→B01→B03→B05→B06→B07→C01→C03→C07→C08→C11→C12→C16→D01→D02→D03→D04→D05→D06→D07→D08→D09→D10→D11→E06→E07`。

**可并行的支线**（某天时间不够或想换换脑子时做）：
- `A02`/`A03`/`A04`（清理类，互不依赖）
- `B10`（猫跟随修复，随时可做）
- `A17`（编辑器路径，随时可做）
- `C09`/`C10`（环境异常，`C08` 之后可并行）

---

## 附录 B：每阶段第一天做什么（消除"打开工程不知道干啥"）

| 阶段首日 | 具体动作 |
|---|---|
| **A 第 1 天** | `A01`：先 `git status` 看那 10 个未提交删除，确认是审计时的清理 → 补 `.gitignore` → 单独提交。今天不写代码，只把地基擦干净 |
| **B 第 1 天** | `B01`：先写 `GameClock` 的 EditMode 单测（纯 C# 部分），再让 `MonoBehaviour` 壳调它。**先测后写** |
| **C 第 1 天** | `C01`：只写 `EntityStateMachine` + 单测（纯 C#，不碰场景）。今天的目标不是"鬼出现"，是"状态机可被断言" |
| **D 第 1 天** | `D01`：只写 `ISaveable` + 注册表 + mock 单测。今天不碰真存档 |
| **E 第 1 天** | `E01`：先把主菜单做出来，让自己能"从头开始玩"，这会立刻暴露状态残留问题 |

---

## 附录 C：哪些任务可以"今天只做半天就提交"

以下任务天然可拆成两个 0.25 天，时间不够时做前半：
- `A08`：先只改枚举与谓词（不动调用点），编译过了就提交；调用点迁移单独提交。
- `A14`：先只做 `Play(SfxId)` 单入口（不分组），能出声就提交；分组与池后续提交。
- `A16`：先加接口 + 让碗实现，袋暂不改，编译过就提交；反向引用删掉单独提交。
- `C01`：先写 `EntityStateMachine` + 单测；其余零件单独提交。
- `D02`：先只做 `SaveFile` + 序列化单测；原子写入单独提交。
