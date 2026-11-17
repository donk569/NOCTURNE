# 端到端自测说明（SELFTEST）

> 对应任务 A18。这份文档回答三个问题：怎么开、看什么、什么算通过。

## 一、它是什么

`CatFeedingSelfTest` 是一个**游戏内自驱动的端到端回归测试**。它不依赖外部驱动，自己在 Play Mode 里
把"开场 → 起床 → 叫唤 → 走客厅 → 拿猫粮袋 → 倒粮 → 猫吃 → 回窝睡 → 第二轮叫醒"整条链路跑一遍，
并把每一步的实测值写进日志文件。

为什么这样做：Unity MCP 插件在 Play Mode 里会反复掉线，没法从外部逐帧驱动。所以改成"游戏内自驱动 +
写日志到磁盘"，外部只看文件就能判断结果。

## 二、怎么开

1. 在工程根目录创建标记文件：

   ```bash
   touch .workbuddy/selftest.on
   ```

2. 进入 Play Mode（编辑器点 Play，或用 MCP：`manage_editor {"action":"play"}`）。

3. 等待约 **150 秒**（完整流程含两轮喂食与睡眠）。

4. 读取日志：`.workbuddy/selftest.log`

5. 退出 Play Mode，**删掉标记文件**（否则下次进 Play 又会自动跑）：

   ```bash
   rm .workbuddy/selftest.on
   ```

> 没有标记文件时，自测脚本完全不介入——连 `Update` 都不挂，不影响正常游玩。

## 三、什么算通过

只认日志**最后两行**：

```
[SELFTEST] RESULT: PASS
[SELFTEST] DONE 全流程结束
```

- `RESULT: PASS` —— 零警告零错误。
- `RESULT: PASS_WITH_WARNINGS(n)` —— 流程跑完但期间有 n 条警告，**需要人工看一眼 `Z2 问题清单`**，
  通常不算失败，但不能无视。
- `RESULT: FAIL(n errors)` —— 期间出现了 n 条错误，**必须修**。

除此之外，日志必须包含这些关键行（缺任何一行都说明链路断了）：

| 行 | 期望值 | 说明 |
|---|---|---|
| `A0 黑屏期间猫音源累计播放` | `0.00s` | 开场白期间必须完全静音 |
| `A1 头部运动` | 峰峰值 < 0.01m | 抽搐会超过 0.01 |
| `A0b 睁眼之后猫叫` | > 0s | 证明睁眼后确实开始叫 |
| `B1 跳下床` | 最高 y > 起点 y | 有起跳弧线，而不是整体平移 |
| `B2 玩家控制权` | `fpc.enabled=True` | 起床后控制权交还 |
| `F2 放回茶几` | 偏差 `0.000m` | 放回必须精确复位 |
| `G3 半途松手` | 进度回到 `0%`、盆 `Empty` | 松手不能留残留 |
| `H2 袋子倾角` | ≈ `86°` | 倒粮姿态正确 |
| `I 阶段 → Sleeping` | 出现 | 吃完能回窝 |
| `N 第二轮阶段 → Eating` | 出现 | 倒粮能把猫叫醒再吃一轮 |
| `Y 铃铛` | > 0 次 | 移动时才响 |
| `Z 控制台统计` | `警告 0 条，错误 0 条` | Console 干净 |

## 四、失败时怎么定位

1. **先看最后一行落在哪一步** —— 日志是顺序写的，最后一条就是卡住的位置。
2. 看 `Z2 问题清单` —— 自测把期间的 warning/error 都抄进去了。
3. 常见失败与原因：

| 现象 | 可能原因 |
|---|---|
| 日志只有 `A0` 就停了 | 开场没结束（黑屏未淡出 / 独白卡住） |
| 停在 `C 叫唤后` | 猫没走到玩家身边（导航或阶段机问题） |
| 停在 `E 准星对着猫粮袋` 且 `target=null` | 交互射线没命中（`PlayerInteractor` 或碰撞层） |
| 停在 `G 准星对着饭盆` | 碗的 `CanInteract` 为 false（袋子没在手上） |
| `RESULT: FAIL` 但流程走完 | 期间有异常被 catch 或资源缺失，看 `Z2` |

## 五、什么时候必须跑

- **每个 BACKLOG 任务完成、提交之前**（这是 Phase 出口闸门）。
- 改了以下任一内容之后：`CatAgent` / `CatBrain` / `NavGrid` / `CatFoodBowl` /
  `CatFoodBagInteractable` / `CatFeedingSequenceController` / `FirstPersonController` /
  `PlayerInteractor` / 场景层级结构。
- 调整了任意 `[SerializeField]` 参数之后。

## 六、已知限制

- 自测跑在**真实时间**里（约 150 秒），不适合频繁跑；改 UI 或纯编辑器工具时可以不跑。
- 它只覆盖"首次喂食"这条主链路。**开场之外的剧情、鬼、睡眠、结局都没有覆盖**——
  那些要等 Phase B/C 落地后按同样模式扩展（在 `CatFeedingSelfTest` 里加段，或另开一个
  `NightLoopSelfTest`）。
- `CatBrainTests`（EditMode 单测）是另一套，跑法是 Window → General → Test Runner → EditMode → Run All，
  **不包含在本自测里**。
