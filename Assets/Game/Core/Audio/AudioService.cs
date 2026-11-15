using System.Collections.Generic;
using UnityEngine;
using NOCTURNE.Core.Diag;

namespace NOCTURNE.Core.Audio
{
    /// <summary>
    /// 全项目唯一的播放入口。**禁止**再在业务脚本里直接 `AudioSource.PlayOneShot`。
    ///
    /// 解决三个具体问题（对应审计里的 P3-5）：
    /// 1. 15 个 AudioSource 各自为政，13 个序列化 clip 为空，Inspector 里看不出归属；
    /// 2. 没有分组音量，做不了"静音全部"或"暂停时淡出环境音"；
    /// 3. 音源数量随场景增长，没人管上限。
    ///
    /// 设计取舍：
    /// - **池化 24 个 AudioSource**：超出时抢"离监听者最远"的那个（远处的声音先丢，听感损失最小）。
    /// - **clip 由 <see cref="AudioCatalog"/> 注册**，代码里只出现 <see cref="SfxId"/>，不直接引用资源。
    /// - **懒创建 + DontDestroyOnLoad**：任何场景第一次调用时自动建，不依赖场景里预先摆好 GameRoot。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        const string LogTag = "Audio";

        public static AudioService Instance { get; private set; }

        [Header("池")]
        [Tooltip("同时可播放的音源上限。超出时抢占离监听者最远的一个")]
        [SerializeField] int poolSize = 24;

        [Header("分组音量（0~1）")]
        [SerializeField, Range(0f, 1f)] float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;
        [SerializeField, Range(0f, 1f)] float ambienceVolume = 1f;
        [SerializeField, Range(0f, 1f)] float voiceVolume = 1f;
        [SerializeField, Range(0f, 1f)] float uiVolume = 1f;

        struct Voice
        {
            public AudioSource source;
            public SoundGroup group;
            public float baseVolume;
        }

        Voice[] _voices;
        readonly Dictionary<SfxId, AudioClip> _clips = new Dictionary<SfxId, AudioClip>();
        readonly List<string> _missingClips = new List<string>();
        bool _poolBuilt;

        // ---------------------------------------------------------------- 生命周期

        void Awake()
        {
            InitializeNow();
        }

        /// <summary>
        /// 真正的初始化。抽出来是因为 <see cref="Ensure"/> 在极早期调用时，
        /// <c>AddComponent</c> 触发的 <c>Awake</c> 可能还没跑（编辑器下根本不跑），
        /// 那时 <see cref="Instance"/> 仍是 null，后续调用会静默丢失。
        /// </summary>
        public void InitializeNow()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null, false);
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            BuildPool();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>保证实例存在。任何静态入口都会先走这里。</summary>
        static AudioService Ensure()
        {
            if (Instance != null) return Instance;

            // 场景里可能已有实例但还没初始化（编辑器下 AddComponent 不跑 Awake）
            var existing = FindObjectOfType<AudioService>();
            if (existing != null)
            {
                if (Instance == null) existing.InitializeNow();
                return Instance;
            }

            var go = new GameObject("[AudioService]");
            var svc = go.AddComponent<AudioService>();
            if (Instance == null) svc.InitializeNow();
            return Instance;
        }

        void BuildPool()
        {
            if (_poolBuilt) return;
            _poolBuilt = true;

            int n = Mathf.Max(1, poolSize);
            _voices = new Voice[n];

            for (int i = 0; i < n; i++)
            {
                var child = new GameObject("voice_" + i.ToString("00"));
                child.transform.SetParent(transform, false);

                var src = child.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 1f;          // 默认 3D，2D 播放时单独改
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 1f;
                src.maxDistance = 18f;
                src.dopplerLevel = 0f;          // 恐怖游戏不需要多普勒
                src.volume = 0f;

                _voices[i] = new Voice { source = src, group = SoundGroup.Sfx, baseVolume = 1f };
            }
        }

        // ---------------------------------------------------------------- 注册

        /// <summary>由 <see cref="AudioCatalog"/> 在启动时调用，把 SfxId 绑定到实际 clip。</summary>
        public static void Register(SfxId id, AudioClip clip)
        {
            if (clip == null) return;
            Ensure()._clips[id] = clip;
        }

        /// <summary>批量注册，供 Catalog 用。</summary>
        public void RegisterRange(IEnumerable<KeyValuePair<SfxId, AudioClip>> pairs)
        {
            if (pairs == null) return;
            foreach (var kv in pairs)
                if (kv.Value != null) _clips[kv.Key] = kv.Value;
        }

        /// <summary>某个 ID 是否已有 clip（自测/校验用）。</summary>
        public static bool HasClip(SfxId id)
        {
            var svc = Ensure();
            return svc != null && svc._clips.ContainsKey(id);
        }

        // ---------------------------------------------------------------- 播放

        /// <summary>播放 2D 音效（界面音、猫叫这种不需要方位的）。</summary>
        public static void Play(SfxId id, float volume = 1f)
        {
            PlayInternal(id, SoundGroup.Sfx, null, Vector3.zero, volume, false);
        }

        /// <summary>播放 3D 音效，跟随某个 Transform（猫铃铛、门）。</summary>
        public static void PlayAt(Transform follow, SfxId id, float volume = 1f)
        {
            PlayInternal(id, SoundGroup.Sfx, follow, follow != null ? follow.position : Vector3.zero, volume, true);
        }

        /// <summary>播放 3D 音效，固定在某个世界坐标（一次性撞击声）。</summary>
        public static void PlayAt(Vector3 position, SfxId id, float volume = 1f)
        {
            PlayInternal(id, SoundGroup.Sfx, null, position, volume, true);
        }

        /// <summary>指定分组的播放入口（猫叫走 Voice，环境音走 Ambience）。</summary>
        public static void Play(SfxId id, SoundGroup group, float volume = 1f)
        {
            PlayInternal(id, group, null, Vector3.zero, volume, false);
        }

        /// <summary>指定分组的 3D 播放。</summary>
        public static void PlayAt(Transform follow, SfxId id, SoundGroup group, float volume = 1f)
        {
            PlayInternal(id, group, follow, follow != null ? follow.position : Vector3.zero, volume, true);
        }

        static void PlayInternal(SfxId id, SoundGroup group, Transform follow,
                                 Vector3 position, float volume, bool spatial)
        {
            if (id == SfxId.None) return;

            var svc = Ensure();
            if (svc == null) return;

            AudioClip clip;
            if (!svc._clips.TryGetValue(id, out clip) || clip == null)
            {
                string key = id.ToString();
                if (!svc._missingClips.Contains(key))
                {
                    svc._missingClips.Add(key);
                    Log.Warn(LogTag, "SfxId " + key + " 没有登记 clip（检查 AudioCatalog）");
                }
                return;
            }

            int slot = svc.FindSlot();
            if (slot < 0) return;

            var v = svc._voices[slot];
            var src = v.source;

            src.Stop();
            src.clip = clip;
            src.transform.position = position;
            src.spatialBlend = spatial ? 1f : 0f;
            src.loop = false;

            v.group = group;
            v.baseVolume = Mathf.Clamp01(volume);
            svc._voices[slot] = v;

            src.volume = svc.ResolveVolume(v);
            src.Play();
        }

        /// <summary>找一个可用声源：优先空闲，其次抢离监听者最远的。</summary>
        int FindSlot()
        {
            if (_voices == null || _voices.Length == 0) return -1;

            for (int i = 0; i < _voices.Length; i++)
                if (!_voices[i].source.isPlaying) return i;

            // 全忙 —— 抢最远的
            Vector3 listen = ListenerPosition();
            int worst = 0;
            float worstDist = -1f;
            for (int i = 0; i < _voices.Length; i++)
            {
                float d = (_voices[i].source.transform.position - listen).sqrMagnitude;
                if (d > worstDist) { worstDist = d; worst = i; }
            }
            return worst;
        }

        static Vector3 ListenerPosition()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        float ResolveVolume(Voice v)
        {
            return v.baseVolume * GroupVolume(v.group) * Mathf.Clamp01(masterVolume);
        }

        // ---------------------------------------------------------------- 音量

        public static float GetVolume(SoundGroup group)
        {
            var svc = Ensure();
            return svc != null ? svc.GroupVolume(group) : 0f;
        }

        public static void SetVolume(SoundGroup group, float value)
        {
            var svc = Ensure();
            if (svc == null) return;

            value = Mathf.Clamp01(value);
            switch (group)
            {
                case SoundGroup.Master: svc.masterVolume = value; break;
                case SoundGroup.Sfx: svc.sfxVolume = value; break;
                case SoundGroup.Ambience: svc.ambienceVolume = value; break;
                case SoundGroup.Voice: svc.voiceVolume = value; break;
                case SoundGroup.Ui: svc.uiVolume = value; break;
            }

            // 正在播的声音要立刻跟着变（"静音全部"必须当场生效）
            svc.RefreshLiveVolumes();
        }

        public static float GetMaster() { var s = Ensure(); return s != null ? s.masterVolume : 0f; }
        public static void SetMaster(float v) { SetVolume(SoundGroup.Master, v); }

        float GroupVolume(SoundGroup group)
        {
            switch (group)
            {
                case SoundGroup.Master: return 1f;      // Master 单独乘，不参与分组
                case SoundGroup.Sfx: return sfxVolume;
                case SoundGroup.Ambience: return ambienceVolume;
                case SoundGroup.Voice: return voiceVolume;
                case SoundGroup.Ui: return uiVolume;
                default: return 1f;
            }
        }

        void RefreshLiveVolumes()
        {
            if (_voices == null) return;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].source.isPlaying) continue;
                _voices[i].source.volume = ResolveVolume(_voices[i]);
            }
        }

        // ---------------------------------------------------------------- 诊断

        /// <summary>当前正在播放的声源数（自测/性能面板用）。</summary>
        public int ActiveVoiceCount
        {
            get
            {
                if (_voices == null) return 0;
                int n = 0;
                for (int i = 0; i < _voices.Length; i++)
                    if (_voices[i].source.isPlaying) n++;
                return n;
            }
        }

        /// <summary>池容量。</summary>
        public int PoolSize { get { return _voices != null ? _voices.Length : 0; } }

        /// <summary>已登记的 clip 数量。</summary>
        public int RegisteredClipCount { get { return _clips.Count; } }
    }
}
