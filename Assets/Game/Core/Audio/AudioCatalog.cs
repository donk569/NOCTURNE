using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOCTURNE.Core.Audio
{
    /// <summary>
    /// SfxId → AudioClip 的唯一映射表。
    ///
    /// 为什么用 ScriptableObject 而不是场景里的数组：
    /// 1. 它可以被 Editor 工具批量填充与校验（缺 clip 直接报错，而不是运行时静默无声）；
    /// 2. 它跨场景共享，不会因为换场景丢配置；
    /// 3. 它放在 <c>Resources/</c> 下，由 <see cref="AudioService"/> 启动时自动加载，
    ///    不需要在场景里预先摆一个引用它的对象。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "NOCTURNE/Audio Catalog", order = 10)]
    public sealed class AudioCatalog : ScriptableObject
    {
        const string ResourcesPath = "AudioCatalog";

        [Serializable]
        public struct Entry
        {
            public SfxId id;
            public AudioClip clip;

            [Range(0f, 1f)]
            [Tooltip("这条音效的基准音量。0 表示未配置，播放时会退回 1")]
            public float volume;
        }

        [SerializeField] Entry[] entries = new Entry[0];

        Dictionary<SfxId, Entry> _map;
        static AudioCatalog _cached;

        public Entry[] Entries { get { return entries; } }

        void OnEnable()
        {
            _map = null;
        }

        /// <summary>从 Resources 加载（只加载一次）。没有资产时返回 null。</summary>
        public static AudioCatalog Load()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<AudioCatalog>(ResourcesPath);
            return _cached;
        }

        void BuildMap()
        {
            _map = new Dictionary<SfxId, Entry>();
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.id == SfxId.None) continue;
                _map[e.id] = e;     // 后出现者覆盖，便于调试时临时替换
            }
        }

        public bool TryGet(SfxId id, out AudioClip clip, out float volume)
        {
            if (_map == null) BuildMap();

            Entry e;
            if (_map != null && _map.TryGetValue(id, out e) && e.clip != null)
            {
                clip = e.clip;
                volume = e.volume > 0.001f ? e.volume : 1f;
                return true;
            }

            clip = null;
            volume = 1f;
            return false;
        }

        /// <summary>把所有条目注册进 AudioService。启动时调用一次。</summary>
        public void RegisterAll()
        {
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.id == SfxId.None || e.clip == null) continue;
                AudioService.Register(e.id, e.clip);
            }
        }

        /// <summary>列出所有缺 clip 的 ID（Editor 校验与自测用）。</summary>
        public List<SfxId> CollectMissing()
        {
            var missing = new List<SfxId>();
            var seen = new HashSet<SfxId>();

            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].clip != null) seen.Add(entries[i].id);
                }
            }

            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                if (id == SfxId.None) continue;
                if (!seen.Contains(id)) missing.Add(id);
            }
            return missing;
        }

        /// <summary>Editor 工具写入用。</summary>
        public void SetEntries(Entry[] newEntries)
        {
            entries = newEntries;
            _map = null;
        }
    }
}
