using System.Collections;
using UnityEngine;
using NOCTURNE.Cat;

namespace NOCTURNE.Feeding
{
    /// <summary>
    /// 猫窝：只管"让猫在正确的位置、正确的姿势、正确地贴在垫子上"这一件事。
    ///
    /// 贴垫子不靠手调 Y：先把猫摆到 CatSleepPoint，播 Cat_Sleep（狮身人面趴卧），
    /// 等 Animator 求值两帧后用 <see cref="CatPoseUtility"/> 量当前姿势的最低点，
    /// 再和垫子表面做差一次性平移 —— 换床、换动画、改缩放都不用重新对数值，也不会陷进去。
    ///
    /// 睡觉期间 cat.SetTransformLocked(true)，CatAgent 的 Update 直接返回，
    /// 不会有任何系统再写 Transform（呼吸仍由 Animator 播）。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatBedController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("猫睡觉时身体中心的位置与朝向")]
        public Transform sleepPoint;

        [Tooltip("猫（留空则自动找场景里的 CatAgent）")]
        public CatAgent cat;

        [Tooltip("CatLookAt。睡觉时要关掉，否则猫会扭头盯着玩家")]
        public MonoBehaviour lookAt;

        [Header("贴垫子")]
        [Tooltip("启用后按趴卧姿势最低点自动贴合垫子表面")]
        public bool settleOnCushion = true;

        [Tooltip("垫子表面高度直接用 SleepPoint 的 Y（推荐）。" +
                 "猫窝故意不放碰撞体：放了会被寻路当成障碍，猫就走不到窝中心")]
        public bool useSleepPointHeight = true;

        [Tooltip("不用 SleepPoint 高度时，向下找垫子表面用的层")]
        public LayerMask surfaceMask = ~0;

        [Tooltip("射线起点相对猫窝的抬高量（米）")]
        public float probeLift = 0.45f;

        [Tooltip("量不到蒙皮时的兜底下沉量（米）。Cat_Sleep 实测网格最低点比根低 0.0305")]
        public float fallbackSink = 0.0305f;

        [Tooltip("自动贴地允许的最大修正（米），超过就夹取并告警")]
        public float maxShift = 0.14f;

        [Header("睡眠")]
        public CatMood sleepMood = CatMood.Sleep;

        [Header("状态（只读）")]
        [SerializeField] bool _occupied;

        public bool IsOccupied { get { return _occupied; } }
        public Transform SleepPoint { get { return sleepPoint != null ? sleepPoint : transform; } }

        Mesh _scratch;

        void Awake()
        {
            if (cat == null) cat = FindObjectOfType<CatAgent>();
        }

        void OnDestroy()
        {
            if (_scratch != null) Destroy(_scratch);
        }

        // ---------------------------------------------------------------- 对外

        /// <summary>让猫到窝里趴下睡觉（位置硬摆 + 姿势贴垫子 + 锁死 Transform）。</summary>
        public void PutToSleep(CatAgent agent)
        {
            if (agent == null) agent = cat;
            if (agent == null)
            {
                Debug.LogWarning("[CatBed] 没有 CatAgent，无法让猫进窝", this);
                return;
            }

            StartCoroutine(SleepRoutine(agent));
        }

        /// <summary>叫醒：解除位置锁，恢复头部注视，交回流程脚本。</summary>
        public void WakeUp(CatAgent agent)
        {
            if (agent == null) agent = cat;
            if (agent == null) return;

            _occupied = false;
            agent.SetTransformLocked(false);
            agent.SetMoodImmediate(CatMood.Idle);
            if (lookAt != null) lookAt.enabled = true;
        }

        IEnumerator SleepRoutine(CatAgent agent)
        {
            agent.SuspendAi(true);
            agent.ScriptedStop();

            Transform sp = SleepPoint;
            Vector3 p = sp.position;
            float yaw = sp.eulerAngles.y;

            agent.PlaceAt(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, yaw, 0f));
            agent.SetMoodImmediate(sleepMood);

            if (lookAt != null) lookAt.enabled = false;

            // 位置唯一控制方：本协程。锁掉 CatAgent，避免别的系统在贴地后动它
            agent.SetTransformLocked(true);
            _occupied = true;

            if (!settleOnCushion) yield break;

            // Animator 初始化的第一帧会吞掉播放请求（项目里实测过），等两帧再量姿势
            yield return null;
            yield return null;

            agent.SetMoodImmediate(sleepMood);   // 再推一次，确保量到的是趴卧姿势
            yield return null;

            if (_scratch == null) _scratch = new Mesh { name = "CatSleepPose" };

            float surfaceY;
            if (useSleepPointHeight)
            {
                surfaceY = sp.position.y;
            }
            else
            {
                Vector3 origin = sp.position + Vector3.up * probeLift;
                if (!CatPoseUtility.TryGetSurfaceY(agent.transform, origin, probeLift + 1.2f, surfaceMask, out surfaceY))
                {
                    Debug.LogWarning("[CatBed] 没找到垫子表面，猫身沉一个兜底量", this);
                    agent.transform.position += Vector3.up * fallbackSink;
                    yield break;
                }
            }

            float lowest;
            if (!CatPoseUtility.TryGetLowestPointY(agent.transform, _scratch, out lowest))
            {
                agent.transform.position += Vector3.up * fallbackSink;
                Debug.LogWarning("[CatBed] 量不到蒙皮最低点，使用兜底下沉量 " + fallbackSink.ToString("F4"), this);
                yield break;
            }

            float delta = surfaceY - lowest;
            if (Mathf.Abs(delta) > maxShift)
            {
                Debug.LogWarning(string.Format(
                    "[CatBed] 姿势最低点与垫子表面差 {0:F3}m，超过 maxShift，已夹取", delta), this);
                delta = Mathf.Clamp(delta, -maxShift, maxShift);
            }

            agent.transform.position += Vector3.up * delta;

            Debug.Log(string.Format(
                "[CatBed] 猫进窝贴垫：垫面 y={0:F4}，姿势最低点 y={1:F4}，修正 {2:+0.0000;-0.0000}m",
                surfaceY, lowest, delta), this);
        }
    }
}
