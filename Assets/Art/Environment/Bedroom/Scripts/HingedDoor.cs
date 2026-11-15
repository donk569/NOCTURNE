using UnityEngine;
using NOCTURNE.Cat;

namespace NOCTURNE
{
    /// <summary>
    /// 可开关的门 / 柜门。
    /// 两种用法：
    /// 1. 模型原点已经在合页上（卧室门、衣柜门）→ pivotOffset 留零。
    /// 2. 模型原点在门板中心（客厅柜门等）→ 把 pivotOffset 填成
    ///    「从物体原点指向合页」的世界偏移（例如 (0,0,+0.26)），
    ///    开门时会绕那个点转，门板不会原地打转。
    /// knobs 这类跟着门走的小零件拖进 extras，会做同样的刚体运动。
    /// 用法：把 openDelta 调成合适的角度，调用 Toggle() 或 Interact()。
    ///
    /// 另外实现了 <see cref="ICatDoor"/>，让猫能把这扇门当成可通行的门洞。
    /// 猫用的接口定义在 NOCTURNE.Cat 程序集里，这样引用方向是 Assembly-CSharp → NOCTURNE.Cat，
    /// 不会和猫的程序集互相依赖。
    /// </summary>
    [DisallowMultipleComponent]
    public class HingedDoor : MonoBehaviour, IInteractable, ICatDoor
    {
        [Header("角度")]
        [Tooltip("相对初始姿态打开多少度。正负决定往哪边开")]
        public float openDelta = 65f;

        [Header("猫咪")]
        [Tooltip("允许猫自己推开这扇门。室内门勾上；柜门、装饰门取消")]
        public bool catCanOpen = true;

        [Header("合页")]
        [Tooltip("从门板原点指向合页的世界偏移。原点在合页上就留零")]
        public Vector3 pivotOffset = Vector3.zero;
        [Tooltip("跟着门一起动的小零件（把手、铰链盖等），可选")]
        public Transform[] extras;

        [Header("运动")]
        [Tooltip("角速度（度/秒）")]
        public float speed = 220f;
        [Tooltip("关到头时的回弹量，模拟柜门碰撞")]
        public float settleOvershoot = 4f;

        [Header("状态")]
        public bool isOpen = false;
        [Tooltip("上锁后不能被交互（准星不显示提示）")]
        public bool locked = false;

        [Header("文案")]
        [Tooltip("准星旁提示的动词，会拼成「打开 卧室门」")]
        public string displayName = "门";

        [Header("音效（可空）")]
        public AudioSource audioSource;
        public AudioClip openClip;
        public AudioClip closeClip;

        float _baseY;
        float _current;
        float _target;
        Vector3 _basePos;
        Quaternion _baseRot;               // 关闭姿态的完整朝向（四元数，避免欧拉角歧义）
        Vector3 _hinge;                    // 世界坐标合页点
        Vector3[] _extraPos;
        Quaternion[] _extraRot;
        bool _inited;

        /// <summary>
        /// 记录关闭基准。Awake 会调用；编辑器里没走 Awake 时由 SetOpen/SnapTo 兜底调用，
        /// 否则 _baseRot 还是默认 identity，一调 SnapTo 就会把带 270° X 旋转的门板转平。
        /// </summary>
        void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            _baseY = transform.localEulerAngles.y;
            _basePos = transform.position;
            _baseRot = transform.rotation;
            _hinge = _basePos + pivotOffset;
            CacheExtras();
            _current = _baseY;
            _target = _baseY + (isOpen ? openDelta : 0f);
        }

        void Awake()
        {
            EnsureInit();
            Apply(_current);
        }

        void CacheExtras()
        {
            if (extras == null) return;
            _extraPos = new Vector3[extras.Length];
            _extraRot = new Quaternion[extras.Length];
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i] == null) continue;
                _extraPos[i] = extras[i].position;
                _extraRot[i] = extras[i].rotation;
            }
        }

        void Update()
        {
            if (Mathf.Abs(Mathf.DeltaAngle(_current, _target)) < 0.05f) return;
            _current = Mathf.MoveTowardsAngle(_current, _target, speed * Time.deltaTime);
            Apply(_current);
        }

        void Apply(float y)
        {
            // 不写 localEulerAngles：门板本身可能带 270° 的 X 旋转，
            // 读回欧拉角再改 Y 会产生歧义（关到头时漂移几度，留下缝隙）。
            // 这里统一用四元数：关闭姿态 = _baseRot，开门 = 绕世界 Y 转 delta。
            float delta = Mathf.DeltaAngle(_baseY, y);
            Quaternion q = Quaternion.AngleAxis(delta, Vector3.up);
            transform.rotation = q * _baseRot;

            // 门板原点绕合页转：O' = hinge + R * (basePos - hinge)
            if (pivotOffset.sqrMagnitude > 1e-8f)
                transform.position = _hinge + q * (_basePos - _hinge);

            if (extras == null) return;
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i] == null) continue;
                extras[i].position = _hinge + q * (_extraPos[i] - _hinge);
                extras[i].rotation = q * _extraRot[i];
            }
        }

        public void Toggle()
        {
            SetOpen(!isOpen);
        }

        // ---- IInteractable ----
        public string Prompt => (isOpen ? "关上" : "打开") + displayName;
        public bool CanInteract => !locked;

        public void Interact()
        {
            if (locked) return;
            Toggle();
        }

        public void SetOpen(bool open)
        {
            EnsureInit();
            isOpen = open;
            _target = _baseY + (open ? openDelta : 0f);
            if (audioSource != null)
            {
                var clip = open ? openClip : closeClip;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
        }

        /// <summary>直接设成打开角度，跳过动画（初始化场景用）</summary>
        public void SnapTo(bool open)
        {
            EnsureInit();
            isOpen = open;
            _current = _target = _baseY + (open ? openDelta : 0f);
            Apply(_current);
        }

        // ---- ICatDoor（猫只读这几个，开关仍然走上面的 SetOpen） ----

        public Transform DoorTransform { get { return transform; } }

        /// <summary>逻辑开关状态（字段 isOpen 的只读出口，给猫用）。</summary>
        public bool IsOpen { get { return isOpen; } }

        /// <summary>上锁的门猫也打不开。</summary>
        public bool CatCanOpen { get { return catCanOpen && !locked; } }

        public float OpenProgress
        {
            get
            {
                if (!_inited) return isOpen ? 1f : 0f;
                float span = Mathf.Max(0.01f, Mathf.Abs(openDelta));
                return Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(_baseY, _current)) / span);
            }
        }

        /// <summary>开到位：逻辑上是开着的，而且插值已经停下来。猫只认这个。</summary>
        public bool IsFullyOpen
        {
            get
            {
                if (!isOpen || !_inited) return false;
                return Mathf.Abs(Mathf.DeltaAngle(_current, _target)) < 0.75f;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            Gizmos.DrawRay(transform.position, transform.right * 0.6f);
            if (pivotOffset.sqrMagnitude > 1e-8f)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(transform.position + transform.rotation * pivotOffset, 0.02f);
            }
        }
    }
}
