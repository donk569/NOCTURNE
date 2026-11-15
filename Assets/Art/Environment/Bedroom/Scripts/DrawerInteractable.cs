using UnityEngine;

namespace NOCTURNE
{
    /// <summary>
    /// 抽屉：沿指定局部方向平滑拉出/推回，不依赖 Rigidbody。
    /// 最大拉出距离、方向、速度都能在 Inspector 调。
    /// 抽屉里的东西可以拖进 extras 跟着一起动。
    /// </summary>
    [DisallowMultipleComponent]
    public class DrawerInteractable : MonoBehaviour, IInteractable
    {
        [Header("行程")]
        [Tooltip("拉出的最大距离（米）")]
        public float openDistance = 0.30f;

        [Tooltip("拉出方向（本物体的局部方向）。例如向右拉就填 (1,0,0)")]
        public Vector3 openDirection = Vector3.left;

        [Tooltip("滑动速度（米/秒）")]
        public float speed = 0.55f;

        [Header("状态")]
        public bool isOpen = false;
        [Tooltip("上锁后不能交互")]
        public bool locked = false;

        [Header("文案")]
        public string displayName = "抽屉";

        [Header("音效（可空）")]
        public AudioSource audioSource;
        public AudioClip openClip;
        public AudioClip closeClip;

        [Header("跟随物体（抽屉里的东西）")]
        public Transform[] extras;

        Vector3 _closedLocal;
        Vector3 _openLocal;
        Vector3[] _extraClosed;
        float _t;

        void Awake()
        {
            _closedLocal = transform.localPosition;
            Vector3 dir = openDirection.sqrMagnitude > 1e-6f ? openDirection.normalized : Vector3.left;
            _openLocal = _closedLocal + dir * openDistance;
            _t = isOpen ? 1f : 0f;
            Apply(_t);

            if (extras != null && extras.Length > 0)
            {
                _extraClosed = new Vector3[extras.Length];
                for (int i = 0; i < extras.Length; i++)
                    if (extras[i] != null) _extraClosed[i] = extras[i].localPosition;
            }
        }

        void Update()
        {
            float target = isOpen ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;

            float step = openDistance > 0.0001f ? (speed / openDistance) * Time.deltaTime : 1f;
            _t = Mathf.MoveTowards(_t, target, step);
            Apply(_t);
        }

        void Apply(float t)
        {
            float eased = t * t * (3f - 2f * t);
            transform.localPosition = Vector3.Lerp(_closedLocal, _openLocal, eased);

            if (extras == null || _extraClosed == null) return;
            Vector3 delta = transform.localPosition - _closedLocal;
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i] == null) continue;
                extras[i].localPosition = _extraClosed[i] + delta;
            }
        }

        public void Toggle()
        {
            SetOpen(!isOpen);
        }

        // ---- IInteractable ----
        public string Prompt => (isOpen ? "推回" : "打开") + displayName;
        public bool CanInteract => !locked;

        public void Interact()
        {
            if (locked) return;
            Toggle();
        }

        public void SetOpen(bool open)
        {
            if (isOpen == open) return;
            isOpen = open;

            if (audioSource != null)
            {
                var clip = open ? openClip : closeClip;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
        }

        /// <summary>直接到位，跳过动画（初始化用）。</summary>
        public void SnapTo(bool open)
        {
            isOpen = open;
            _t = open ? 1f : 0f;
            Apply(_t);
        }
    }
}
