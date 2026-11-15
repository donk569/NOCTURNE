using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 尾巴的程序化摆动（含轻微惯性拖尾）。
    ///
    /// 为什么不在动画剪辑里改：尾巴骨骼在 FBX 剪辑里是静态关键帧，
    /// 重新导出一个带尾巴动画的 FBX 需要美术资源；而尾巴摆动本质是
    /// "叠加在基础姿势上的低频旋转"，在 LateUpdate 里按骨骼链叠加即可，
    /// 既不动原始模型，也能按状态（站/走/跑/睡）连续调幅度。
    ///
    /// 关键：Animator 每帧先写姿势，LateUpdate 再叠加，SkinnedMeshRenderer 渲染时才读骨骼矩阵，
    /// 所以这里写 bone.rotation 不会被动画覆盖。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatBodyMotion : MonoBehaviour
    {
        [Header("骨骼（留空则按名字自动找）")]
        public Transform[] tailBones;
        public string[] tailBoneNames = new string[] { "tail.01", "tail.02", "tail.03" };

        [Tooltip("尾巴摆动绕哪根局部轴。导出骨骼 +Y 指向尾尖，绕 Z 摆是最像左右甩的")]
        public Vector3 wagAxis = new Vector3(0f, 0f, 1f);

        [Header("幅度（度，第一段骨骼）")]
        public float idleAmplitude = 7f;
        public float walkAmplitude = 12f;
        public float runAmplitude = 18f;
        [Tooltip("睡觉时的幅度比例，要求是安静的")]
        [Range(0f, 1f)] public float sleepScale = 0.12f;

        [Header("节奏（赫兹）")]
        public float idleFrequency = 0.45f;
        public float walkFrequency = 0.95f;
        public float runFrequency = 1.5f;

        [Header("惯性")]
        [Tooltip("每往下一节，相位滞后多少（0.3~0.5 有鞭子感，接近 1 会像肉虫）")]
        public float phaseLagPerBone = 0.42f;
        [Tooltip("每往下一节，幅度放大多少倍")]
        public float amplitudeGainPerBone = 0.55f;
        [Tooltip("幅度变化的平滑速度")]
        public float amplitudeSharpness = 3.5f;

        [Header("头/身微动")]
        [Tooltip("待机时身体起伏幅度（度），实测大了就像抽搐，保持很小")]
        public float breatheAmplitude = 1.2f;
        public float breatheFrequency = 0.28f;

        Transform[] _bones;
        Transform _chest;
        float _phase;
        float _breathePhase;
        float _amp = -1f;
        float _freq = 1f;
        float _speed;
        Vector3 _lastPos;
        CatAgent _agent;

        // 叠加式旋转每帧必须恢复基准。Animator 剪辑不一定给 tail / chest 写了曲线，
        // 没写曲线时 bone.rotation 会保留上一帧叠加后的值，下一帧再叠一次 →
        // 逐帧累积成肉眼可见的"抽搐"。这是开场盯人时抖动的根因之一。
        Quaternion[] _baseRot;
        bool _baseValid;

        void Awake()
        {
            _agent = GetComponent<CatAgent>();
            var list = new System.Collections.Generic.List<Transform>();
            if (tailBones != null && tailBones.Length > 0)
            {
                for (int i = 0; i < tailBones.Length; i++)
                    if (tailBones[i] != null) list.Add(tailBones[i]);
            }
            else
            {
                var all = GetComponentsInChildren<Transform>(true);
                for (int n = 0; n < tailBoneNames.Length; n++)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].name == tailBoneNames[n]) { list.Add(all[i]); break; }
                    }
                }
            }
            _bones = list.ToArray();

            var allBones = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allBones.Length; i++)
            {
                if (allBones[i].name == "chest") { _chest = allBones[i]; break; }
            }
            if (_bones.Length == 0)
                Debug.LogWarning("[CatBodyMotion] 没找到尾巴骨骼，尾巴不会动", this);

            _lastPos = transform.position;
        }

        void Update()
        {
            // Animator 在 Update 之后、LateUpdate 之前求值。求值前先还原干净姿势，
            // 保证 LateUpdate 的叠加永远只含"这一帧的量"，不会跨帧累积。
            RestoreBase();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 p = transform.position;
            Vector3 d = p - _lastPos;
            d.y = 0f;
            _lastPos = p;
            float instant = d.magnitude / dt;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-10f * dt));
            if (instant < 0.001f && _speed < 0.02f) _speed = 0f;
        }

        void LateUpdate()
        {
            if (_bones == null || _bones.Length == 0) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 记下 Animator 本帧写出的基准姿势，随后所有叠加都以它为起点
            SaveBase();

            // ---- 目标幅度/节奏：静止 → 走路 → 小跑
            float targetAmp = idleAmplitude;
            float targetFreq = idleFrequency;

            if (_speed > 0.05f)
            {
                float k = Mathf.InverseLerp(0.12f, 0.5f, _speed);
                targetAmp = Mathf.Lerp(walkAmplitude, runAmplitude, k);
                targetFreq = Mathf.Lerp(walkFrequency, runFrequency, k);
            }

            // 睡觉/趴卧时尾巴几乎不动
            if (IsSleeping())
            {
                targetAmp *= sleepScale;
                targetFreq *= 0.6f;
            }

            if (_amp < 0f) { _amp = targetAmp; _freq = targetFreq; }
            float kk = 1f - Mathf.Exp(-amplitudeSharpness * dt);
            _amp = Mathf.Lerp(_amp, targetAmp, kk);
            _freq = Mathf.Lerp(_freq, targetFreq, kk);

            _phase += Mathf.PI * 2f * _freq * dt;
            if (_phase > Mathf.PI * 2f) _phase -= Mathf.PI * 2f;

            Vector3 axis = wagAxis.sqrMagnitude < 1e-6f ? Vector3.forward : wagAxis.normalized;

            for (int i = 0; i < _bones.Length; i++)
            {
                Transform b = _bones[i];
                if (b == null) continue;

                float phase = _phase - i * phaseLagPerBone * Mathf.PI * 2f;
                float amp = _amp * Mathf.Pow(1f + amplitudeGainPerBone, i);
                float angle = Mathf.Sin(phase) * amp;

                // 叠加式：不动基础姿势，只在其上加一段旋转
                b.rotation = b.rotation * Quaternion.AngleAxis(angle, axis);
            }

            // ---- 呼吸：胸口极轻微的起伏（不用 Y 位移，避免"上下弹跳"）
            _breathePhase += Mathf.PI * 2f * breatheFrequency * dt;
            if (_breathePhase > Mathf.PI * 2f) _breathePhase -= Mathf.PI * 2f;

            if (_chest != null && breatheAmplitude > 0.01f)
            {
                float breathe = Mathf.Sin(_breathePhase) * breatheAmplitude;
                _chest.rotation = _chest.rotation * Quaternion.AngleAxis(breathe, Vector3.right);
            }
        }

        void SaveBase()
        {
            int n = _bones.Length;
            if (_baseRot == null || _baseRot.Length != n + 1) _baseRot = new Quaternion[n + 1];
            for (int i = 0; i < n; i++)
                _baseRot[i] = _bones[i] != null ? _bones[i].rotation : Quaternion.identity;
            _baseRot[n] = _chest != null ? _chest.rotation : Quaternion.identity;
            _baseValid = true;
        }

        void RestoreBase()
        {
            if (!_baseValid || _baseRot == null || _bones == null) return;
            int n = _bones.Length;
            if (_baseRot.Length != n + 1) { _baseValid = false; return; }
            for (int i = 0; i < n; i++)
                if (_bones[i] != null) _bones[i].rotation = _baseRot[i];
            if (_chest != null) _chest.rotation = _baseRot[n];
        }

        bool IsSleeping()
        {
            if (_agent == null) return false;
            // 位置锁定 = 开场趴卧/睡在窝里，这两种情况尾巴都该安静
            return _agent.IsTransformLocked;
        }
    }
}
