# 开发会话日志

> 记录每轮自主开发的完成项、验证证据与待确认问题。
> 用途：下次开工先看这里，避免重复调查；疑问在用户回来时集中处理。

---

## 2026-11-15 会话（自主开发第 1 轮）

### 完成情况：Phase A 前 9 项（A01–A09）

| 任务 | 提交 | 内容 | 验证证据 |
|---|---|---|---|
| A01 | `chore(repo)` | 提交 70 项资源删除（36 资源 / 71.5MB）+ 审计报告入库 | 6 个 TMP 组件全部使用保留的 `STXINWEI SDF Dynamic`，`hasGlyph=True`；编译 0 错误 |
| A02 | `chore(world)` | 删除死代码 `PlanarMirror.cs`(185) | 全项目仅 3 处提及（1 处注释 + 自身 2 处）、场景挂载 0、GUID 引用 0；`PR_BodyMirror_Glass` 与 `MirrorPicture` 均正常 |
| A03 | `chore(scene)` | 删除 3 个调试遗留相机 | `CAMERAS_LEFT=1`、`Camera.main=PlayerCam`、Play 6s/582 帧无异常 |
| A04 | `chore(scene)` | 抽屉去重名（`Drawer_Desk_*` / `Drawer_Nightstand_*`） | 5 个 `DrawerInteractable` 的 `NULL_REFS=0`；Play 5s 无异常 |
| A05 | `chore(tests)` | 3 个调试脚本（1099 行）`#if UNITY_EDITOR` 包裹 | 编译 0 错误；完整自测 51 行 `DONE`、0 警告 0 错误 |
| A06 | `docs` | 三份架构/清单/规范文档入库 | 944 + 776 + 217 行 |
| A07 | `feat(core)` | 建 `NOCTURNE.Core` 程序集 + `Log` | 编译 0 错误；实测输出 `[Probe] warn path ok` |
| A08 | `refactor(core)` | `GameState` 扩 9 态 + 5 个派生谓词 + 迁至 `Core/Flow` | 枚举 `Opening=0…Loading=8`；场景组件仍在 `OpeningSystems` 且 `_state=0`；旧谓词已移除；完整自测 51 行 `DONE` |
| A09 | `refactor(core)` | `ControlLock` 替换 `FeedingInteractionLock` | 完整自测：`G3` 松手后进度归零、`H` 倒粮正常、推进到 `Sleeping`、0 警告 0 错误 |

### 自检机制（每项任务必须过）

1. **编译闸门**：`refresh_unity` 后 `mcons.py console` 必须 0 error（MCP WebSocket 提示除外）。
2. **引用闸门**：改场景/搬文件后，用 `probe_missing_refs.cs` 确认场景 `MISSING=0`。
3. **功能闸门**：涉及运行逻辑的改动，必须跑 `CatFeedingSelfTest`（`.workbuddy/selftest.on` + 日志末尾 `DONE`）。
4. **提交粒度**：一个任务 = 一次 commit，commit message 用 BACKLOG 里给定的那行。

### 本轮踩到的坑（已修，后续注意）

- **`Log.cs` 的 `Debug` 歧义**：同时 `using System.Diagnostics` 和 `UnityEngine` 时 `Debug` 报 CS0104。已加 `using Debug = UnityEngine.Debug;`。
- **`csrun.py` 不支持 stdin**：`python csrun.py -` 会 FileNotFound。临时片段要写成文件。
- **codedom 编译器限制**：不支持局部函数、`var` 推断静态成员访问。写探针时用显式类型。

### 未做：A10–A20（11 项）

不是卡住，是本轮先收在 A09 这个稳定的骨架上。剩余任务见 `BACKLOG.md`，从 **A10（拆 `PlayerInteractor`）** 继续即可。

---

## 待确认问题（OPEN QUESTIONS）

### Q-01 A09 的范围收窄：`fpc.enabled=false` 未纳入 ControlLock

- **现状**：`PlayerSittingController:117` 与 `PlayerLyingController:122` 仍用 `fpc.enabled = false` 整体停用第一人称组件，A09 只把倒粮改成了 `ControlLock`。
- **为什么不改**：它不是"锁输入"，而是"整个组件停机"（连 `HeadBob`/`Flashlight` 都停）。而且有 3 个外部消费者依赖这个状态：`ChairInteractable.cs:123` 读 `!_fpc.enabled` 清空长按、`CatFeedingSelfTest.cs:274` 断言它、两个 Controller 各自 `= true` 恢复。
- **要你决定**：① 保持现状（语义清晰、风险低）；② 后续单独一个任务把它迁进 ControlLock，需要同步改这 3 个消费者。

### Q-02 鬼的规模仍是假设

按 **3 只同屏 / 8 只定义** 设计，你表示"还不确定"。若提到 5 只以上，Phase C 开工前必须先上 `SensesBatch`（架构已留位置，不用改结构）。

### Q-03 场景改动后的美术验收

A03（删相机）、A04（抽屉改名）都改了 `SampleScene`。命名改动不影响运行，但如果你有旧截图/文档标注了 `Drawer_Top` 之类的名字，需要同步。要我重新出一套场景截图就告诉我。

---

## 下次开工的入口

1. 先看 `BACKLOG.md` 的 **A10**：从 `FirstPersonController` 拆出 `PlayerInteractor`。
   - 注意：`CatFoodBagInteractable` 会读 `_fpc.CurrentTarget`，拆分后必须保留 `FPC.CurrentTarget` 作为转发属性，否则编译断。
2. 跑一次自测确认基线仍绿，再动手。
3. 若 Q-01 选了方案②，把 A09 剩余部分插在 A11 之前做。

---

## 2026-11-15 续（A10–A12）

| 任务 | 提交 | 内容 | 验证证据 |
|---|---|---|---|
| A10 | `refactor(player)` | 从 FPC 拆出 `PlayerInteractor`（射线/CurrentTarget/提示/交互键），FPC 保留转发属性 | 编译 0 错误；自测 E/F/F2/F3/G/G2/G3/H 段全通过；`interactDistance/interactMask/interactKey` 场景值经核实均为默认后才删除 |
| A11 | `refactor(interaction)` | 建 `NOCTURNE.Interaction` 程序集，接口迁出并改命名空间，8 个引用者补 using | 编译 0 错误；场景 1715 字段 `MISSING=0`；12 个交互物组件全在；自测全绿 |
| A12 | `refactor` | 建 `NOCTURNE.Player` / `NOCTURNE.World`，10 个脚本迁出 `Assets/Art`，旧目录删除 | 编译 0 错误；`MISSING=0`；26 个脚本全部正常挂载；自测全绿 |

### 当前程序集结构

```
NOCTURNE.Core          Core/            Log, GameState(9态), GameStateManager, ControlLock
NOCTURNE.Interaction   Interaction/     IInteractable, IInteractPrompt
NOCTURNE.Player        Player/          FirstPersonController, PlayerInteractor, PlayerSittingController
NOCTURNE.World         World/Props/     Chair/Drawer/HingedDoor/Mirror/SlidingDoor/TriggerVolume
NOCTURNE.Cat           Cat/Scripts/     CatAgent, CatBrain, CatNavGrid, CatOpeningController ...
Assembly-CSharp        Opening/ Feeding/ UI/   剩余（A20 收尾时清零）
```

### 本轮新增踩坑

- **`ChairHoldUI` 依赖 TMPro**：放进 `World` 会逼 World 引用 `Unity.TextMeshPro`，破坏"World 不含 UI"的分层。已移到 `Game/UI/`（暂归 Assembly-CSharp），见 Q-04。
- **跨程序集依赖要按矩阵来**：`ChairInteractable`(World) 引用 `PlayerSittingController`(Player) 是矩阵允许的 **World → Player** 单向依赖；反过来不行。加 asmdef 前必须先查依赖方向，否则会出现循环引用报错。
- **`execute_code` 在一帧内同步执行**：想验证 `Update` 驱动的逻辑（如 `PlayerInteractor.Scan`）不能靠反射调用 + 立即读结果，必须让它真的跑过一帧。这类验证改用"自测脚本内断言"更可靠。

### 新的待确认问题

**Q-04 `ChairHoldUI` 的归属**
它同时依赖 `ChairInteractable`(World)、`PlayerSittingController`(Player) 和 TMPro。按架构矩阵 `UI` 只能引用 Core+Interaction，所以现在暂放 `Game/UI/` 且不建 asmdef（归 Assembly-CSharp）。等 Phase B 做 HUD 时，若要建 `NOCTURNE.UI`，需要先把 `ChairHoldUI` 改成订阅事件（不再直接引用 Chair/Sitting），否则建不出来。

### 下次开工的入口

**A13：`Nav` 程序集 —— `CatNavGrid` → `Nav/NavGrid`**
- 注意：`CatNavGrid` 被 `CatAgent` 用 `List<CatDoorway>` 参数调用；改名时 `CatNavGrid.cs` 与 `CatDoorway.cs` 要一起处理（`CatDoorway` 是纯 C# 类，被 `CatNavGrid.Build` 与 `CatAgent` 共同引用）。
- 猫域 `NOCTURNE.Cat.asmdef` 目前 `references: []`，新建 `NOCTURNE.Nav` 后要给它加引用，否则 `CatAgent` 找不到 `NavGrid`。

---

## 2026-11-15 续 2（A13）

| 任务 | 提交 | 内容 | 验证证据 |
|---|---|---|---|
| A13 | `df76978` | `CatNavGrid` → `Nav/NavGrid`（GUID 不变），新建 `NOCTURNE.Nav` 程序集；新增 `IDoorPassage` 接口解耦"门的语义" | 编译 0 错误；`MISSING=0`；完整自测 `DONE`、0 警告 0 错误；Play 6s/597 帧烟测 |

### 关键设计决策：`CatDoorway` 用接口解耦，而不是搬进 Nav 域

采用方案 **(b)**：在 `Nav` 域定义 `IDoorPassage`（只有 `Passable` + `DoorTransform` 两个成员），`CatDoorway` 留在 `Cat` 域并实现它，`NavGrid.Build` 只接受 `IReadOnlyList<IDoorPassage>`。

好处：
- `Nav` 不再认识"门"的语义（谁能开、合页怎么转、猫被门板扫到怎么躲），这些留在实体域；
- `CatDoorway` 的 `List<CatDoorway>` 通过 `IReadOnlyList` 协变直接传入，`CatAgent` 调用点几乎没改；
- 未来加"鬼能穿的门""只 Warning 不阻挡的门"时，只要实现这个两成员接口，`NavGrid` 一行都不用动。

### 主理人拍板：`NOCTURNE.Nav` 不引用 `Core`

技术负责人给出三个选项，我选 **① 删掉死引用**：
- 证据：`grep` 全 `Assets/Game/Nav/*.cs` 无任何 `using NOCTURNE.Core` / `Log.` / `GameState` / `ControlLock`；
- 依据：ARCHITECTURE §5.2 禁令 2 明确「Nav 不许引用任何 `NOCTURNE.*`，连 Core 都不要」；
- 保留它只会让"Nav 是纯算法域"这条边界变模糊。等 **C03 `NavService`** 真要写日志时再加，届时是一次显式决策。

最终 `NOCTURNE.Nav` 的 `references: []` —— 目前全项目唯一零依赖的运行时程序集。

### 当前程序集状态

```
NOCTURNE.Core          references: []
NOCTURNE.Nav           references: []              ← A13 新增，零依赖
NOCTURNE.Interaction   references: [Core]
NOCTURNE.Player        references: [Core, Interaction]
NOCTURNE.World         references: [Core, Interaction, Player, Cat]
NOCTURNE.Cat           references: [Nav]            ← A13 更新
Assembly-CSharp        剩余 Opening / Feeding / UI
```

### 下次开工的入口

**A14：`AudioService`（分组 + 池）**
- 位置：`Assets/Game/Core/Audio/`（属 Core，因为 `Log` 与它同域且被所有域调用）
- 要点：`Play(SfxId)` / `PlayAt(Transform, SfxId)` 两个入口；5 个分组（Master/SFX/Ambience/Voice/UI）；池 24 个 `AudioSource`，超出抢最远；常驻 `DontDestroyOnLoad`
- 注意：Core 要新加 `Unity.TextMeshPro`？**不需要**，但 Core 需要 `UnityEngine.AudioModule`（默认就在引擎内，不用显式引用）
- 技术负责人的提醒：**EditMode 测试一直没跑过**（`CatBrainTests` 在 `NOCTURNE.Cat.Tests` 里），建议 A18/A19 统一跑一次；可复用的 Play 验证脚本：`.workbuddy/tools/a13_playtest.py`
