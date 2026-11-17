using System.Collections;
using UnityEngine;
using NOCTURNE.Cat;
using NOCTURNE.Opening;

namespace NOCTURNE
{
    /// <summary>
    /// 猫咪的开场行为。
    ///
    /// 开场时猫就在床上、正对玩家（朝向由场景里的 Cat_Bed_OpeningPosition 决定），
    /// 期间 CatAgent 被禁用并加锁 —— 不走寻路、不响应需求系统、不会被 AI 挪走。
    ///
    /// v2 修正（悬空 / 抖动）：
    /// 1. 姿势换成趴卧剪辑（默认 Cat_Sleep，狮身人面式，clip 自带呼吸循环）。
    ///    之前只播 Cat_Idle —— 那是四腿站立的循环（Blender 里 root 高度 0、四爪踩地），
    ///    放到 0.712m 高的床垫上就是「站在床上」，身体重心高、腿是直的，看着就是悬空。
    /// 2. 不再靠手调 Y。锁定时先求值出趴卧姿势，量出蒙皮网格最低点的世界 Y，
    ///    再向下射线找床面，一次性把根节点平移到「肉刚好贴住床面」的位置。
    ///    换床、换动画、改缩放都不用重新对数值，也不会穿模。
    /// 3. 位置唯一控制方：本脚本。CatAgent 被禁用 + SetTransformLocked(true)，
    ///    CatLookAt 被禁用，Animator.applyRootMotion 置 false，
    ///    所以玩家睁眼、转头、摄像机变化都不会带得猫抖。
    ///
    /// 只有玩家真正完成起床（PlayerLyingController.GetUpFinished）后，
    /// 猫才解锁并跳到地上，落地后把控制权交还 CatAgent。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatOpeningController : MonoBehaviour
    {
        [Header("引用")]
        public Transform cat;
        public Transform bedOpeningPosition;
        public Transform floorExitPosition;
        public CatAgent catAgent;
        public Animator animator;
        public PlayerLyingController lying;
        [Tooltip("头部注视脚本。开场需要它盯着玩家，所以锁定期间要开着并把它指向玩家的眼睛")]
        public CatLookAt lookAt;
        [Tooltip("程序化尾巴/呼吸叠加。开场期间禁用，把骨骼完全交给坐姿剪辑")]
        public CatBodyMotion bodyMotion;
        [Tooltip("铃铛。开场期间禁用，避免任何位移把它带响")]
        public CatBell catBell;

        [Header("开场凝视（近距离、饥饿、令人不适）")]
        [Tooltip("开场让猫盯着谁。留空则用玩家的相机（CameraRig）")]
        public Transform gazeTarget;

        [Tooltip("锁定时让猫的水平朝向转向注视者（否则是锚点里配的固定朝向）")]
        public bool faceGazeOnLock = true;

        [Tooltip("歪头角度（度）。歪一点点就会明显不对劲")]
        public float openingHeadTilt = 6f;

        [Tooltip("凝视时头部慢速摆动的幅度（度）")]
        public float openingTiltSway = 2.2f;

        [Header("开场姿势")]
        [Tooltip("开场播放的剪辑。Cat_Sit = 揣手趴着抬头看玩家（自带呼吸+下巴微动）；" +
                 "Cat_Sleep = 低头睡着。睁眼时猫必须是醒着的，所以默认 Cat_Sit")]
        public string openingClipName = "Cat_Sit";

        [Tooltip("锁定时把猫贴到床面上（按姿势最低点自动对齐，不靠手调 Y）")]
        public bool settleOnSurface = true;

        [Tooltip("找床面用的层。默认全部层，射线会跳过猫自己的碰撞体")]
        public LayerMask surfaceMask = ~0;

        [Tooltip("找床面时射线的起点抬高量（米），要高于猫的碰撞体顶部")]
        public float settleProbeLift = 0.7f;

        [Tooltip("自动对齐允许的最大位移（米），超过就认为配置有问题，只做夹取并告警")]
        public float settleMaxShift = 0.35f;

        [Tooltip("量不到蒙皮网格时的兜底下沉量（米）。Cat_Sleep 实测网格最低点比根低 0.0305")]
        public float fallbackPoseSink = 0.0305f;

        [Tooltip("贴地做几遍。换剪辑后姿势会再变一次，一遍量不准（3 遍基本收敛）")]
        public int settlePasses = 3;

        [Tooltip("每遍之间等多久（秒）")]
        public float settleInterval = 0.35f;

        [Tooltip("允许猫陷进被子多少（米）。视觉上\"压着\"比\"浮着\"可信")]
        public float sinkIntoSurface = 0.012f;

        [Header("离床动作")]
        [Tooltip("起身（重心前移）时长，趴卧→起跳之间的过渡")]
        public float crouchDelay = 0.18f;

        [Tooltip("从床上跳到地面的时长")]
        public float hopDuration = 0.95f;
        [Tooltip("跳跃弧线的额外高度")]
        public float hopArc = 0.16f;
        [Tooltip("落地后等多久交还 AI")]
        public float handoverDelay = 0.25f;
        [Tooltip("离床时播放的动画（CatAgent 里已有的 pounce）")]
        public string leapStateName = "Cat_Pounce";

        [Header("开场叫声")]
        [Tooltip("叫声剪辑池，每次随机挑一个。留空则不叫")]
        public AudioClip[] meowClips;

        [Tooltip("不在锁定那一刻就叫：等开场流程在「睁眼」这一帧调用 BeginOpeningMeow()。" +
                 "黑屏独白期间必须完全静音，所以默认开启")]
        public bool waitForEyeOpenSignal = true;

        [Tooltip("开场第一声的延迟（秒）。玩家睁眼的一瞬间不能连着叫")]
        public float firstMeowDelay = 1.6f;

        [Tooltip("两次叫声的最小间隔（秒）")]
        public float meowIntervalMin = 3.5f;

        [Tooltip("两次叫声的最大间隔（秒）")]
        public float meowIntervalMax = 7.5f;

        [Range(0f, 1f)] public float meowVolume = 0.55f;

        [Tooltip("放叫声的 3D 音源。留空则自动找/建在猫身上")]
        public AudioSource meowSource;

        [Header("张嘴同步")]
        [Tooltip("叫的时候下巴张开的角度（度）。0 = 不驱动，只靠剪辑自带的下巴微动")]
        public float meowJawOpen = 14f;

        [Tooltip("张嘴动作跟着叫声持续多久（秒）。<=0 则用音频本身长度")]
        public float meowJawSeconds = 0f;

        [Header("开场锁定")]
        [Tooltip("启用后在 Start 里强制把猫摆到床上并锁住")]
        public bool applyOnStart = true;

        [Tooltip("交还 AI 后是否允许头部注视。锁定前的开关状态会被缓存，交还时原样恢复，" +
                 "避免「下床那一帧突然开注视」把头甩过去")]
        public bool enableLookAtAfterHandover = true;

        [Tooltip("开场贴脸凝视时关掉歪头摆动/点头微动。距离只有 0.36m，任何程序化微动都会被放大成抽搐")]
        public bool freezeGazeMicroMotion = true;

        public bool IsLocked { get; private set; }
        public bool IsLeaving { get; private set; }

        bool _hooked;
        bool _settling;
        Mesh _scratch;

        // 锁定前的组件开关 / 参数缓存：交还时逐项还原，不做任何"硬开"
        bool _savedBodyMotion, _savedBell, _savedLookAt, _hasSaved;
        float _savedTiltSway, _savedNod, _savedTilt;

        Transform _jawBone;
        float _jawSign = 1f;
        float _jawT = -1f;
        float _jawDur;
        bool _jawSignChecked;

        void Start()
        {
            if (applyOnStart) EnterOpeningLock();
        }

        void OnEnable()
        {
            Hook(true);
        }

        void OnDisable()
        {
            Hook(false);
        }

        void OnDestroy()
        {
            if (_scratch != null) Destroy(_scratch);
        }

        void Hook(bool on)
        {
            if (lying == null) return;
            if (on && !_hooked) { lying.GetUpFinished += OnPlayerGetUpFinished; _hooked = true; }
            else if (!on && _hooked) { lying.GetUpFinished -= OnPlayerGetUpFinished; _hooked = false; }
        }

        /// <summary>把猫摆到床上并锁死（位置、旋转、姿势、以及「谁在动它」全部接管）。</summary>
        public void EnterOpeningLock()
        {
            if (cat == null) return;

            StopAllCoroutines();
            _settling = false;

            if (bedOpeningPosition != null)
            {
                cat.position = bedOpeningPosition.position;
                cat.rotation = bedOpeningPosition.rotation;
            }

            // 面向注视者：开场玩家平躺，猫要在脸旁边"正面"盯着他。
            // 注意：这一刻玩家还没躺下去（PlayerLyingController 在稍后把相机搬到床上），
            // 直接朝相机转头会朝到"玩家站着的位置"，最后看到的是猫的侧脸。
            // 所以优先朝躺卧锚点，头部再交给 CatLookAt 去追真正的眼睛。
            Transform gaze = ResolveGaze();
            Transform faceTarget = gaze;
            if (lying != null && lying.lyingAnchor != null) faceTarget = lying.lyingAnchor;

            if (faceGazeOnLock && faceTarget != null)
            {
                Vector3 dir = faceTarget.position - cat.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    cat.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            IsLocked = true;
            IsLeaving = false;

            // ---- 开场独占：所有会写猫 Transform / 骨骼的普通组件一律停掉。
            //      位置与姿势只由本脚本 + Animator 的坐姿剪辑负责，杜绝多控制方打架。
            if (bodyMotion == null) bodyMotion = GetComponent<CatBodyMotion>();
            if (catBell == null) catBell = GetComponent<CatBell>();

            if (!_hasSaved)
            {
                _savedBodyMotion = bodyMotion != null && bodyMotion.enabled;
                _savedBell = catBell != null && catBell.enabled;
                _savedLookAt = lookAt != null && lookAt.enabled;
                if (lookAt != null)
                {
                    _savedTiltSway = lookAt.headTiltSwayDegrees;
                    _savedNod = lookAt.headNodDegrees;
                    _savedTilt = lookAt.headTiltDegrees;
                }
                _hasSaved = true;
            }

            if (catAgent != null)
            {
                catAgent.SetTransformLocked(true);
                catAgent.enabled = false;
            }
            if (bodyMotion != null) bodyMotion.enabled = false;
            if (catBell != null) catBell.enabled = false;

            // ---- 开场期间骨骼由 Animator 独占，CatLookAt 必须关闭。
            //      实测根因：CatLookAt 在 LateUpdate 写 neck/head 的 WORLD rotation，
            //      而 Animator 每帧写同一批骨骼的 localRotation；两者叠加后
            //      逐帧四元数角距 Whead 均值 0.037°/帧 -> 0.297°/帧（8 倍），
            //      n>1° 从 2 帧涨到 30 帧/650 帧，贴脸 0.36m 时就是"持续抽搐"。
            //      朝向已由上面的 cat.rotation 一次性对准躺卧锚点；Cat_Sit 剪辑
            //      自带呼吸、头部、耳朵、尾巴微动，足够"近距离注视"，不需要逐帧追目标。
            //      openingHeadTilt / openingTiltSway / freezeGazeMicroMotion 仅为兼容
            //      旧场景序列化数据保留，开场不再驱动任何程序化骨骼微动。
            if (lookAt != null) lookAt.enabled = false;

            // ---- 姿势
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.speed = 1f;
                PlayOpeningClip();
            }

            if (settleOnSurface) StartCoroutine(SettleRoutine());

            // 黑屏独白期间绝不能有猫叫：默认等开场流程在睁眼那一帧发信号
            if (!waitForEyeOpenSignal) StartMeowing();
        }

        /// <summary>
        /// 把开场剪辑推上去。用 Play 而不是 CrossFade：开场是全黑，不需要过渡，
        /// 也更贴合「锁定瞬间就定死姿势」的语义。
        /// </summary>
        void PlayOpeningClip()
        {
            if (animator == null || string.IsNullOrEmpty(openingClipName)) return;

            int hash = Animator.StringToHash(openingClipName);
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return;

            // Animator 还没初始化完时 HasState 恒为 false，这里静默返回，
            // 由 SettleRoutine 里的重发兜底；真写错了名字会在那里报 error。
            if (!animator.HasState(0, hash)) return;

            animator.Play(hash, 0, 0f);
        }

        /// <summary>
        /// 按「当前姿势的最低点」把猫贴到床面上。
        /// 必须在 Animator 求值之后做，所以先等几帧；
        /// 而且换剪辑之后姿势会再变一次，所以做多遍（收敛）。
        /// </summary>
        IEnumerator SettleRoutine()
        {
            _settling = true;

            // Animator 初始化的第一帧会吞掉播放请求（本项目实测确认过），
            // 所以这里补发一次；否则量到的是默认的 Cat_Idle（站立），
            // 贴地就会按站姿算，趴卧时反而陷进床里。
            yield return null;
            PlayOpeningClip();
            yield return null;
            yield return null;

            if (!_settling || cat == null) yield break;

            if (animator != null && !string.IsNullOrEmpty(openingClipName))
            {
                int want = Animator.StringToHash(openingClipName);
                if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash != want)
                {
                    Debug.LogError("[CatOpeningController] 开场姿势没切到 " + openingClipName +
                                   "（状态名可能写错，或者不在第 0 层），贴地会按当前姿势算", this);
                }
            }

            int passes = Mathf.Max(1, settlePasses);
            for (int i = 0; i < passes; i++)
            {
                if (i > 0) yield return new WaitForSeconds(Mathf.Max(0.05f, settleInterval));
                if (!_settling || cat == null) yield break;
                SettleOnce(i);
            }

            _settling = false;
        }

        /// <summary>量一次姿势最低点并对齐到床面。返回 false = 量不到蒙皮。</summary>
        bool SettleOnce(int pass)
        {
            Vector3 origin = cat.position + Vector3.up * settleProbeLift;

            float surfaceY;
            if (!CatPoseUtility.TryGetSurfaceY(cat, origin, settleProbeLift + 1.2f, surfaceMask, out surfaceY))
            {
                if (pass == 0) Debug.LogWarning("[CatOpeningController] 没找到床面，保留锚点高度", this);
                return false;
            }

            if (_scratch == null) _scratch = new Mesh { name = "CatOpeningPose" };

            float lowest;
            if (!CatPoseUtility.TryGetLowestPointY(cat, _scratch, out lowest))
            {
                // 量不到蒙皮（没 SkinnedMeshRenderer / 还没求值）：用实测常数兜底
                if (pass == 0)
                {
                    cat.position = new Vector3(cat.position.x,
                                               cat.position.y + fallbackPoseSink,
                                               cat.position.z);
                    Debug.LogWarning("[CatOpeningController] 量不到蒙皮最低点，使用兜底下沉量 " +
                                     fallbackPoseSink.ToString("F4"), this);
                }
                return false;
            }

            float delta = surfaceY - lowest;
            if (Mathf.Abs(delta) > settleMaxShift)
            {
                Debug.LogWarning(string.Format(
                    "[CatOpeningController] 姿势最低点与床面差了 {0:F3}m，超过 settleMaxShift，已夹取。" +
                    "检查 openingClipName 是否真的是趴卧剪辑。", delta), this);
                delta = Mathf.Clamp(delta, -settleMaxShift, settleMaxShift);
            }

            // 允许轻微陷进被子：视觉上"压着"比"浮着"可信得多
            delta -= Mathf.Max(0f, sinkIntoSurface);

            cat.position = new Vector3(cat.position.x, cat.position.y + delta, cat.position.z);

            Debug.Log(string.Format(
                "[CatOpeningController] 开场贴地#{0}：床面 y={1:F4}，姿势最低点 y={2:F4}，修正 {3:+0.0000;-0.0000}m，结果 y={4:F4}",
                pass, surfaceY, lowest, delta, cat.position.y), this);
            return true;
        }

        // ---------------------------------------------------------------- 叫声

        Transform ResolveGaze()
        {
            if (gazeTarget != null) return gazeTarget;
            var fpc = FindObjectOfType<FirstPersonController>();
            if (fpc != null && fpc.cameraRig != null)
            {
                gazeTarget = fpc.cameraRig;
                return gazeTarget;
            }
            return null;
        }

        void StartMeowing()
        {
            if (meowClips == null || meowClips.Length == 0) return;
            StopCoroutine("MeowLoop");
            StartCoroutine(MeowLoop());
        }

        void StopMeowing()
        {
            StopCoroutine("MeowLoop");
            _jawT = -1f;
        }

        /// <summary>开场流程在「睁眼」那一帧调用：从这一刻起猫才开始叫。</summary>
        public void BeginOpeningMeow()
        {
            if (IsLeaving) return;
            StartMeowing();
        }

        /// <summary>需要彻底安静时调用（重新开始开场、跳过独白等）。</summary>
        public void StopOpeningMeow()
        {
            StopMeowing();
        }

        // ---------------------------------------------------------------- 张嘴同步

        void CacheJawBone()
        {
            if (_jawBone != null || cat == null) return;
            var all = cat.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "jaw") { _jawBone = all[i]; break; }
            }
        }

        /// <summary>
        /// 下巴张开的旋转方向取决于骨骼局部轴，写死容易反（变成嘴往里陷）。
        /// 这里第一次用正号试一下，量下巴尖是不是往下走，不走就翻符号。
        /// </summary>
        void EnsureJawSign()
        {
            if (_jawSignChecked || _jawBone == null) return;
            _jawSignChecked = true;

            Quaternion rot = _jawBone.rotation;
            Vector3 probeBefore = _jawBone.position + _jawBone.forward * 0.05f;

            _jawBone.rotation = rot * Quaternion.AngleAxis(20f, Vector3.right);
            Vector3 probePlus = _jawBone.position + _jawBone.forward * 0.05f;
            _jawBone.rotation = rot * Quaternion.AngleAxis(-20f, Vector3.right);
            Vector3 probeMinus = _jawBone.position + _jawBone.forward * 0.05f;
            _jawBone.rotation = rot;

            float dPlus = probePlus.y - probeBefore.y;
            float dMinus = probeMinus.y - probeBefore.y;
            // 张嘴 = 下巴尖往下掉
            _jawSign = dPlus < dMinus ? 1f : -1f;
        }

        void LateUpdate()
        {
            // 开场锁定期间骨骼由 Animator 独占：不叠加 jaw，避免与 Cat_Sit 自带的
            // 下巴曲线双重写入（睁眼叫声时实测 Wjaw 均值从 0.055 翻倍到 0.107°/帧）。
            // 跳床开始后（IsLeaving）由 LeaveBedRoutine 的 StopMeowing 收尾，同样不写。
            if (IsLocked || IsLeaving)
            {
                // 不写骨骼，也不留下「正在张嘴」的悬挂状态
                _jawT = -1f;
                return;
            }
            if (_jawT < 0f) return;

            _jawT += Time.deltaTime;
            float k = _jawT / Mathf.Max(0.05f, _jawDur);
            if (k >= 1f) { _jawT = -1f; return; }

            CacheJawBone();
            if (_jawBone == null || meowJawOpen <= 0.01f) return;

            EnsureJawSign();
            float open = Mathf.Sin(k * Mathf.PI) * meowJawOpen * _jawSign;
            _jawBone.rotation = _jawBone.rotation * Quaternion.AngleAxis(open, Vector3.right);
        }

        /// <summary>
        /// 间歇性叫：每次随机间隔，绝不循环播放，也不会在睁眼瞬间连叫。
        /// </summary>
        IEnumerator MeowLoop()
        {
            if (meowSource == null && cat != null)
            {
                meowSource = cat.GetComponent<AudioSource>();
                if (meowSource == null) meowSource = cat.gameObject.AddComponent<AudioSource>();
            }

            if (meowSource != null)
            {
                meowSource.playOnAwake = false;
                meowSource.loop = false;
                meowSource.spatialBlend = 1f;                 // 3D：声音来自猫的位置
                meowSource.rolloffMode = AudioRolloffMode.Logarithmic;
                meowSource.minDistance = 0.8f;                // 很近时最清晰
                meowSource.maxDistance = 9f;
                meowSource.dopplerLevel = 0f;
            }

            yield return new WaitForSeconds(Mathf.Max(0.1f, firstMeowDelay));

            while (IsLocked && !IsLeaving)
            {
                if (meowSource != null && meowClips != null && meowClips.Length > 0)
                {
                    AudioClip clip = PickMeowClip();
                    if (clip != null)
                    {
                        meowSource.pitch = Random.Range(0.97f, 1.05f);
                        meowSource.PlayOneShot(clip, Mathf.Clamp01(meowVolume));

                        // 张嘴跟着这一声走
                        _jawDur = meowJawSeconds > 0f ? meowJawSeconds : clip.length;
                        _jawT = 0f;
                    }
                }

                float wait = Random.Range(Mathf.Min(meowIntervalMin, meowIntervalMax),
                                          Mathf.Max(meowIntervalMin, meowIntervalMax));
                yield return new WaitForSeconds(Mathf.Max(0.5f, wait));
            }
        }

        int _lastMeow = -1;

        /// <summary>随机挑一个叫声，但绝不连续两次用同一段（否则听起来像卡带）。</summary>
        AudioClip PickMeowClip()
        {
            int n = meowClips.Length;
            if (n == 1) return meowClips[0];

            int pick = Random.Range(0, n);
            if (pick == _lastMeow) pick = (pick + 1 + Random.Range(0, n - 1)) % n;
            _lastMeow = pick;
            return meowClips[pick];
        }

        void OnPlayerGetUpFinished()
        {
            if (!IsLocked || IsLeaving) return;
            StartCoroutine(LeaveBedRoutine());
        }

        IEnumerator LeaveBedRoutine()
        {
            if (cat == null) { IsLeaving = false; yield break; }

            IsLeaving = true;
            _settling = false;
            StopMeowing();

            Vector3 from = cat.position;
            Vector3 to = floorExitPosition != null
                ? floorExitPosition.position
                : from + new Vector3(0.9f, -0.7f, 0f);

            // 落点必须真的在地面上：向下射线校正 Y，避免半空落地或者陷进地板
            float groundY;
            if (CatPoseUtility.TryGetSurfaceY(cat, new Vector3(to.x, to.y + 1.2f, to.z), 2.4f,
                                              surfaceMask, out groundY))
            {
                to = new Vector3(to.x, groundY, to.z);
            }

            // 朝落点方向
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                cat.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            // 播一段起跳动作（CatAgent 关着，直接推 Animator）
            if (animator != null && !string.IsNullOrEmpty(leapStateName))
                animator.CrossFade(Animator.StringToHash(leapStateName), 0.12f, 0, 0f);

            // 先让 Pounce 的前段（压低重心/蹬地）在原地播完，再开始位移，
            // 否则一按 E 猫就平移出去，像被拖走
            if (crouchDelay > 0f) yield return new WaitForSeconds(crouchDelay);

            float t = 0f;
            float dur = Mathf.Max(0.2f, hopDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);

                Vector3 p = Vector3.Lerp(from, to, Smooth(k));
                p.y += Mathf.Sin(k * Mathf.PI) * hopArc;
                cat.position = p;

                yield return null;
            }

            cat.position = to;

            if (handoverDelay > 0f) yield return new WaitForSeconds(handoverDelay);

            // ---- 落地，交还 AI（先解锁再启用，避免启用的那一帧被旧锁态挡住）
            IsLocked = false;
            IsLeaving = false;

            if (catAgent != null)
            {
                catAgent.SetTransformLocked(false);
                catAgent.enabled = true;
                catAgent.SnapToFloor();
            }

            // ---- 交还：逐项还原锁定前的状态，绝不在落地那一帧硬开/硬关组件
            //      （历史 bug：无条件打开 CatLookAt 会让头在落地瞬间猛甩过去）
            if (bodyMotion != null) bodyMotion.enabled = _savedBodyMotion;
            if (catBell != null) catBell.enabled = _savedBell;
            if (lookAt != null)
            {
                lookAt.headTiltSwayDegrees = _savedTiltSway;
                lookAt.headNodDegrees = _savedNod;
                lookAt.headTiltDegrees = _savedTilt;
                lookAt.enabled = enableLookAtAfterHandover && _savedLookAt;
            }
            _hasSaved = false;
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>重新开始开场时复位。</summary>
        public void ResetOpening()
        {
            StopAllCoroutines();
            _settling = false;
            EnterOpeningLock();
        }
    }
}
