using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 姿势接地工具：算出"当前动画姿势下，猫身上最低的那个点在世界坐标里的高度"。
    ///
    /// 用途是开场把猫摆到床上时不要靠猜 Y：
    /// 先让 Animator 把趴卧姿势求值出来，量出最低点，再和床面高度做差，一次性平移到位。
    /// 这样换了床、换了动画、改了缩放都不用重新手调数值，也不会出现"肉穿过床垫"。
    ///
    /// 注意：SkinnedMeshRenderer 的 bounds 是蒙皮包围盒的保守估计（会随动画膨胀），
    /// 直接用它算会偏高，所以这里 BakeMesh 拿真实顶点。
    /// </summary>
    public static class CatPoseUtility
    {
        /// <summary>
        /// 取根节点下蒙皮网格的最低世界 Y。scratch 是外部复用的临时 Mesh（避免每次 new）。
        /// </summary>
        public static bool TryGetLowestPointY(Transform root, Mesh scratch, out float worldY)
        {
            worldY = 0f;
            if (root == null || scratch == null) return false;

            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null) return false;

            scratch.Clear();
            smr.BakeMesh(scratch);

            Vector3[] verts = scratch.vertices;
            if (verts == null || verts.Length == 0) return false;

            // 关键：不能用 smr.transform.localToWorldMatrix。
            // 本角色为了匹配 Blender 单位，CatRig 整条骨骼链被放大 100 倍，
            // 而 SkinnedMeshRenderer.BakeMesh 输出的顶点已经抵消了渲染节点自身的缩放
            // （顶点数值是世界尺度），再乘 localToWorldMatrix 会再放大 100 倍 ——
            // 之前"开场悬空"就是这里量出 -2.33m，导致贴地位移被 settleMaxShift 夹到 +0.35。
            // 正确做法：只应用位置与旋转，缩放取 1。
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

            float min = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float y = m.MultiplyPoint3x4(verts[i]).y;
                if (y < min) min = y;
            }

            if (min == float.MaxValue) return false;

            // 兜底防呆：量出来的最低点离渲染包围盒中心太远，说明矩阵假设不成立，
            // 退回 Unity 自己算的（偏保守的）世界包围盒下沿，宁可略高也不要穿模
            Bounds wb = smr.bounds;
            if (Mathf.Abs(min - wb.center.y) > wb.extents.y * 2.5f + 0.2f)
            {
                Debug.LogWarning("[CatPoseUtility] 蒙皮最低点异常（" + min.ToString("F3") +
                                 "），改用渲染包围盒下沿 " + wb.min.y.ToString("F3"));
                min = wb.min.y;
            }

            worldY = min;
            return true;
        }

        /// <summary>
        /// 向下找支撑面高度（床垫/地板）。会跳过 catRoot 自己的碰撞体，
        /// 否则射线第一下就打在猫的胶囊上。
        /// </summary>
        public static bool TryGetSurfaceY(Transform catRoot, Vector3 from, float maxDistance,
                                          LayerMask mask, out float surfaceY)
        {
            surfaceY = from.y;

            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, maxDistance, mask,
                                                   QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;
                if (catRoot != null && c.transform.IsChildOf(catRoot)) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    surfaceY = hits[i].point.y;
                    found = true;
                }
            }
            return found;
        }
    }
}
