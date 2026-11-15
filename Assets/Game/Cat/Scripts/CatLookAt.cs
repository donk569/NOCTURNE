using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 让猫头（和脖子）追随目标。用于制造「这只猫一直在看你」的恐怖感。
    ///
    /// 为什么不用 Animator IK：本角色是 Generic 骨架（非 Humanoid），
    /// <c>OnAnimatorIK</c> 不会回调。所以改成在 LateUpdate 里
    /// 直接叠加头部骨骼的世界旋转——Animator 在动画阶段已经写完姿势，
    /// LateUpdate 里覆盖是安全的，且 SkinnedMeshRenderer 在渲染时才读骨骼矩阵。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatLookAt : MonoBehaviour
    {
        [Tooltip("注视目标，留空则自动取 CatAgent 的 lookTarget")]
        public Transform target;

        [Tooltip("参与注视的骨骼名（从根部算起，按顺序叠加）")]
        public string[] boneNames = new string[] { "neck", "head" };

        [Tooltip("每段骨骼最大偏转角（度）")]
        public float maxAnglePerBone = 42f;

        [Tooltip("头部骨骼长轴（局部坐标系）。导出后骨骼 +Y 指向头骨→鼻尖")]
        public Vector3 boneForwardAxis = Vector3.up;

        [Tooltip("转动平滑，越大越跟手")]
        public float sharpness = 9f;

        [Tooltip("注视目标位置的平滑强度。玩家相机有呼吸晃动，1:1 跟随会让头在贴脸距离上一直抖")]
        public float targetSharpness = 3.2f;

        [Tooltip("超过这个距离就不看了（米）")]
        public float maxDistance = 9f;

        [Tooltip("关掉可临时停止注视")]
        public bool enableLook = true;

        [Header("凝视时的细微动作（让它不像一尊雕像）")]
        [Tooltip("歪头幅度（度），绕头骨局部 Z 轴。开场近距离凝视时最出效果")]
        public float headTiltDegrees = 0f;

        [Tooltip("歪头的缓慢摆幅（度），0 = 固定歪着")]
        public float headTiltSwayDegrees = 2.2f;

        [Tooltip("歪头摆动频率（赫兹），要很慢")]
        public float headTiltSwaySpeed = 0.13f;

        [Tooltip("点头/前探的微动幅度（度），绕头骨局部 X 轴")]
        public float headNodDegrees = 1.8f;

        [Tooltip("点头微动频率（赫兹）")]
        public float headNodSpeed = 0.27f;

        [Tooltip("微动受速度影响：有速度时减小，避免跑动时头在抖")]
        [Range(0f, 1f)] public float motionDamp = 0.85f;

        Transform[] _bones;
        CatAgent _agent;
        float _microTime;
        Vector3 _lastPos;
        bool _hasLast;
        float _speed;
        Vector3 _smoothedTarget;
        bool _targetInit;

        // 与 CatBodyMotion 同理：注视和歪头/点头都是叠加式旋转，
        // 动画剪辑未必给 neck / head 写了曲线，不恢复基准就会逐帧累积成抽搐。
        Quaternion[] _baseRot;
        bool _baseValid;

        void Awake()
        {
            _agent = GetComponent<CatAgent>();
            var list = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < boneNames.Length; i++)
            {
                var t = FindDeep(transform, boneNames[i]);
                if (t != null) list.Add(t);
            }
            _bones = list.ToArray();
        }

        void Start()
        {
            if (target == null && _agent != null) target = _agent.lookTarget;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            return null;
        }

        void Update()
        {
            // Animator 求值之前还原干净姿势，杜绝跨帧累积
            RestoreBase();
        }

        void LateUpdate()
        {
            if (_bones == null || _bones.Length == 0) return;
            SaveBase();
            if (!enableLook || target == null) return;

            Transform last = _bones[_bones.Length - 1];
            if (last == null) return;

            // 目标位置先低通：玩家躺卧时相机在呼吸（±6mm、0.5Hz），
            // 贴脸 0.36m 时哪怕 1mm 的抖动都会被放大成"头一直在抽"。
            Vector3 rawTarget = target.position;
            if (!_targetInit) { _smoothedTarget = rawTarget; _targetInit = true; }
            float tk = 1f - Mathf.Exp(-Mathf.Max(0.2f, targetSharpness) * Time.deltaTime);
            _smoothedTarget = Vector3.Lerp(_smoothedTarget, rawTarget, tk);

            Vector3 to = _smoothedTarget - last.position;
            if (to.sqrMagnitude > maxDistance * maxDistance) return;
            if (to.sqrMagnitude < 1e-6f) return;
            to.Normalize();

            // 从根部往头部逐段叠加，天然分散角度、不会把脖子拧断
            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;

                Vector3 cur = bone.TransformDirection(boneForwardAxis);
                Quaternion delta = Quaternion.FromToRotation(cur, to);

                float ang = Quaternion.Angle(Quaternion.identity, delta);
                if (ang > maxAnglePerBone && ang > 1e-3f)
                    delta = Quaternion.Slerp(Quaternion.identity, delta, maxAnglePerBone / ang);

                Quaternion want = delta * bone.rotation;
                bone.rotation = Quaternion.Slerp(bone.rotation, want,
                    1f - Mathf.Exp(-sharpness * Time.deltaTime));
            }

            ApplyMicroMotion(_bones[_bones.Length - 1]);
        }

        void SaveBase()
        {
            int n = _bones.Length;
            if (_baseRot == null || _baseRot.Length != n) _baseRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
                _baseRot[i] = _bones[i] != null ? _bones[i].rotation : Quaternion.identity;
            _baseValid = true;
        }

        void RestoreBase()
        {
            if (!_baseValid || _baseRot == null || _bones == null) return;
            if (_baseRot.Length != _bones.Length) { _baseValid = false; return; }
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].rotation = _baseRot[i];
        }

        /// <summary>
        /// 在注视之上叠一层很慢的歪头/点头。速度越快减得越多，
        /// 否则跑动时头会跟着抖，看起来像抽搐。
        /// </summary>
        void ApplyMicroMotion(Transform head)
        {
            if (head == null) return;
            if (headTiltSwayDegrees < 0.01f && headTiltDegrees < 0.01f && headNodDegrees < 0.01f) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _microTime += dt;

            Vector3 p = transform.position;
            if (!_hasLast) { _lastPos = p; _hasLast = true; }
            Vector3 d = p - _lastPos;
            d.y = 0f;
            _lastPos = p;
            _speed = Mathf.Lerp(_speed, d.magnitude / dt, 1f - Mathf.Exp(-8f * dt));
            float damp = Mathf.Lerp(1f, 1f - motionDamp, Mathf.InverseLerp(0.05f, 0.5f, _speed));

            float tilt = headTiltDegrees
                       + Mathf.Sin(_microTime * Mathf.PI * 2f * headTiltSwaySpeed) * headTiltSwayDegrees;
            float nod = Mathf.Sin(_microTime * Mathf.PI * 2f * headNodSpeed) * headNodDegrees;

            head.rotation = head.rotation
                          * Quaternion.AngleAxis(tilt * damp, Vector3.forward)
                          * Quaternion.AngleAxis(nod * damp, Vector3.right);
        }
    }
}
