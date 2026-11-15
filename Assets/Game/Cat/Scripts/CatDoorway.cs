using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 从 <see cref="ICatDoor"/> 抓出来的几何信息：门平面、门宽、合页位置。
    ///
    /// 只在 <see cref="Capture"/> 时读一次 Renderer 包围盒（此刻门是关着的），
    /// 之后不再依赖门板实时姿态，避免开门动画把平面算歪。
    /// </summary>
    public sealed class CatDoorway
    {
        public readonly ICatDoor Door;

        /// <summary>合页/门扇原点（世界坐标）。铰链门的回转中心就在这。</summary>
        public Vector3 Pivot;

        /// <summary>门洞中心（世界坐标，Y 取合页高度）。</summary>
        public Vector3 Center;

        /// <summary>门平面法线（水平，指向门洞两侧）。</summary>
        public Vector3 Normal = Vector3.forward;

        /// <summary>门宽方向（水平）。</summary>
        public Vector3 WidthAxis = Vector3.right;

        /// <summary>门板宽度（米）。</summary>
        public float Width = 1f;

        /// <summary>门板厚度（米），用于判定"路径是否穿过门平面"。</summary>
        public float Thickness = 0.05f;

        /// <summary>猫能不能自己开这扇门（设计师开关 + 宽度合理性）。</summary>
        public bool Passable;

        /// <summary>
        /// 开门期间猫要退到距合页这个半径之外，否则会被门板扫到。
        /// 铰链门扫过的最大半径就是门宽，多留 0.3m 余量。
        /// </summary>
        public float SafeRadius { get { return Width + 0.3f; } }

        /// <summary>门板上的碰撞体（缓存，用于"这一步会不会撞到门板"的物理判定）。</summary>
        Collider[] _leafColliders;
        readonly Collider[] _overlap = new Collider[16];

        public CatDoorway(ICatDoor door)
        {
            Door = door;
            Capture();
            CacheColliders();
        }

        void CacheColliders()
        {
            if (Door == null || Door.DoorTransform == null)
            {
                _leafColliders = new Collider[0];
                return;
            }
            _leafColliders = Door.DoorTransform.GetComponentsInChildren<Collider>(true);
        }

        /// <summary>
        /// 这个位置（猫体积）是否被门板占住。
        ///
        /// 为什么不只靠"线段穿门平面"：门板是有厚度的实体，猫在门附近贴边、绕角、
        /// 或者门正在转的时候，平面判定会漏；直接用门板碰撞体做重叠检测最可靠，
        /// 也是"卡在门里面"这个问题的兜底。
        /// </summary>
        public bool Blocks(Vector3 p, float radius, float height)
        {
            if (_leafColliders == null || _leafColliders.Length == 0) return false;

            float r = Mathf.Max(0.05f, radius);
            float h = Mathf.Max(r * 2f + 0.02f, height);
            Vector3 p0 = new Vector3(p.x, p.y + r + 0.03f, p.z);
            Vector3 p1 = new Vector3(p.x, p.y + Mathf.Max(r + 0.03f, h - r), p.z);

            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r, _overlap, ~0,
                                                   QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _overlap[i];
                if (c == null) continue;
                for (int k = 0; k < _leafColliders.Length; k++)
                {
                    if (_leafColliders[k] == c) return true;
                }
            }
            return false;
        }

        /// <summary>重新采样门板几何（场景初始姿态下调用最准）。</summary>
        public void Capture()
        {
            if (Door == null || Door.DoorTransform == null) return;

            Pivot = Door.DoorTransform.position;

            Bounds b;
            if (!TryGetWorldBounds(Door.DoorTransform, out b))
            {
                Width = 1f;
                Thickness = 0.05f;
                Center = Pivot;
                Passable = false;
                return;
            }

            Vector3 size = b.size;
            bool alongX = size.x >= size.z;

            Width = alongX ? size.x : size.z;
            Thickness = Mathf.Max(0.02f, alongX ? size.z : size.x);
            WidthAxis = alongX ? Vector3.right : Vector3.forward;
            Normal = alongX ? Vector3.forward : Vector3.right;
            Center = new Vector3(b.center.x, Pivot.y, b.center.z);
        }

        static bool TryGetWorldBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds(root.position, Vector3.zero);
            bool any = false;
            var rs = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled) continue;
                if (!any) { bounds = rs[i].bounds; any = true; }
                else bounds.Encapsulate(rs[i].bounds);
            }
            return any;
        }

        /// <summary>线段 a→b 是否从这扇门的门洞里穿过去。</summary>
        public bool Crosses(Vector3 a, Vector3 b, float lateralPadding)
        {
            float da = Vector3.Dot(a - Center, Normal);
            float db = Vector3.Dot(b - Center, Normal);
            if (da * db > 0f) return false;

            float denom = da - db;
            if (Mathf.Abs(denom) < 1e-5f) return false;

            float t = da / denom;
            Vector3 p = Vector3.Lerp(a, b, t);
            float lateral = Vector3.Dot(p - Center, WidthAxis);
            return Mathf.Abs(lateral) <= Width * 0.5f + lateralPadding;
        }

        /// <summary>把点拉到合页半径之外（开门动画期间用，防止被门板扫到）。</summary>
        public Vector3 PushOutsideSwing(Vector3 p)
        {
            Vector3 rel = p - Pivot;
            rel.y = 0f;
            float d = rel.magnitude;
            if (d >= SafeRadius || d < 1e-4f) return p;
            Vector3 q = Pivot + rel / d * SafeRadius;
            q.y = p.y;
            return q;
        }
    }
}
