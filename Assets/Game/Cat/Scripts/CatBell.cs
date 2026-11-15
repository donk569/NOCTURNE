using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 脖子上的铃铛：只要猫在移动就会间隔性地响，停下就停。
    ///
    /// 为什么用"按走过的距离"而不是"按时间"：
    /// 按时间的话，猫走得快慢不同，铃声节奏却一样，听起来像节拍器；
    /// 按距离可以让"跑起来叮当更密"自然发生，而且静止时不会响。
    ///
    /// 距离衰减交给 3D AudioSource（spatialBlend = 1），本脚本只负责"什么时候响、响多轻"。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatBell : MonoBehaviour
    {
        [Header("音频")]
        [Tooltip("必须是 3D（spatialBlend = 1）的 AudioSource。留空则用同物体上的")]
        public AudioSource source;

        [Tooltip("铃铛音效，会随机挑一个播放，避免每次都一模一样")]
        public AudioClip[] clips;

        [Tooltip("整体音量")]
        [Range(0f, 1f)] public float volume = 0.5f;

        [Tooltip("多普勒/距离衰减用：玩家离多近时是满音量")]
        public float minDistance = 0.9f;

        [Tooltip("超过这个距离基本听不见")]
        public float maxDistance = 8f;

        [Header("响铃节奏（按行走距离）")]
        [Tooltip("普通走路时，每走这么远响一次（米）")]
        public float walkRingDistance = 0.62f;

        [Tooltip("小跑时，每走这么远响一次（米）。要明显小于走路，听起来才急")]
        public float runRingDistance = 0.34f;

        [Tooltip("超过这个速度就算小跑（米/秒）")]
        public float runSpeedThreshold = 0.38f;

        [Tooltip("两次铃声的最小间隔，防止高速时糊成一片噪音")]
        public float minInterval = 0.17f;

        [Tooltip("低于这个速度就不算在动（米/秒），避免原地微抖也响")]
        public float moveThreshold = 0.045f;

        [Header("音色微调")]
        [Tooltip("每次响的音高随机范围，越小越机械")]
        public Vector2 pitchJitter = new Vector2(0.94f, 1.06f);

        [Tooltip("跑动时音量放大倍数")]
        public float runVolumeBoost = 1.25f;

        [Header("调试")]
        public bool logRings;

        /// <summary>累计响铃次数（调试/自测用）。</summary>
        public static int TotalRings;

        float _speed;
        float _travel;
        float _lastRingTime = -10f;
        Vector3 _lastPos;
        bool _hasLast;

        void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source != null)
            {
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = Mathf.Max(0.05f, minDistance);
                source.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
                source.dopplerLevel = 0.35f;
            }
            _lastPos = transform.position;
            _hasLast = true;
        }

        void OnEnable()
        {
            _hasLast = false;
            _travel = 0f;
            _speed = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = transform.position;

            if (!_hasLast)
            {
                _lastPos = pos;
                _hasLast = true;
                return;
            }

            // 只算水平位移：猫上下跳不该被当成"走了很远"
            Vector3 d = pos - _lastPos;
            d.y = 0f;
            float step = d.magnitude;
            _lastPos = pos;

            float instant = step / dt;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-12f * dt));

            if (_speed < moveThreshold)
            {
                // 停下就把进度慢慢吐掉，避免停下来那一刻补响一声
                _travel = Mathf.Max(0f, _travel - dt * 0.6f);
                return;
            }

            float k = Mathf.InverseLerp(runSpeedThreshold * 0.6f, runSpeedThreshold, _speed);
            float threshold = Mathf.Lerp(walkRingDistance, runRingDistance, k);
            _travel += step;

            if (_travel < threshold) return;
            if (Time.time - _lastRingTime < minInterval) return;

            _travel = 0f;
            _lastRingTime = Time.time;
            Ring(k);
        }

        void Ring(float runAmount)
        {
            if (source == null || clips == null || clips.Length == 0) return;

            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;

            source.pitch = Random.Range(pitchJitter.x, pitchJitter.y);
            float v = volume * Mathf.Lerp(1f, runVolumeBoost, Mathf.Clamp01(runAmount));
            source.PlayOneShot(clip, Mathf.Clamp01(v));
            TotalRings++;

            if (logRings)
                Debug.Log("[CatBell] ring v=" + v.ToString("F2") + " speed=" + _speed.ToString("F2"), this);
        }
    }
}
