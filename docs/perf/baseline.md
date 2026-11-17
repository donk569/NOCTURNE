# NOCTURNE 性能基线

- 采样时间: 2026-11-17 14:16:51
- 采样时长: 25s（跳过开场 6s）
- 渲染: 848x384 @240Hz
- 图形 API: Direct3D11
- 机器: 13th Gen Intel(R) Core(TM) i7-13700H / NVIDIA GeForce RTX 4060 Laptop GPU

| 指标 | 值 |
|---|---|
| 帧数 | 2393 |
| 平均 FPS | 95.7 |
| 中位帧时间 | 10.02 ms |
| P99 帧时间 | 16.94 ms |
| 最差帧 | 29.15 ms |
| 托管堆增量 | 6.81 MB |
| 每帧 GC | 2.92 KB |
| Animator（总/活动） | 1 / 1 |
| MonoBehaviour（活动） | 51 |
| 播放中 AudioSource | 1 |


## 怎么复现

```bash
touch .workbuddy/perf.on          # 标记文件
# 进入 Play Mode，等约 35 秒（6s 跳开场 + 25s 采样）
# 结果写入 .workbuddy/perf_baseline.log
rm .workbuddy/perf.on
```

## 读法

- **中位帧时间 10.02ms** ≈ 100 FPS，是稳定态；**P99 16.94ms** 说明偶发掉到 60 FPS 边界；
  **最差帧 29.15ms** 是单次卡顿（可能是 GC 或编辑器本身的开销）。
- **每帧 GC 2.92 KB** 是当前红线参考值。Phase C 加鬼之后，这个数字不该显著上升；
  一旦超过 8KB/帧，优先查 `Update` 里的字符串拼接与 LINQ。
- **Animator 1 / MonoBehaviour 51** 是"3 只鬼同屏"预算的基数：每只鬼预计 +1 Animator、
  +6~10 个组件，3 只后仍在安全区。
- 采样窗口是编辑器 Game 视图（848x384），**不代表真机分辨率下的 GPU 开销**，
  只用于纵向对比不同阶段的 CPU 侧变化。

## 已知局限

- 编辑器内采样：包含 Editor 自身的开销，绝对值偏低。
- 未分离主线程/渲染线程，未采集 GPU 时间——需要 GPU 数据时用 Profiler 的 GPU 模块单独看。
- 只在"开场结束后静置探索"这一段采样，**没覆盖喂食高峰与睡眠**。
  后续若要更细的阶段对比，在 `PerfSampler` 里按 `GameState` 分桶即可。
