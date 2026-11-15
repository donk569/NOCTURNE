using UnityEngine;
using NOCTURNE.Cat;
using NOCTURNE.Interaction;

namespace NOCTURNE
{
    /// <summary>
    /// 推拉门 / 推拉窗。和 HingedDoor 的区别是整扇板平移而不是绕合页转。
    /// 因为 FBX 里一扇门的玻璃、边框、中梃、把手是各自独立的同级物体，
    /// 这里不重新挂父级（会被下次重新导入 FBX 冲掉），而是直接拖一组 Transform，
    /// 运行时记录初始位置再整体平移。
    ///
    /// 实现 <see cref="ICatDoor"/>，但 <c>catCanOpen</c> 默认 false：
    /// 阳台门是"出房屋"的通道，猫不能从这里出去。
    /// </summary>
    [DisallowMultipleComponent]
    public class SlidingDoor : MonoBehaviour, IInteractable, ICatDoor
    {
        [Header("门扇（把这扇门的所有零件都拖进来）")]
        public Transform[] leaves;

        [Header("猫咪")]
        [Tooltip("允许猫自己拉开这扇门。阳台门/窗必须保持关闭")]
        public bool catCanOpen = false;

        [Header("位移")]
        [Tooltip("打开时的位移（世界方向）。阳台两扇通常一个 -X 一个 +X")]
        public Vector3 openOffset = new Vector3(-1.45f, 0f, 0f);
        [Tooltip("平移速度 m/s")]
        public float speed = 1.1f;
        [Tooltip("全开/全关时的轻微顿感，0 = 干脆")]
        [Range(0f, 0.3f)] public float easeDamp = 0.12f;

        [Header("状态")]
        public bool isOpen = false;
        public bool locked = false;
        public string displayName = "推拉门";

        [Header("音效（可空）")]
        public AudioSource audioSource;
        public AudioClip openClip;
        public AudioClip closeClip;

        Vector3[] _basePos;
        float _t;          // 0 = 关，1 = 开

        public string Prompt => (isOpen ? "关上" : "拉开") + " " + displayName;
        public bool CanInteract => !locked;

        void Awake()
        {
            CacheBase();
            _t = isOpen ? 1f : 0f;
            Apply(_t);
        }

        void CacheBase()
        {
            if (leaves == null) return;
            _basePos = new Vector3[leaves.Length];
            for (int i = 0; i < leaves.Length; i++)
                _basePos[i] = leaves[i] != null ? leaves[i].position : Vector3.zero;
        }

        void Update()
        {
            float target = isOpen ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;

            float delta = speed / Mathf.Max(0.05f, openOffset.magnitude) * Time.deltaTime;
            _t = Mathf.MoveTowards(_t, target, Mathf.Max(delta, 0.0001f));
            Apply(_t);
        }

        void Apply(float t)
        {
            if (_basePos == null) return;
            float e = easeDamp <= 0.0001f ? t : EaseOut(t, easeDamp);
            for (int i = 0; i < leaves.Length; i++)
            {
                if (leaves[i] == null) continue;
                leaves[i].position = _basePos[i] + openOffset * e;
            }
        }

        /// <summary>起步/收尾稍微软一点，但不改变总行程</summary>
        static float EaseOut(float t, float damp)
        {
            return Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), Mathf.Clamp01(damp * 4f));
        }

        public void Toggle() { SetOpen(!isOpen); }

        public void Interact()
        {
            if (locked) return;
            Toggle();
        }

        public void SetOpen(bool open)
        {
            isOpen = open;
            if (audioSource != null)
            {
                var clip = open ? openClip : closeClip;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
        }

        public void SnapTo(bool open)
        {
            isOpen = open;
            _t = open ? 1f : 0f;
            Apply(_t);
        }

        // ---- ICatDoor（猫只读这几个，开关仍然走上面的 SetOpen） ----

        public Transform DoorTransform { get { return transform; } }

        /// <summary>逻辑开关状态（字段 isOpen 的只读出口，给猫用）。</summary>
        public bool IsOpen { get { return isOpen; } }

        public bool CatCanOpen { get { return catCanOpen && !locked; } }

        public float OpenProgress { get { return _t; } }

        /// <summary>开到位：逻辑上开着，而且平移已经到终点。猫只认这个。</summary>
        public bool IsFullyOpen { get { return isOpen && _t >= 0.98f; } }
    }
}
