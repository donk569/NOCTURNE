using System.Collections.Generic;
using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 室内可行走网格（代替 NavMesh）。
    ///
    /// 背景：本项目 <c>NavMeshSettings.m_NavMeshData</c> 为空，全工程没有任何 NavMeshAgent，
    /// 猫的移动一直是「朝远离玩家方向直线位移 + 一个矩形夹取」。矩形不认墙、不认家具、不认门，
    /// 所以会出现贴墙滑行、卡床脚、顺着开着的阳台门往室外挤。
    ///
    /// 这里不引入 NavMesh 包（会改工程依赖，也会和现有关卡流冲突），而是用一次性采样建一张网格：
    ///   1. 在 boundsCenter / boundsSize 划出的矩形里铺格子（该矩形必须落在房屋内）；
    ///   2. 每格从高处向下打射线找地面，只认基准高度附近的地面 —— 床、桌面、柜顶不算地面；
    ///   3. 用猫体积的胶囊做重叠检测，墙体、家具、门框都会把格子标成不可走；
    ///   4. 生成 8 邻接图，A* 求路径，再做直线可见性平滑。
    ///
    /// 因为可行走格子被裁在 bounds 之内，随机目标点不可能落到室外（阳台在 bounds 之外）。
    /// 门板碰撞体在烘焙时被忽略，门洞是通的；门关着这件事由 <see cref="CatDoorway"/> 在移动时把关。
    /// </summary>
    public sealed class CatNavGrid
    {
        /// <summary>网格是否可用（可用才允许猫移动）。</summary>
        public bool Ready { get; private set; }

        /// <summary>可行走格子数。</summary>
        public int WalkableCount { get; private set; }

        public Bounds Area { get { return new Bounds(_center, _size); } }

        Vector3 _center;
        Vector3 _size;
        Vector3 _min;
        float _cell;
        int _nx, _nz;
        int _maskBits;

        float[] _floor;
        bool[] _walk;
        bool[] _noFloor;      // 射线打空（地板缺失）的格子，只给补漏用
        int[] _walkable;

        readonly Collider[] _hits = new Collider[32];
        readonly HashSet<Collider> _ignored = new HashSet<Collider>();

        // ---- A* 复用缓冲（一次分配，反复使用，避免每次寻路 GC）
        float[] _g;
        float[] _f;
        int[] _parent;
        bool[] _closed;
        readonly MinHeap _heap = new MinHeap(512);
        readonly List<int> _raw = new List<int>(256);
        readonly List<int> _smooth = new List<int>(64);

        // ---------------------------------------------------------------- 烘焙

        /// <summary>
        /// 建网格。返回是否成功。
        /// </summary>
        /// <param name="center">可行走区域中心（世界坐标）</param>
        /// <param name="size">可行走区域尺寸（X×Z 生效，Y 只用来当基准地面高度）</param>
        /// <param name="cell">格子边长（米），0.1~0.2 比较合适</param>
        /// <param name="radius">猫身半径</param>
        /// <param name="height">猫身高度</param>
        /// <param name="mask">墙体/家具所在层</param>
        /// <param name="ignoreRoot">要忽略的碰撞体根（猫自己）</param>
        /// <param name="doorways">门列表：可通行门的门板碰撞体会被忽略</param>
        public bool Build(Vector3 center, Vector3 size, float cell, float radius, float height,
                          LayerMask mask, Transform ignoreRoot, List<CatDoorway> doorways)
        {
            Ready = false;
            WalkableCount = 0;

            _cell = Mathf.Clamp(cell, 0.05f, 0.4f);
            if (size.x < _cell * 3f || size.z < _cell * 3f) return false;

            _center = center;
            _size = size;
            _maskBits = mask.value;
            _min = new Vector3(center.x - size.x * 0.5f, center.y, center.z - size.z * 0.5f);
            _nx = Mathf.Max(1, Mathf.CeilToInt(size.x / _cell));
            _nz = Mathf.Max(1, Mathf.CeilToInt(size.z / _cell));

            int n = _nx * _nz;
            _floor = new float[n];
            _walk = new bool[n];
            _noFloor = new bool[n];
            _g = new float[n];
            _f = new float[n];
            _parent = new int[n];
            _closed = new bool[n];

            CollectIgnored(ignoreRoot, doorways);

            float r = Mathf.Max(0.05f, radius);
            float h = Mathf.Max(r * 2f + 0.04f, height);

            var list = new List<int>(n);
            for (int j = 0; j < _nz; j++)
            {
                for (int i = 0; i < _nx; i++)
                {
                    int idx = i + j * _nx;
                    float wx = _min.x + (i + 0.5f) * _cell;
                    float wz = _min.z + (j + 0.5f) * _cell;

                    float floorY;
                    if (!SampleFloor(wx, wz, out floorY)) { _noFloor[idx] = true; continue; }
                    if (IsBlocked(wx, floorY, wz, r, h)) continue;

                    _floor[idx] = floorY;
                    _walk[idx] = true;
                    list.Add(idx);
                }
            }

            // 补漏：门洞正下方常常有一两格地板网格没铺到（两块地板网格在门槛处对接），
            // 射线会打空。这类格子如果胶囊检测是空的（说明不是被墙挡住），
            // 且周围已经有可行走邻居，就按邻居地面高度补回来。
            // 注意只补「打空」的格子，被墙/家具挡住的格子永远不会被补，墙不会被打穿。
            FillFloorGaps(r, h, list);

            _walkable = list.ToArray();
            WalkableCount = list.Count;
            Ready = WalkableCount > 8;
            return Ready;
        }

        void FillFloorGaps(float r, float h, List<int> list)
        {
            var filled = new List<int>();

            for (int j = 1; j < _nz - 1; j++)
            {
                for (int i = 1; i < _nx - 1; i++)
                {
                    int idx = i + j * _nx;
                    if (!_noFloor[idx] || _walk[idx]) continue;

                    int neighbors = 0;
                    float sum = 0f;

                    for (int dj = -1; dj <= 1; dj++)
                    {
                        for (int di = -1; di <= 1; di++)
                        {
                            if (di == 0 && dj == 0) continue;
                            int k = (i + di) + (j + dj) * _nx;
                            if (!_walk[k]) continue;
                            neighbors++;
                            sum += _floor[k];
                        }
                    }

                    if (neighbors < 3) continue;

                    float fy = sum / neighbors;
                    float wx = _min.x + (i + 0.5f) * _cell;
                    float wz = _min.z + (j + 0.5f) * _cell;
                    if (IsBlocked(wx, fy, wz, r, h)) continue;

                    _floor[idx] = fy;
                    _walk[idx] = true;
                    filled.Add(idx);
                }
            }

            for (int i = 0; i < filled.Count; i++) list.Add(filled[i]);
        }

        void CollectIgnored(Transform ignoreRoot, List<CatDoorway> doorways)
        {
            _ignored.Clear();

            if (ignoreRoot != null)
            {
                var cs = ignoreRoot.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cs.Length; i++) _ignored.Add(cs[i]);
            }

            if (doorways == null) return;
            for (int i = 0; i < doorways.Count; i++)
            {
                var dw = doorways[i];
                if (dw == null || !dw.Passable || dw.Door == null) continue;
                Transform t = dw.Door.DoorTransform;
                if (t == null) continue;
                var cs = t.GetComponentsInChildren<Collider>(true);
                for (int k = 0; k < cs.Length; k++) _ignored.Add(cs[k]);
            }
        }

        /// <summary>向下找地面：只认基准高度附近的那一层，床垫/桌面/柜顶会被判成"非地面"。</summary>
        bool SampleFloor(float x, float z, out float y)
        {
            y = _center.y;
            Vector3 origin = new Vector3(x, _center.y + 2.4f, z);
            RaycastHit hit;
            if (!Physics.Raycast(origin, Vector3.down, out hit, 3.4f, _maskBits,
                                 QueryTriggerInteraction.Ignore))
                return false;

            if (hit.point.y > _center.y + 0.12f) return false;   // 站在床/桌子上，不是地面
            if (hit.point.y < _center.y - 0.45f) return false;   // 掉到地板下方
            y = hit.point.y;
            return true;
        }

        bool IsBlocked(float x, float floorY, float z, float r, float h)
        {
            Vector3 p0 = new Vector3(x, floorY + r + 0.03f, z);
            Vector3 p1 = new Vector3(x, floorY + Mathf.Max(r + 0.03f, h - r), z);

            int count = Physics.OverlapCapsuleNonAlloc(p0, p1, r, _hits, _maskBits,
                                                      QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _hits[i];
                if (c == null) continue;
                if (c is CharacterController) continue;     // 玩家占位不算障碍
                if (_ignored.Contains(c)) continue;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- 查询

        public int IndexOf(Vector3 p)
        {
            if (_walk == null) return -1;
            int i = Mathf.FloorToInt((p.x - _min.x) / _cell);
            int j = Mathf.FloorToInt((p.z - _min.z) / _cell);
            if (i < 0 || j < 0 || i >= _nx || j >= _nz) return -1;
            return i + j * _nx;
        }

        public Vector3 CellCenter(int idx)
        {
            int i = idx % _nx;
            int j = idx / _nx;
            return new Vector3(_min.x + (i + 0.5f) * _cell,
                               _floor[idx],
                               _min.z + (j + 0.5f) * _cell);
        }

        public bool IsWalkable(Vector3 p)
        {
            if (!Ready) return false;
            int idx = IndexOf(p);
            return idx >= 0 && _walk[idx];
        }

        /// <summary>该点所在格子的地面高度；点不可走时返回它自己的 y。</summary>
        public float FloorHeight(Vector3 p)
        {
            if (!Ready) return p.y;
            int idx = IndexOf(p);
            if (idx < 0 || !_walk[idx]) return p.y;
            return _floor[idx];
        }

        /// <summary>把点吸附到最近的可行走格子（相当于 NavMesh.SamplePosition）。</summary>
        public bool TrySnap(Vector3 p, float maxDist, out Vector3 result)
        {
            result = p;
            if (!Ready) return false;

            int idx = IndexOf(p);
            if (idx >= 0 && _walk[idx]) { result = CellCenter(idx); return true; }

            int ci = Mathf.FloorToInt((p.x - _min.x) / _cell);
            int cj = Mathf.FloorToInt((p.z - _min.z) / _cell);
            int rad = Mathf.Max(1, Mathf.CeilToInt(maxDist / _cell));
            float best = maxDist * maxDist;
            int bestIdx = -1;

            for (int j = cj - rad; j <= cj + rad; j++)
            {
                if (j < 0 || j >= _nz) continue;
                for (int i = ci - rad; i <= ci + rad; i++)
                {
                    if (i < 0 || i >= _nx) continue;
                    int k = i + j * _nx;
                    if (!_walk[k]) continue;
                    Vector3 c = CellCenter(k);
                    float d = (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z);
                    if (d < best) { best = d; bestIdx = k; }
                }
            }

            if (bestIdx < 0) return false;
            result = CellCenter(bestIdx);
            return true;
        }

        /// <summary>在任意室内可行走格子里随机取一点。</summary>
        public bool TryGetRandomPoint(out Vector3 p)
        {
            p = Vector3.zero;
            if (!Ready) return false;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                int idx = _walkable[Random.Range(0, _walkable.Length)];
                if (!_walk[idx]) continue;
                p = CellCenter(idx);
                return true;
            }
            return false;
        }

        /// <summary>取离 want 最近、且在 radius 内的可行走点。</summary>
        public bool TryGetPointNear(Vector3 want, float radius, out Vector3 result)
        {
            result = want;
            if (!Ready) return false;

            float best = radius * radius;
            bool found = false;

            for (int k = 0; k < _walkable.Length; k++)
            {
                int idx = _walkable[k];
                if (!_walk[idx]) continue;
                Vector3 c = CellCenter(idx);
                float d = (c.x - want.x) * (c.x - want.x) + (c.z - want.z) * (c.z - want.z);
                if (d < best) { best = d; result = c; found = true; }
            }
            return found;
        }

        // ---------------------------------------------------------------- A*

        /// <summary>
        /// 求路径（世界坐标折线，已做直线平滑）。返回 false 表示不连通。
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            if (!Ready) return false;

            Vector3 a, b;
            if (!TrySnap(from, _cell * 6f, out a)) return false;
            if (!TrySnap(to, _cell * 4f, out b)) return false;

            int start = IndexOf(a);
            int goal = IndexOf(b);
            if (start < 0 || goal < 0) return false;

            int n = _nx * _nz;
            for (int i = 0; i < n; i++)
            {
                _g[i] = float.MaxValue;
                _f[i] = float.MaxValue;
                _parent[i] = -1;
                _closed[i] = false;
            }

            _heap.Clear();
            _g[start] = 0f;
            _f[start] = Heuristic(start, goal);
            _heap.Push(start, _f[start]);

            bool found = false;
            int guard = 0;

            while (_heap.Count > 0 && guard++ < n * 4)
            {
                int cur = _heap.Pop();
                if (cur == goal) { found = true; break; }
                if (_closed[cur]) continue;
                _closed[cur] = true;

                int ci = cur % _nx;
                int cj = cur / _nx;

                for (int dj = -1; dj <= 1; dj++)
                {
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int ni = ci + di;
                        int nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz) continue;

                        int nb = ni + nj * _nx;
                        if (!_walk[nb] || _closed[nb]) continue;

                        // 斜向走位要求两个正交邻居都通，避免卡着家具角切过去
                        if (di != 0 && dj != 0)
                        {
                            if (!_walk[ci + di + cj * _nx]) continue;
                            if (!_walk[ci + (cj + dj) * _nx]) continue;
                        }

                        float step = (di != 0 && dj != 0) ? 1.41421356f : 1f;
                        float tentative = _g[cur] + step;
                        if (tentative >= _g[nb]) continue;

                        _g[nb] = tentative;
                        _f[nb] = tentative + Heuristic(nb, goal);
                        _parent[nb] = cur;
                        _heap.Push(nb, _f[nb]);
                    }
                }
            }

            if (!found) return false;

            _raw.Clear();
            int node = goal;
            int safety = 0;
            while (node >= 0 && safety++ < n)
            {
                _raw.Add(node);
                if (node == start) break;
                node = _parent[node];
            }
            _raw.Reverse();
            if (_raw.Count == 0) return false;

            Smooth();
            for (int i = 0; i < _smooth.Count; i++) result.Add(CellCenter(_smooth[i]));
            return result.Count > 0;
        }

        float Heuristic(int a, int b)
        {
            int ax = a % _nx, az = a / _nx;
            int bx = b % _nx, bz = b / _nx;
            int dx = Mathf.Abs(ax - bx);
            int dz = Mathf.Abs(az - bz);
            int mn = Mathf.Min(dx, dz);
            return (dx + dz) + (1.41421356f - 2f) * mn;
        }

        void Smooth()
        {
            _smooth.Clear();
            if (_raw.Count == 0) return;

            _smooth.Add(_raw[0]);
            int anchor = 0;
            while (anchor < _raw.Count - 1)
            {
                int far = _raw.Count - 1;
                while (far > anchor + 1)
                {
                    if (LineWalkable(_raw[anchor], _raw[far])) break;
                    far--;
                }
                _smooth.Add(_raw[far]);
                anchor = far;
            }
        }

        /// <summary>两格之间是否直线可走（按半格步长采样，够用且比物理查询便宜）。</summary>
        bool LineWalkable(int a, int b)
        {
            Vector3 pa = CellCenter(a);
            Vector3 pb = CellCenter(b);
            float dist = Vector3.Distance(new Vector3(pa.x, 0f, pa.z), new Vector3(pb.x, 0f, pb.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (_cell * 0.5f)));

            int prev = a;
            for (int s = 1; s <= steps; s++)
            {
                Vector3 p = Vector3.Lerp(pa, pb, s / (float)steps);
                int idx = IndexOf(p);
                if (idx < 0 || !_walk[idx]) return false;

                // 斜向采样也要保证正交格子通，否则会擦着墙角过去
                int pi = prev % _nx, pj = prev / _nx;
                int ci = idx % _nx, cj = idx / _nx;
                if (pi != ci && pj != cj)
                {
                    if (!_walk[ci + pj * _nx]) return false;
                    if (!_walk[pi + cj * _nx]) return false;
                }
                prev = idx;
            }
            return true;
        }

        /// <summary>调试用：画可行走格子。</summary>
        public void DrawGizmos(float y)
        {
            if (!Ready) return;
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.4f);
            float s = _cell * 0.85f;
            for (int k = 0; k < _walkable.Length; k++)
            {
                Vector3 c = CellCenter(_walkable[k]);
                Gizmos.DrawCube(new Vector3(c.x, y, c.z), new Vector3(s, 0.01f, s));
            }
        }

        // ---------------------------------------------------------------- 最小堆

        sealed class MinHeap
        {
            int[] _v;
            float[] _k;
            int _n;

            public MinHeap(int cap)
            {
                _v = new int[Mathf.Max(8, cap)];
                _k = new float[_v.Length];
            }

            public int Count { get { return _n; } }

            public void Clear() { _n = 0; }

            public void Push(int value, float key)
            {
                if (_n >= _v.Length)
                {
                    System.Array.Resize(ref _v, _v.Length * 2);
                    System.Array.Resize(ref _k, _k.Length * 2);
                }

                int i = _n++;
                _v[i] = value;
                _k[i] = key;

                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_k[p] <= _k[i]) break;
                    Swap(p, i);
                    i = p;
                }
            }

            public int Pop()
            {
                int top = _v[0];
                _n--;

                if (_n > 0)
                {
                    _v[0] = _v[_n];
                    _k[0] = _k[_n];

                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1;
                        int r = l + 1;
                        int m = i;
                        if (l < _n && _k[l] < _k[m]) m = l;
                        if (r < _n && _k[r] < _k[m]) m = r;
                        if (m == i) break;
                        Swap(m, i);
                        i = m;
                    }
                }
                return top;
            }

            void Swap(int a, int b)
            {
                int tv = _v[a]; _v[a] = _v[b]; _v[b] = tv;
                float tk = _k[a]; _k[a] = _k[b]; _k[b] = tk;
            }
        }
    }
}
