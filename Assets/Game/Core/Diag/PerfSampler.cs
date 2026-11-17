#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

namespace NOCTURNE.Core.Diag
{
    /// <summary>
    /// 性能基线采样器（仅编辑器）。启动条件：工程根目录存在 `.workbuddy/perf.on`。
    ///
    /// 为什么用"游戏内采样 + 写文件"而不是 Profiler 窗口截图：
    /// Play Mode 下 MCP 会掉线，外部读不到实时数据；写成文本才能对比不同阶段的数值。
    /// </summary>
    public sealed class PerfSampler : MonoBehaviour
    {
        const string MarkerRelative = "../.workbuddy/perf.on";
        const string OutRelative = "../.workbuddy/perf_baseline.log";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string marker = Path.GetFullPath(Path.Combine(Application.dataPath, MarkerRelative));
            if (!File.Exists(marker)) return;

            var go = new GameObject("[PerfSampler]");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfSampler>();
        }

        [SerializeField] float sampleSeconds = 25f;

        void Start()
        {
            StartCoroutine(Sample());
        }

        IEnumerator Sample()
        {
            // 跳过开场（前 6 秒主要是黑屏与淡入，不代表游玩负载）
            yield return new WaitForSeconds(6f);

            int frames = 0;
            float elapsed = 0f;
            float worst = 0f;
            long memStart = System.GC.GetTotalMemory(false);
            long gcStart = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();

            var dts = new System.Collections.Generic.List<float>(2048);

            while (elapsed < sampleSeconds)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                frames++;
                if (dt > worst) worst = dt;
                dts.Add(dt);
                yield return null;
            }

            long gcDelta = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - gcStart;

            dts.Sort();
            float p50 = dts[dts.Count / 2];
            float p99 = dts[Mathf.Clamp((int)(dts.Count * 0.99f), 0, dts.Count - 1)];

            int animators = 0, activeAnimators = 0, behaviours = 0, audioSources = 0;
            foreach (var a in FindObjectsOfType<Animator>(true))
            {
                animators++;
                if (a.isActiveAndEnabled && a.gameObject.activeInHierarchy) activeAnimators++;
            }
            foreach (var b in FindObjectsOfType<MonoBehaviour>(true))
                if (b.isActiveAndEnabled) behaviours++;
            foreach (var s in FindObjectsOfType<AudioSource>(true))
                if (s.isPlaying) audioSources++;

            var sb = new StringBuilder();
            sb.AppendLine("# NOCTURNE 性能基线");
            sb.AppendLine();
            sb.AppendLine("- 采样时间: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("- 采样时长: " + sampleSeconds.ToString("F0") + "s（跳过开场 6s）");
            sb.AppendLine("- 渲染: " + (Screen.width + "x" + Screen.height) + " @" + Screen.currentResolution.refreshRateRatio.value.ToString("F0") + "Hz");
            sb.AppendLine("- 图形 API: " + SystemInfo.graphicsDeviceType);
            sb.AppendLine("- 机器: " + SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName);
            sb.AppendLine();
            sb.AppendLine("| 指标 | 值 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 帧数 | " + frames + " |");
            sb.AppendLine("| 平均 FPS | " + (frames / elapsed).ToString("F1") + " |");
            sb.AppendLine("| 中位帧时间 | " + (p50 * 1000f).ToString("F2") + " ms |");
            sb.AppendLine("| P99 帧时间 | " + (p99 * 1000f).ToString("F2") + " ms |");
            sb.AppendLine("| 最差帧 | " + (worst * 1000f).ToString("F2") + " ms |");
            sb.AppendLine("| 托管堆增量 | " + ((gcDelta) / 1048576.0).ToString("F2") + " MB |");
            sb.AppendLine("| 每帧 GC | " + (gcDelta / (double)frames / 1024.0).ToString("F2") + " KB |");
            sb.AppendLine("| Animator（总/活动） | " + animators + " / " + activeAnimators + " |");
            sb.AppendLine("| MonoBehaviour（活动） | " + behaviours + " |");
            sb.AppendLine("| 播放中 AudioSource | " + audioSources + " |");
            sb.AppendLine();

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, OutRelative));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[Perf] 采样完成，已写入 " + path + "\n" + sb);
        }
    }
}
#endif
