using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using NOCTURNE.Core.Audio;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 一键重建 <see cref="AudioCatalog"/>：扫描工程里的音频资源，按文件名映射到 SfxId。
    ///
    /// 为什么要有它：手工在 Inspector 里拖 11 个 clip、以后每加一个音效再拖一次，
    /// 是典型的"会忘记、会拖错"的活。这里把它变成一条菜单命令，并且**缺文件会报出来**。
    /// </summary>
    public static class AudioCatalogBuilder
    {
        const string CatalogDir = EditorPaths.AudioCatalogDir;
        const string CatalogPath = CatalogDir + "/AudioCatalog.asset";

        // 文件名（不含扩展名）→ SfxId。找不到对应项的音效会被报告但不会中断。
        static readonly Dictionary<string, SfxId> NameMap = new Dictionary<string, SfxId>
        {
            { "cat_bell", SfxId.CatBell },
            { "cat_attention", SfxId.CatMeowAttention },
            { "cat_hungry", SfxId.CatMeowHungry },
            { "cat_sweet", SfxId.CatMeowSweet },
            { "door_open", SfxId.DoorOpen },
            { "door_close", SfxId.DoorClose },
            { "drawer_slide", SfxId.DrawerSlide },
            { "kibble_pour", SfxId.KibblePour },
            { "blanket_rustle", SfxId.BlanketRustle },
            { "room_tone_loop", SfxId.RoomTone },
            { "breath_slow", SfxId.BreathSlow },
        };

        [MenuItem("NOCTURNE/音频/重建 AudioCatalog", false, 40)]
        public static void Rebuild()
        {
            var clips = FindAllAudioClips();
            var entries = new List<AudioCatalog.Entry>();
            var used = new HashSet<SfxId>();
            var unknown = new List<string>();

            foreach (var pair in clips)
            {
                SfxId id;
                if (!NameMap.TryGetValue(pair.Key, out id))
                {
                    unknown.Add(pair.Key + " (" + pair.Value + ")");
                    continue;
                }
                if (used.Contains(id)) continue;

                entries.Add(new AudioCatalog.Entry
                {
                    id = id,
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(pair.Value),
                    volume = 1f
                });
                used.Add(id);
            }

            if (!Directory.Exists(CatalogDir))
            {
                Directory.CreateDirectory(CatalogDir);
                AssetDatabase.Refresh();
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 报告
            var missing = catalog.CollectMissing();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[AudioCatalog] 已登记 " + entries.Count + " 条，资产: " + CatalogPath);

            if (unknown.Count > 0)
            {
                sb.AppendLine("未映射的音频文件（" + unknown.Count + "）—— 需要在 SfxId 与 NameMap 里加一项：");
                foreach (var u in unknown) sb.AppendLine("  · " + u);
            }

            if (missing.Count > 0)
            {
                sb.AppendLine("缺 clip 的 SfxId（" + missing.Count + "）—— 这些在播放时会静默跳过：");
                foreach (var m in missing) sb.AppendLine("  ! " + m);
            }

            Debug.Log(sb.ToString());
            if (missing.Count > 0) Debug.LogWarning("[AudioCatalog] 有 " + missing.Count + " 个 SfxId 还没有 clip，见上一条日志。");
        }

        [MenuItem("NOCTURNE/音频/校验 AudioCatalog", false, 41)]
        public static void Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[AudioCatalog] 尚未创建，先执行「重建 AudioCatalog」。");
                return;
            }

            var missing = catalog.CollectMissing();
            if (missing.Count == 0)
            {
                Debug.Log("[AudioCatalog] 校验通过：所有 SfxId 都有 clip。");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[AudioCatalog] 缺失 " + missing.Count + " 个 clip：");
            foreach (var m in missing) sb.AppendLine("  ! " + m);
            Debug.LogWarning(sb.ToString());
        }

        static Dictionary<string, string> FindAllAudioClips()
        {
            var result = new Dictionary<string, string>();
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var name = Path.GetFileNameWithoutExtension(path);
                if (!result.ContainsKey(name)) result[name] = path;
            }
            return result;
        }
    }
}
