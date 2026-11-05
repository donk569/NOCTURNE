using UnityEngine;

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
    /// </summary>
    [DisallowMultipleComponent]
    public class HingedDoor : MonoBehaviour, IInteractable
    {
        [Header("角度")]
        [Tooltip("相对初始姿态打开多少度。正负决定往哪边开")]
        public float openDelta = 65f;

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
        Vector3 _hinge;                    // 世界坐标合页点
        Vector3[] _extraPos;
        Quaternion[] _extraRot;

        void Awake()
        {
            _baseY = transform.localEulerAngles.y;
            _basePos = transform.position;
            _hinge = _basePos + pivotOffset;
            CacheExtras();
            _current = _baseY;
            _target = _baseY + (isOpen ? openDelta : 0f);
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
            var e = transform.localEulerAngles;
            e.y = y;
            transform.localEulerAngles = e;

            float delta = Mathf.DeltaAngle(_baseY, y);
            Quaternion q = Quaternion.AngleAxis(delta, Vector3.up);

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
        public string Prompt => (isOpen ? "关上" : "打开") + " " + displayName;
        public bool CanInteract => !locked;

        public void Interact()
        {
            if (locked) return;
            Toggle();
        }

        public void SetOpen(bool open)
        {
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
            isOpen = open;
            _current = _target = _baseY + (open ? openDelta : 0f);
            Apply(_current);
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
