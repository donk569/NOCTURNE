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
