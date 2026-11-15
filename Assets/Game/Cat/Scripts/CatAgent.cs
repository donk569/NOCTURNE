using System.Collections.Generic;
using NOCTURNE.Nav;
using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 黑猫行为驱动器：把 <see cref="CatBrain"/> 的决策落到动画、寻路与位移上。
    ///
    /// 8 段动画（Idle / Walk / Sit / Sleep / Eat / Drink / Alert / Pounce）都从
    /// 同一个 Animator 控制器里按名字交叉淡入，不依赖任何状态机连线，
    /// 便于在代码里精确控制时机。
    ///
    /// 位移部分（v2）：
    /// - 不再朝「远离玩家」的方向直线硬推，而是走 <see cref="NavGrid"/> 的室内网格 + A*；
    /// - 所有目标点都先落在室内可行走格子上，寻路失败就取消本次移动，绝不改 Transform 硬塞；
    /// - 路线要穿门时先走到合页安全半径外，调 <see cref="ICatDoor.SetOpen"/> 开门，
    ///   等 <see cref="ICatDoor.IsFullyOpen"/> 才继续（门外/窗/柜门在门脚本里已经标成"猫不能开"）；
    /// - 同一时间只有一个目标，走到才换下一个，避免来回抽搐。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class CatAgent : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("玩家 Transform，留空则自动按 Tag 查找")]
        public Transform player;

        [Tooltip("头部注视目标，留空则注视玩家")]
        public Transform lookTarget;

        [Header("感知")]
        [Tooltip("感知刷新间隔（秒），不必每帧做射线")]
        public float senseInterval = 0.12f;

        [Tooltip("是否要求玩家在视线内才算「看见」")]
        public bool requireLineOfSight = true;

        public LayerMask sightBlockMask = ~0;

        [Header("移动")]
        [Tooltip("关掉后猫原地不动（纯氛围摆件）")]
        public bool canMove = true;

        [Tooltip("逃跑移动速度 m/s")]
        public float fleeSpeed = 0.9f;

        [Tooltip("闲逛移动速度 m/s。这个值要跟 Walk 剪辑的爪子速度匹配，否则脚底打滑")]
        public float walkSpeed = 0.34f;

        [Tooltip("Walk 剪辑里爪子的自然速度（m/s）= 步幅 / (支撑相占比 × 周期)。" +
                 "换动画后要跟着改，运行时用它换算播放倍速")]
        public float walkClipNaturalSpeed = 0.39f;

        [Tooltip("超过这个速度就切 Run 剪辑（m/s）。低于它用 Walk，两者必须能看出区别")]
        public float runAnimSpeed = 0.50f;

        [Tooltip("Run 剪辑里爪子的自然速度（m/s）= 步幅 / (支撑相占比 × 周期)")]
        public float runClipNaturalSpeed = 1.00f;

        [Tooltip("转向速度 度/秒（猫大约 200~260，太快会显得急刹）")]
        public float turnSpeed = 240f;

        [Tooltip("转向时对「目标方向」做低通的强度。路径点是 12cm 网格，不滤一下就一格一扭")]
        public float turnDirectionSharpness = 5.5f;

        [Tooltip("逃跑时与玩家的目标间距，避免贴脸")]
        public float keepDistance = 3.2f;

        [Header("室内活动范围（必须落在房屋内部，阳台要在外面）")]
        [Tooltip("可行走区域中心（世界坐标，Y 用地面高度）")]
        public Vector3 boundsCenter = new Vector3(0f, 0f, -1.5f);

        [Tooltip("可行走区域尺寸（X×Z 是水平范围）。区域越出房屋，猫就可能在室外选点")]
        public Vector3 boundsSize = new Vector3(4.0f, 3.0f, 9.0f);

        [Tooltip("障碍物遮挡层级（墙壁/家具），寻路网格与感知射线共用")]
        public LayerMask obstacleMask = ~0;

        [Tooltip("猫身半径（米），寻路网格按这个体积做重叠检测")]
        public float bodyRadius = 0.15f;

        [Tooltip("猫身高度（米）")]
        public float navHeight = 0.34f;

        [Tooltip("寻路格子边长（米）。越小越贴合家具，烘焙越慢")]
        public float navCellSize = 0.12f;

        [Header("闲逛节奏")]
        [Tooltip("两次闲逛之间的最短间隔（秒）")]
        public float roamIntervalMin = 4f;

        [Tooltip("两次闲逛之间的最长间隔（秒）")]
        public float roamIntervalMax = 10f;

        [Tooltip("一次移动的绝对上限（秒）。猫只有 0.2m/s，跨屋路线可能要 30s，所以给得很宽")]
        public float maxWalkTime = 120f;

        [Tooltip("卡住判定：多少秒没有走到下一个路径点就放弃。比按总时长判断更贴近真卡住。" +
                 "小跑速度慢、路径点密时容易误判，给 6s")]
        public float stuckTimeout = 6f;

        [Tooltip("接近目标点开始减速的半径（米）")]
        public float slowDownRadius = 0.45f;

        [Tooltip("到达判定半径（米）。要小于寻路格子边长，否则会提前拐弯切到被挡的格子")]
        public float arriveDistance = 0.08f;

        [Tooltip("等门打开的超时（秒），超时就放弃这个目标")]
        public float doorOpenTimeout = 3f;

        [Tooltip("门板宽度小于这个值就不当成猫能过的门（衣柜门 0.5m 左右，室内门 0.98m）")]
        public float minDoorWidth = 0.7f;

        [Header("定点目标（可选）")]
        [Tooltip("食物碗 / 水碗 / 猫窝 / 躲藏点。留空则只在室内随机闲逛")]
        public CatSpot[] spots = new CatSpot[0];

        [Tooltip("有定点目标时，走到定点的概率")]
        [Range(0f, 1f)] public float spotChance = 0.7f;

        [Header("饥饿 / 进食（可选）")]
        [Tooltip("启用饥饿需求：饿了会优先去食盆")]
        public bool useHunger = true;

        [Tooltip("食盆停留点（场景里的 FoodPoint）。留空则不会去吃饭")]
        public CatSpot foodSpot;

        [Tooltip("饥饿值增长速度（每秒）。0.008 → 约 2 分钟饿一次")]
        public float hungerPerSecond = 0.009f;

        [Tooltip("饥饿值到这个值以上才去吃饭")]
        [Range(0.1f, 1f)] public float hungerEatThreshold = 0.6f;

        [Tooltip("吃完之后的饥饿值")]
        [Range(0f, 0.5f)] public float hungerAfterEating = 0.08f;

        [Tooltip("一次进食时长（秒）")]
        public float eatDuration = 5f;

        [Header("扑击")]
        [Tooltip("一次扑击在 Unity 侧前冲的距离（米）")]
        public float pounceForwardDistance = 0.55f;

        [Header("调试")]
        [Tooltip("在 Scene 视图里画出可行走格子")]
        public bool debugDrawNav = false;

        [Header("动画")]
        [Range(0.02f, 0.6f)]
        public float crossFade = 0.18f;

        [Header("脚本化移动（开场喂食流程）")]
        [Tooltip("脚本化移动时动画倍速上限。小跑速度比 Walk 剪辑自然速度高，需要放宽上限，" +
                 "否则会被夹回 1.6 倍导致脚底打滑")]
        public float scriptedAnimSpeedCap = 3.4f;

        /// <summary>决策核心，外部可以读状态、也能强推一次性动作。</summary>
        public CatBrain Brain { get; private set; }

        public CatMood Mood { get { return Brain != null ? Brain.Mood : CatMood.Idle; } }

        /// <summary>是否被开场流程锁住了位置（锁住时本组件完全不碰 Transform）。</summary>
        public bool IsTransformLocked { get { return _transformLocked; } }

        /// <summary>寻路网格是否可用。</summary>
        public bool IsNavigationReady { get { return _nav != null && _nav.Ready; } }

        /// <summary>状态变化事件：(旧状态, 新状态)。</summary>
        public event System.Action<CatMood, CatMood> MoodChanged;

        /// <summary>扑击起跳瞬间触发，接音效/震动。</summary>
        public event System.Action Pounced;

        /// <summary>
        /// 脚本化移动结束。参数 true = 到达目标，false = 寻路失败/卡住/被放弃。
        /// 开场喂食流程用它串顺序，不轮询状态。
        /// </summary>
        public event System.Action<bool> ScriptedPathEnded;

        /// <summary>AI 是否被外部挂起（挂起时只走脚本化路径，不读取感知、不闲逛）。</summary>
        public bool IsAiSuspended { get { return _aiSuspended; } }

        /// <summary>本帧的实际位移速度（m/s）。调试与验收脚本读它来看动画和位移是否匹配。</summary>
        public float CurrentSpeed { get { return _currentSpeed; } }

        /// <summary>内部状态快照，只给调试/自测用。出问题时能一眼看出卡在哪一层。</summary>
        public string DebugState
        {
            get
            {
                return string.Format(
                    "locked={0} susp={1} poseHold={2} path={3} idx={4}/{5} moving={6} speed={7:F2} " +
                    "brain={8} applied={9} animSpeed={10:F2} nav={11}",
                    _transformLocked, _aiSuspended, _poseHold, _pathKind, _pathIndex, _path.Count,
                    _moving, _currentSpeed, Brain != null ? Brain.Mood : CatMood.Idle,
                    (int)_appliedMood, _anim != null ? _anim.speed : -1f, IsNavigationReady);
            }
        }

        // ---------------------------------------------------------------- 内部状态

        enum PathKind { None, Flee, Roam, Scripted }

        enum RoamPhase { Wait, Move, Act }

        Animator _anim;
        Transform _headBone;

        readonly Dictionary<CatMood, float> _clipLength = new Dictionary<CatMood, float>();
        readonly HashSet<CatMood> _hasClip = new HashSet<CatMood>();

        CatMood _appliedMood = (CatMood)(-1);
        float _senseTimer;
        bool _playerVisible;
        bool _playerLooking;

        // 扑击位移
        bool _pouncing;
        float _pounceT;
        float _pounceDur;
        Vector3 _pounceFrom;
        Vector3 _pounceTo;
        bool _pounceFired;

        // 寻路
        NavGrid _nav;
        readonly List<CatDoorway> _doorways = new List<CatDoorway>();
        readonly List<Vector3> _path = new List<Vector3>();
        readonly List<Vector3> _scratchPath = new List<Vector3>();
        int _pathIndex;
        int _lastPathIndex;
        float _stuckTimer;
        Vector3 _stuckAnchorPos;
        float _pathTime;
        Vector3 _pathGoal = new Vector3(9999f, 9999f, 9999f);
        PathKind _pathKind = PathKind.None;
        bool _moving;

        ICatDoor _openingDoor;
        CatDoorway _pendingDoor;
        float _doorWait;

        // 闲逛调度
        RoamPhase _roamPhase = RoamPhase.Wait;
        float _roamTimer;
        float _fleeTimer;
        int _roamFailures;
        int _lastSpot = -1;
        CatMood _goalMood = CatMood.Idle;
        float _goalActionTime = 1.2f;
        float _currentSpeed;

        // 转向用的"当前想朝的方向"（低通后的），避免网格点造成一格一扭
        Vector3 _faceDir = Vector3.forward;
        bool _faceDirInit;

        // 目标（定点 / 饥饿）
        CatSpot _goalSpot;
        bool _goalViaApproach;
        float _hunger;
        bool _eating;

        bool _transformLocked;

        // 脚本化移动（开场喂食流程独占）
        bool _aiSuspended;
        bool _poseHold;             // 外部指定了姿势（吃/睡/警觉），不允许被 Idle 覆盖
        float _scriptedSpeed;
        int _scriptedRepaths;       // 本次脚本化移动已经就地重规划过几次

        // ---------------------------------------------------------------- 生命周期

        void Awake()
        {
            _anim = GetComponent<Animator>();
            Brain = new CatBrain();
            CacheClipLengths();
            FindHeadBone();

            if (player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p != null) player = p.transform;
            }
            if (lookTarget == null) lookTarget = player;
        }

        void OnEnable()
        {
            _appliedMood = (CatMood)(-1);
            StopPath();

            // 被开场流程禁用时不会走 Start，重新启用时补建一次
            if (!IsNavigationReady) BuildNavigation();

            _roamPhase = RoamPhase.Wait;
            _roamTimer = Random.Range(1.5f, 3f);
        }

        void OnDisable()
        {
            StopPath();
        }

        void Start()
        {
            if (!IsNavigationReady) BuildNavigation();
        }

        void CacheClipLengths()
        {
            _clipLength.Clear();
            _hasClip.Clear();
            var rac = _anim.runtimeAnimatorController;
            if (rac == null) return;
            var clips = rac.animationClips;

            // 按剪辑表长度遍历，不要用 m <= (int)CatMood.Pounce：
            // Run = 8 排在 Pounce = 7 后面，用 Pounce 当上界会把 Run 漏出缓存，
            // HasClip(Run) 恒为 false → 跑动时 CrossFade 根本不发，猫用 Idle 姿势滑过去。
            for (int m = 0; m < CatClips.BY_MOOD.Length; m++)
            {
                var mood = (CatMood)m;
                string want = CatClips.Of(mood);
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] == null) continue;
                    if (clips[i].name == want)
                    {
                        _clipLength[mood] = clips[i].length;
                        _hasClip.Add(mood);
                        break;
                    }
                }
            }
        }

        /// <summary>控制器里是否存在该状态的剪辑。</summary>
        public bool HasClip(CatMood mood)
        {
            // 注意：不能用 Animator.HasState —— 它在 Animator 完成初始化前恒为 false，
            // 编辑器里做静态检查会全部误判。
            return _hasClip.Count == 0 || _hasClip.Contains(mood);
        }

        void FindHeadBone()
        {
            _headBone = null;
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == CatBones.HEAD) { _headBone = all[i]; break; }
            }
        }

        /// <summary>头部骨骼，供 <see cref="CatLookAt"/> 之类的脚本复用。</summary>
        public Transform HeadBone { get { return _headBone; } }

        // ---------------------------------------------------------------- 主循环

        void Update()
        {
            // 开场趴卧期间位置由 CatOpeningController 独占，这里一个字节都不写
            if (_transformLocked) return;

            // 喂食流程接管期间：只沿脚本化路径走，不读感知、不闲逛、不抢姿势
            if (_aiSuspended)
            {
                TickSuspended();
                return;
            }

            TickHunger();

            UpdateSenses();
            CatMood mood = Brain.Tick(BuildSenses(), Time.deltaTime);

            UpdateLocomotion(mood);

            CatMood anim = mood;
            if (_moving && (mood == CatMood.Idle || mood == CatMood.Alert))
                anim = _currentSpeed > runAnimSpeed ? CatMood.Run : CatMood.Walk;

            ApplyMood(anim);
            EnsureAnimatorState(anim);
            SyncAnimatorSpeed(anim);
            UpdatePounce();
        }

        /// <summary>
        /// AI 挂起时的一帧：只做「沿脚本路径前进」和「走路动画」，
        /// 姿势锁定（吃/睡/警觉）期间连动画都不覆盖。
        /// </summary>
        void TickSuspended()
        {
            _moving = false;
            _currentSpeed = 0f;

            if (_poseHold)
            {
                // 姿势由外部指定（进食/睡觉/警觉），这里不写 Animator
                EnsureAnimatorState(_appliedMood);
                return;
            }

            if (_pathKind == PathKind.Scripted && _path.Count > 0 && _pathIndex < _path.Count)
                FollowPath(_scriptedSpeed);

            CatMood anim = _moving
                ? (_currentSpeed > runAnimSpeed ? CatMood.Run : CatMood.Walk)
                : CatMood.Idle;
            ApplyMood(anim);
            EnsureAnimatorState(anim);
            SyncAnimatorSpeed(anim);
        }

        void TickHunger()
        {
            if (!useHunger) return;

            // 进食结束（brain 的一次性动作锁释放）就结算。
            // 放在这里而不是只在 EndAct 里做，是因为吃到一半被玩家吓走时
            // RoamTick 会被 Flee 顶掉，EndAct 根本不会执行。
            if (_eating && (Brain == null || !Brain.IsLocked))
            {
                _hunger = Mathf.Clamp01(hungerAfterEating);
                _eating = false;
            }

            if (_eating) return;
            _hunger = Mathf.Clamp01(_hunger + hungerPerSecond * Time.deltaTime);
        }

        /// <summary>
        /// 让 Walk 的播放倍速跟实际位移速度对齐 —— 这是"脚底打滑"的正解：
        /// 剪辑本身有一个爪子速度（walkClipNaturalSpeed），位移比它快多少就播快多少。
        /// 逃跑比自然速度高太多，所以上限截到 2.2 倍，宁可轻微打滑也不要腿部抽搐。
        /// </summary>
        void SyncAnimatorSpeed(CatMood anim)
        {
            if (_anim == null) return;

            float want = 1f;
            if (anim == CatMood.Walk && _currentSpeed > 0.01f)
            {
                // 闲逛/逃跑按 1.6 倍封顶；脚本化小跑允许放宽到 scriptedAnimSpeedCap，
                // 否则 0.55m/s 的小跑会被夹回 1.6 倍，脚底明显打滑
                float cap = _pathKind == PathKind.Scripted
                    ? Mathf.Max(1.6f, scriptedAnimSpeedCap)
                    : 1.6f;
                want = Mathf.Clamp(_currentSpeed / Mathf.Max(0.02f, walkClipNaturalSpeed),
                                   0.7f, cap);
            }
            else if (anim == CatMood.Run && _currentSpeed > 0.01f)
            {
                // 奔跑同样要对齐，否则 Run 剪辑 1 倍速播放、身体却只有一半速度 → 明显打滑
                want = Mathf.Clamp(_currentSpeed / Mathf.Max(0.02f, runClipNaturalSpeed),
                                   0.6f, 1.8f);
            }

            if (Mathf.Abs(_anim.speed - want) > 0.01f) _anim.speed = want;
        }

        // ---------------------------------------------------------------- 感知

        void UpdateSenses()
        {
            _senseTimer -= Time.deltaTime;
            if (_senseTimer > 0f) return;
            _senseTimer = senseInterval;

            if (player == null)
            {
                _playerVisible = false;
                _playerLooking = false;
                return;
            }

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            _playerVisible = true;
            if (requireLineOfSight && dist > 0.01f)
            {
                Vector3 eye = transform.position + Vector3.up * 0.22f;
                Vector3 target = player.position + Vector3.up * 1.4f;
                if (Physics.Linecast(eye, target, sightBlockMask, QueryTriggerInteraction.Ignore))
                    _playerVisible = false;
            }

            // 玩家是否朝我（把玩家的前向与「玩家→猫」的方向做点积）
            Vector3 pf = player.forward;
            pf.y = 0f;
            Vector3 dirToMe = -toPlayer.normalized;
            _playerLooking = pf.sqrMagnitude > 0.001f && dist < 8f &&
                             Vector3.Dot(pf.normalized, dirToMe) > 0.72f;
        }

        CatSenses BuildSenses()
        {
            if (player == null) return CatSenses.Far;

            Vector3 d = player.position - transform.position;
            d.y = 0f;
            return new CatSenses
            {
                playerDistance = d.magnitude,
                playerVisible = _playerVisible,
                playerLookingAtMe = _playerLooking
            };
        }

        // ---------------------------------------------------------------- 动画

        void ApplyMood(CatMood mood)
        {
            if (mood == _appliedMood) return;

            var old = _appliedMood;
            _appliedMood = mood;
            PlayClip(mood);

            if (MoodChanged != null && (int)old >= 0)
                MoodChanged(old, mood);
        }

        void PlayClip(CatMood mood)
        {
            if (!HasClip(mood)) return;
            int hash = Animator.StringToHash(CatClips.Of(mood));
            _anim.CrossFade(hash, crossFade, 0, 0f);
        }

        /// <summary>
        /// 兜底：Animator 初始化的第一帧会吞掉 CrossFade（实测确认），
        /// 导致状态机停在默认的 Cat_Idle。这里每帧核对一次，不一致且不在过渡中
        /// 就补发一次，成本可忽略。
        /// </summary>
        void EnsureAnimatorState(CatMood mood)
        {
            if (!HasClip(mood)) return;
            if (_anim.IsInTransition(0)) return;

            int want = Animator.StringToHash(CatClips.Of(mood));
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash == want) return;

            _anim.CrossFade(want, crossFade, 0, 0f);
        }

        // ---------------------------------------------------------------- 寻路网格

        /// <summary>重建室内网格（运行时改了 bounds / 摆了新家具可以手动调一次）。</summary>
        [ContextMenu("重建室内网格")]
        public void BuildNavigation()
        {
            CollectDoors();

            if (_nav == null) _nav = new NavGrid();

            Vector3 center = new Vector3(boundsCenter.x, boundsCenter.y, boundsCenter.z);
            Vector3 size = new Vector3(Mathf.Abs(boundsSize.x), 0f, Mathf.Abs(boundsSize.z));

            bool ok = _nav.Build(center, size, navCellSize, Mathf.Max(0.05f, bodyRadius),
                                 Mathf.Max(0.1f, navHeight), obstacleMask, transform, _doorways);

            if (!ok)
            {
                Debug.LogError("[CatAgent] 室内寻路网格烘焙失败（可行走格子不足）。" +
                               "检查 boundsCenter / boundsSize 是否覆盖了房屋内部、obstacleMask 是否包含墙体层。" +
                               "网格不可用时猫会原地不动，不会乱跑。", this);
                return;
            }

            int passable = 0;
            for (int i = 0; i < _doorways.Count; i++) if (_doorways[i].Passable) passable++;

            if (_doorways.Count > 0 && passable == 0)
            {
                Debug.LogWarning(string.Format(
                    "[CatAgent] 场景里有 {0} 扇门，但没有一扇被判定为「猫可通行」（门板宽度需 ≥ {1:F2}m，且 catCanOpen=true）。" +
                    "猫将无法跨房间活动。检查 HingedDoor / SlidingDoor 的勾选与门板包围盒。",
                    _doorways.Count, minDoorWidth), this);
            }

            Debug.Log(string.Format(
                "[CatAgent] 室内网格就绪：{0} 个可行走格子，识别到 {1} 扇猫可通行的门（共 {2} 扇门）",
                _nav.WalkableCount, passable, _doorways.Count), this);
        }

        void CollectDoors()
        {
            _doorways.Clear();

            // 猫的程序集看不到 Assembly-CSharp 里的门脚本，所以按接口找，不拖任何引用
            var all = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var door = all[i] as ICatDoor;
                if (door == null) continue;
                if (door.DoorTransform == null) continue;

                var dw = new CatDoorway(door);
                // 设计师开关 + 宽度合理性：衣柜门、装饰小门不会被当成通道
                dw.Passable = door.CatCanOpen && dw.Width >= minDoorWidth;
                _doorways.Add(dw);
            }
        }

        // ---------------------------------------------------------------- 位移调度

        void UpdateLocomotion(CatMood mood)
        {
            _moving = false;
            _currentSpeed = 0f;

            if (!canMove || !IsNavigationReady) return;
            if (Brain.IsLocked || _pouncing) return;

            switch (mood)
            {
                case CatMood.Walk:
                    FleeTick();
                    break;
                case CatMood.Idle:
                case CatMood.Alert:
                    RoamTick();
                    break;
                default:
                    // Sleep：停住，等下一轮决策
                    break;
            }
        }

        void FleeTick()
        {
            _fleeTimer -= Time.deltaTime;

            bool noPath = _pathKind != PathKind.Flee || _path.Count == 0 || _pathIndex >= _path.Count;
            if (noPath)
            {
                if (_fleeTimer > 0f) return;

                if (TryPickFleeTarget())
                {
                    _fleeTimer = 0.8f;
                }
                else
                {
                    // 无路可逃就原地站住，不要原地抽搐
                    StopPath();
                    _fleeTimer = 0.5f;
                    return;
                }
            }

            FollowPath(fleeSpeed * 0.9f);
        }

        bool TryPickFleeTarget()
        {
            if (player == null) return false;

            Vector3 away = transform.position - player.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0004f) away = -transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0004f) away = Vector3.forward;
            away.Normalize();

            float[] dists = { Mathf.Max(1.6f, keepDistance), 1.6f };
            float[] angles = { 0f, 30f, -30f, 60f, -60f, 95f, -95f, 135f, -135f, 180f };

            for (int d = 0; d < dists.Length; d++)
            {
                for (int a = 0; a < angles.Length; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, angles[a], 0f) * away;
                    Vector3 want = transform.position + dir * dists[d];
                    if (TryStartPath(want, PathKind.Flee)) return true;
                }
            }
            return false;
        }

        void RoamTick()
        {
            switch (_roamPhase)
            {
                case RoamPhase.Wait:
                    _roamTimer -= Time.deltaTime;
                    if (_roamTimer <= 0f) BeginRoamGoal();
                    break;

                case RoamPhase.Move:
                    FollowPath(walkSpeed);
                    break;

                case RoamPhase.Act:
                    FaceActTarget();
                    if (Brain.IsLocked) return;     // 动作还没播完
                    _roamTimer -= Time.deltaTime;
                    if (_roamTimer <= 0f) EndAct();
                    break;
            }
        }

        /// <summary>到位后如果配了 faceTarget（比如饭盆本体），转过去面对它。</summary>
        void FaceActTarget()
        {
            if (_goalSpot == null || _goalSpot.faceTarget == null) return;
            Vector3 dir = _goalSpot.faceTarget.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) FaceDirection(dir.normalized);
        }

        void EndAct()
        {
            // 吃完才结算饥饿
            if (_eating)
            {
                _hunger = Mathf.Clamp01(hungerAfterEating);
                _eating = false;
            }

            _goalSpot = null;
            _roamPhase = RoamPhase.Wait;
            _roamTimer = Random.Range(roamIntervalMin, roamIntervalMax);
        }

        /// <summary>
        /// 取下一个目标。优先级：饥饿（食盆） &gt; 定点（猫窝/躲藏点…） &gt; 室内随机点。
        /// 只在 Wait 阶段取目标 —— 移动中不重新生成，避免来回抽搐。
        /// </summary>
        void BeginRoamGoal()
        {
            // 1) 饿了：直奔食盆
            if (IsHungry() && BeginSpotGoal(foodSpot)) return;

            // 2) 定点目标
            CatSpot spot = PickSpot();
            if (spot != null && BeginSpotGoal(spot)) return;

            // 3) 室内随机
            if (BeginRandomGoal()) return;

            _roamTimer = 1.5f;
        }

        bool IsHungry()
        {
            return useHunger && foodSpot != null && foodSpot.point != null &&
                   _hunger >= hungerEatThreshold;
        }

        CatSpot PickSpot()
        {
            if (spots == null || spots.Length == 0) return null;
            if (Random.value > spotChance) return null;

            int idx = PickSpotIndex();
            if (idx < 0) return null;

            _lastSpot = idx;
            return spots[idx];
        }

        bool BeginSpotGoal(CatSpot spot)
        {
            if (spot == null || spot.point == null) return false;

            // 配了接近点就先走接近点，到了自动续第二段（不要一路直接怼到饭盆中心）
            bool viaApproach = spot.approach != null &&
                               FlatDistance(spot.approach.position, spot.point.position) > 0.05f;
            Vector3 target = viaApproach ? spot.approach.position : spot.point.position;

            if (!TryStartPath(target, PathKind.Roam))
            {
                RegisterGoalFailure();
                return false;
            }

            _goalSpot = spot;
            _goalViaApproach = viaApproach;
            _goalMood = spot.mood;
            _goalActionTime = Mathf.Max(0.8f, spot.actSeconds);
            _roamPhase = RoamPhase.Move;
            _roamFailures = 0;
            return true;
        }

        bool BeginRandomGoal()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector3 p;
                if (!_nav.TryGetRandomPoint(out p)) continue;
                if (!FarEnough(p, 1.0f)) continue;
                if (!TryStartPath(p, PathKind.Roam)) continue;

                _goalSpot = null;
                _goalViaApproach = false;
                _goalMood = CatMood.Idle;
                _goalActionTime = 1.2f;
                _roamPhase = RoamPhase.Move;
                _roamFailures = 0;
                return true;
            }

            RegisterGoalFailure();
            return false;
        }

        void RegisterGoalFailure()
        {
            _roamFailures++;
            _roamTimer = Mathf.Min(4f, 0.6f + 0.5f * _roamFailures);
            if (_roamFailures >= 3)
            {
                _roamFailures = 0;
                _roamTimer = 3.5f;
            }
        }

        int PickSpotIndex()
        {
            float total = 0f;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i] == null || spots[i].point == null) continue;
                if (spots.Length > 1 && i == _lastSpot) continue;
                total += Mathf.Max(0f, spots[i].weight);
            }
            if (total <= 0.0001f) return -1;

            float pick = Random.value * total;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i] == null || spots[i].point == null) continue;
                if (spots.Length > 1 && i == _lastSpot) continue;
                pick -= Mathf.Max(0f, spots[i].weight);
                if (pick <= 0f) return i;
            }
            return -1;
        }

        bool FarEnough(Vector3 p, float minDistance)
        {
            Vector3 a = p;
            Vector3 b = transform.position;
            a.y = 0f;
            b.y = 0f;
            if (Vector3.Distance(a, b) < minDistance) return false;

            Vector3 g = _pathGoal;
            g.y = 0f;
            return Vector3.Distance(a, g) > minDistance * 0.5f;
        }

        // ---------------------------------------------------------------- 路径跟随

        bool TryStartPath(Vector3 goal, PathKind kind)
        {
            Vector3 snapped;
            if (!_nav.TrySnap(goal, 0.9f, out snapped))
            {
                if (kind == PathKind.Scripted)
                    Debug.LogWarning("[CatAgent] 脚本化移动：目标点附近 0.9m 内没有可行走格子 " +
                                     goal.ToString("F2"), this);
                return false;
            }

            if (!_nav.IsWalkable(snapped))
            {
                if (kind == PathKind.Scripted)
                    Debug.LogWarning("[CatAgent] 脚本化移动：吸附点不可走 " + snapped.ToString("F2"), this);
                return false;
            }

            _scratchPath.Clear();
            if (!_nav.FindPath(transform.position, snapped, _scratchPath))
            {
                if (kind == PathKind.Scripted)
                    Debug.LogWarning("[CatAgent] 脚本化移动：从 " + transform.position.ToString("F2") +
                                     " 到 " + snapped.ToString("F2") + " 不连通（A* 无解）", this);
                return false;
            }

            if (_scratchPath.Count == 0) return false;

            _path.Clear();
            for (int i = 0; i < _scratchPath.Count; i++) _path.Add(_scratchPath[i]);

            _pathIndex = 0;
            _lastPathIndex = -1;
            _stuckTimer = 0f;
            _stuckAnchorPos = transform.position;
            _pathGoal = snapped;
            _pathKind = kind;
            _pathTime = 0f;
            _openingDoor = null;
            _pendingDoor = null;
            _doorWait = 0f;

            // 逃跑优先于闲逛：把闲逛调度挂起，别让旧目标在威胁解除后又被接着走
            if (kind == PathKind.Flee)
            {
                _roamPhase = RoamPhase.Wait;
                _roamTimer = Random.Range(roamIntervalMin, roamIntervalMax);
            }
            return true;
        }

        void FollowPath(float speed)
        {
            // ---- 正在等门开
            if (_openingDoor != null)
            {
                _doorWait += Time.deltaTime;

                if (_openingDoor.IsFullyOpen)
                {
                    _openingDoor = null;
                    _pendingDoor = null;
                    _doorWait = 0f;
                }
                else if (_doorWait > doorOpenTimeout)
                {
                    Debug.LogWarning("[CatAgent] 等门超时，放弃当前目标", this);
                    _openingDoor = null;
                    _pendingDoor = null;
                    AbandonPath();
                    return;
                }
                else
                {
                    return;     // 门没开到位，一步都不走
                }
            }

            if (_path.Count == 0 || _pathIndex >= _path.Count)
            {
                FinishPath();
                return;
            }

            _pathTime += Time.deltaTime;
            if (_pathTime > maxWalkTime)
            {
                AbandonPath();
                return;
            }

            // 卡住判定：只看"有没有推进到下一个路径点"，跟速度快慢解耦
            if (_pathIndex != _lastPathIndex)
            {
                _lastPathIndex = _pathIndex;
                _stuckTimer = 0f;
            }
            else
            {
                _stuckTimer += Time.deltaTime;

                // "卡住"的真正定义是"身体没在动"，不是"路径点没推进"：
                // 沿墙滑行、绕门框时路径点本来就会停一会儿，但猫在移动，不能算卡住。
                if (FlatDistance(transform.position, _stuckAnchorPos) > 0.06f)
                {
                    _stuckTimer = 0f;
                    _stuckAnchorPos = transform.position;
                }

                // 卡住判定的时限跟着"离下一个路径点还有多远"走：
                // 平滑后的路径段可能长 1~2m，小跑 0.4m/s 要 3~5s 才走完，
                // 用固定 2s 会把"正常在走"误判成卡住。
                float stuckLimit = stuckTimeout;
                if (_pathKind == PathKind.Scripted && _pathIndex < _path.Count)
                {
                    float remain = FlatDistance(transform.position, _path[_pathIndex]);
                    float expect = remain / Mathf.Max(0.05f, _scriptedSpeed);
                    stuckLimit = Mathf.Clamp(expect * 2.5f, 1.5f, stuckTimeout);
                }

                if (_stuckTimer > stuckLimit)
                {
                    // 脚本化移动（开场喂食流程）：先自愈，再考虑放弃。
                    // 门口/拐角被门框挤住时通常只差几厘米，把身体推回最近的可走格
                    // 再重新规划就能过去 —— 直接放弃会让玩家看到猫卡在门里不动。
                    if (_pathKind == PathKind.Scripted && _scriptedRepaths < 4)
                    {
                        _scriptedRepaths++;
                        _stuckTimer = 0f;

                        Vector3 fixedPos;
                        if (_nav.TrySnap(transform.position, 0.3f, out fixedPos))
                            transform.position = new Vector3(fixedPos.x, _nav.FloorHeight(fixedPos), fixedPos.z);

                        if (TryStartPath(_pathGoal, PathKind.Scripted))
                        {
                            Debug.LogWarning("[CatAgent] 脚本化移动卡顿，已就地重规划 #" + _scriptedRepaths +
                                             "，目标 " + _pathGoal.ToString("F2") +
                                             "（门前=" + (_pendingDoor != null) + "）", this);
                            return;
                        }
                    }

                    Debug.LogWarning("[CatAgent] 移动卡住（" + stuckTimeout.ToString("F1") +
                                     "s 没推进），放弃当前目标。idx=" + _pathIndex + "/" + _path.Count +
                                     " 目标点=" + (_pathIndex < _path.Count ? _path[_pathIndex].ToString("F2") : "-") +
                                     " 当前=" + transform.position.ToString("F2") +
                                     " 当前速度=" + _currentSpeed.ToString("F2") +
                                     " 等门=" + (_openingDoor != null) +
                                     " 门前=" + (_pendingDoor != null) +
                                     " 门已开=" + (_pendingDoor != null && _pendingDoor.Door != null && _pendingDoor.Door.IsFullyOpen), this);
                    AbandonPath();
                    return;
                }
            }

            if (_pendingDoor == null) _pendingDoor = FindClosedDoorAhead();

            float budget = speed * Time.deltaTime;
            int guard = 0;

            while (budget > 0.0001f && guard++ < 8)
            {
                if (_pathIndex >= _path.Count) break;

                Vector3 pos = transform.position;
                Vector3 target = _path[_pathIndex];

                Vector3 to = target - pos;
                to.y = 0f;
                float dist = to.magnitude;

                if (dist <= arriveDistance)
                {
                    _pathIndex++;
                    continue;
                }

                // ---- 门前：退到合页安全半径之外再开，避免被门板扫到
                if (_pendingDoor != null && !_pendingDoor.Door.IsFullyOpen)
                {
                    // 门在运行时被上锁：这次目标作废，别硬穿
                    if (!_pendingDoor.Door.CatCanOpen)
                    {
                        AbandonPath();
                        return;
                    }

                    float fromPivot = FlatDistance(pos, _pendingDoor.Pivot);
                    if (fromPivot <= _pendingDoor.SafeRadius + 0.12f)
                    {
                        _pendingDoor.Door.SetOpen(true);
                        _openingDoor = _pendingDoor.Door;
                        _doorWait = 0f;
                        return;
                    }
                }

                Vector3 dir = to / dist;

                // 转弯时减速：朝向差越大走得越慢，避免"平移 + 瞬间扭头"
                float align = Vector3.Dot(transform.forward, dir);
                float speedScale = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((align + 0.25f) / 1.25f));

                // 接近终点时自然减速，不要一头撞到饭盆前急停
                // （走"接近点"那一段不减速，否则到点会一慢一快）
                if (slowDownRadius > 0.01f && !_goalViaApproach)
                {
                    float toGoal = FlatDistance(pos, _pathGoal);
                    if (toGoal < slowDownRadius)
                        speedScale *= Mathf.Lerp(0.32f, 1f, toGoal / slowDownRadius);
                }

                float stepLen = Mathf.Min(budget, dist) * speedScale;
                if (stepLen <= 1e-5f)
                {
                    if (_pathKind == PathKind.Scripted)
                        Debug.LogWarning("[CatAgent] 脚本化移动：单帧步长为 0。budget=" + budget.ToString("F4") +
                                         " dist=" + dist.ToString("F3") + " speedScale=" + speedScale.ToString("F3") +
                                         " speed=" + speed.ToString("F2"), this);
                    break;
                }

                Vector3 next = pos + dir * stepLen;

                // 门板实体挡路（比平面判定更硬的兜底）：停下来开门等门，绝不硬挤
                CatDoorway blocker = ClosedDoorBlocking(next);
                if (blocker != null)
                {
                    blocker.Door.SetOpen(true);
                    _openingDoor = blocker.Door;
                    _pendingDoor = blocker;
                    _doorWait = 0f;
                    _moving = false;
                    return;
                }

                if (_pendingDoor != null && !_pendingDoor.Door.IsFullyOpen &&
                    FlatDistance(next, _pendingDoor.Pivot) < _pendingDoor.SafeRadius)
                {
                    next = _pendingDoor.PushOutsideSwing(next);
                }

                if (!_nav.IsWalkable(next))
                {
                    // 贴着门框/墙角时，直线这一脚会踩进不可走格。
                    // 先在 8 邻域里找一个"可走且更接近目标"的格子滑过去（沿墙滑行），
                    // 而不是原地放弃 —— 猫挤过门框就是靠这个。
                    Vector3 slide;
                    if (FindSlideTarget(pos, target, out slide))
                    {
                        next = Vector3.MoveTowards(pos, slide, Mathf.Max(budget, 0.06f));
                        if (!_nav.IsWalkable(next)) next = slide;
                    }
                    else
                    {
                        if (_pathKind == PathKind.Scripted)
                            Debug.LogWarning("[CatAgent] 脚本化移动：下一步越界/被挡 " + next.ToString("F2") +
                                             "（当前 " + pos.ToString("F2") + "，路径点 " + target.ToString("F2") +
                                             "，门前=" + (_pendingDoor != null) + "）", this);
                        AbandonPath();
                        return;
                    }
                }

                FaceDirection(dir);
                transform.position = new Vector3(next.x, _nav.FloorHeight(next), next.z);
                budget -= stepLen;
                _moving = true;
                _currentSpeed = speed * speedScale;
            }

            if (_pathIndex >= _path.Count) FinishPath();
        }

        /// <summary>这一步会不会撞上"关着的、猫能开的"门板（物理重叠，最可靠的兜底）。</summary>
        CatDoorway ClosedDoorBlocking(Vector3 p)
        {
            for (int i = 0; i < _doorways.Count; i++)
            {
                CatDoorway dw = _doorways[i];
                if (dw == null || !dw.Passable || dw.Door == null) continue;
                if (dw.Door.IsFullyOpen) continue;
                if (!dw.Door.CatCanOpen) continue;   // 不能开的门由网格直接挡掉

                if (dw.Blocks(p, bodyRadius * 0.9f, navHeight)) return dw;
            }
            return null;
        }

        CatDoorway FindClosedDoorAhead()
        {
            if (_path.Count == 0) return null;

            int end = Mathf.Min(_path.Count - 1, _pathIndex + 5);
            Vector3 a = transform.position;

            for (int k = _pathIndex; k <= end; k++)
            {
                Vector3 b = _path[k];

                for (int i = 0; i < _doorways.Count; i++)
                {
                    CatDoorway dw = _doorways[i];
                    if (dw == null || !dw.Passable || dw.Door == null) continue;
                    if (dw.Door.IsFullyOpen) continue;
                    if (dw.Crosses(a, b, bodyRadius * 0.5f)) return dw;
                }

                a = b;
            }
            return null;
        }

        void FinishPath()
        {
            PathKind kind = _pathKind;
            CatMood mood = _goalMood;
            float act = _goalActionTime;
            CatSpot spot = _goalSpot;
            bool viaApproach = _goalViaApproach;

            StopPath();

            // 脚本化移动：到了就通知，交给流程脚本决定下一步，不触发闲逛动作
            if (kind == PathKind.Scripted)
            {
                if (ScriptedPathEnded != null) ScriptedPathEnded(true);
                return;
            }

            if (kind != PathKind.Roam) return;

            // 第一段（接近点）走完 → 立刻续第二段（最终点）
            if (spot != null && viaApproach && spot.point != null)
            {
                if (TryStartPath(spot.point.position, PathKind.Roam))
                {
                    _goalSpot = spot;
                    _goalViaApproach = false;
                    _goalMood = mood;
                    _goalActionTime = act;
                    _roamPhase = RoamPhase.Move;
                    return;
                }
            }

            // 到达最终点：开始做动作
            _goalSpot = spot;
            _roamPhase = RoamPhase.Act;
            _roamTimer = 0.5f;

            if (spot != null)
            {
                // 食盆：播进食（低头吃），brain 的 one-shot 锁会把这段吃满，
                // 锁结束后 Act 再等 0.5s 才结算饥饿
                if (spot.isFood && useHunger)
                {
                    _eating = true;
                    Brain.PlayOneShot(CatMood.Eat, Mathf.Max(1f, eatDuration));
                    ApplyMood(CatMood.Eat);
                    _roamTimer = 0.5f;
                    return;
                }

                if (spot.mood == CatMood.Sit || spot.mood == CatMood.Eat || spot.mood == CatMood.Drink)
                {
                    PlayAction(spot.mood, Mathf.Max(0.8f, act));
                    return;
                }
            }

            if (mood == CatMood.Sit || mood == CatMood.Eat || mood == CatMood.Drink)
                PlayAction(mood, Mathf.Max(0.8f, act));
        }

        void AbandonPath()
        {
            PathKind kind = _pathKind;
            StopPath();

            // 脚本化移动失败（寻路失败/卡住/等门超时）：告诉流程脚本，由它决定重试还是降级
            if (kind == PathKind.Scripted)
            {
                if (ScriptedPathEnded != null) ScriptedPathEnded(false);
                return;
            }

            if (kind == PathKind.Roam)
            {
                _roamPhase = RoamPhase.Wait;
                _roamTimer = 1.2f + Random.Range(0f, 0.8f);
            }
            else
            {
                _fleeTimer = 0.5f;
            }
        }

        void StopPath()
        {
            _path.Clear();
            _pathIndex = 0;
            _lastPathIndex = -1;
            _stuckTimer = 0f;
            _pathTime = 0f;
            _pathKind = PathKind.None;
            _openingDoor = null;
            _pendingDoor = null;
            _doorWait = 0f;
            _moving = false;
            _currentSpeed = 0f;
            _goalSpot = null;
            _goalViaApproach = false;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 当前这一步被挡住时，在 8 邻域（一格 0.12m）里找一个可走、且比现在更接近目标的格子。
        /// 相当于沿墙滑行，专治"猫挤在门框/柜角动不了"。
        /// </summary>
        bool FindSlideTarget(Vector3 pos, Vector3 target, out Vector3 result)
        {
            result = pos;
            float best = FlatDistance(pos, target);
            bool found = false;

            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                Vector3 c = pos + dir * 0.12f;
                if (!_nav.IsWalkable(c)) continue;

                float d = FlatDistance(c, target);
                if (d < best - 0.005f)
                {
                    best = d;
                    result = c;
                    found = true;
                }
            }

            return found;
        }

        void FaceDirection(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();

            // 路径点是 12cm 网格上的离散点，直接 LookRotation 每跨一格就会"啪"地扭一下。
            // 先把目标方向做低通，再按剩余角度给转速（起步柔和、到位减速），才不会像机器人。
            if (!_faceDirInit) { _faceDir = transform.forward; _faceDirInit = true; }
            float k = 1f - Mathf.Exp(-Mathf.Max(0.5f, turnDirectionSharpness) * Time.deltaTime);
            _faceDir = Vector3.Slerp(_faceDir, dir, k);
            if (_faceDir.sqrMagnitude < 1e-6f) _faceDir = dir;

            Quaternion want = Quaternion.LookRotation(_faceDir, Vector3.up);
            float angle = Quaternion.Angle(transform.rotation, want);
            float step = turnSpeed * Time.deltaTime * Mathf.Clamp01(0.18f + angle / 45f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, step);
        }

        // ---------------------------------------------------------------- 扑击

        /// <summary>
        /// 朝目标方向扑出去。动画只做原地动作，位移由这里用曲线驱动，
        /// 与动画的腾空段（约 0.30~0.65 归一化时间）对齐。
        /// </summary>
        public void Pounce(Vector3 targetWorldPos)
        {
            if (_pouncing || _transformLocked) return;

            Vector3 dir = targetWorldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            dir.Normalize();

            FaceDirection(dir);

            float dur = 0f;
            if (!_clipLength.TryGetValue(CatMood.Pounce, out dur) || dur <= 0f) dur = 2f;

            _pounceDur = dur;
            _pounceT = 0f;
            _pounceFrom = transform.position;

            // 落点同样要落在室内可行走格子上，撞墙/出界就缩短距离
            Vector3 safe = transform.position;
            if (IsNavigationReady)
            {
                const int steps = 8;
                for (int i = steps; i >= 1; i--)
                {
                    Vector3 test = transform.position + dir * (pounceForwardDistance * i / steps);
                    if (_nav.IsWalkable(test))
                    {
                        safe = new Vector3(test.x, _nav.FloorHeight(test), test.z);
                        break;
                    }
                }
            }
            else
            {
                safe = transform.position;
            }

            _pounceTo = safe;
            _pouncing = true;
            _pounceFired = false;

            Brain.PlayOneShot(CatMood.Pounce, dur);
            ApplyMood(CatMood.Pounce);
        }

        void UpdatePounce()
        {
            if (!_pouncing) return;

            if (_transformLocked)
            {
                _pouncing = false;
                return;
            }

            _pounceT += Time.deltaTime;
            float u = Mathf.Clamp01(_pounceT / _pounceDur);

            // 起跳瞬间通知
            if (!_pounceFired && u >= 0.30f)
            {
                _pounceFired = true;
                if (Pounced != null) Pounced();
            }

            // 只在腾空段位移：0.30 → 0.65
            const float A = 0.30f, B = 0.65f;
            float k = Mathf.Clamp01((u - A) / (B - A));
            k = k * k * (3f - 2f * k);          // smoothstep，起落都不突兀
            transform.position = Vector3.Lerp(_pounceFrom, _pounceTo, k);

            if (u >= 1f) _pouncing = false;
        }

        // ---------------------------------------------------------------- 对外

        /// <summary>
        /// 挂起普通 AI（感知/闲逛/逃跑/饥饿全停），位置只由脚本化移动驱动。
        /// 开场喂食流程在玩家起床后立即调用，避免 CatOpeningController 交还 AI 的那一帧
        /// 猫先按自己的意愿开始闲逛。
        /// </summary>
        public void SuspendAi(bool suspend)
        {
            _aiSuspended = suspend;
            _poseHold = false;

            if (suspend)
            {
                StopPath();
                _scriptedSpeed = 0f;
            }
            else
            {
                _roamPhase = RoamPhase.Wait;
                _roamTimer = Random.Range(roamIntervalMin, roamIntervalMax);
                _appliedMood = (CatMood)(-1);
            }
        }

        /// <summary>
        /// 沿室内寻路网格走到目标点（含开门、避障）。返回 false 表示没有可用路径。
        /// 结果通过 <see cref="ScriptedPathEnded"/> 通知。速度用 m/s，小跑建议 0.45~0.6。
        /// </summary>
        public bool ScriptedMove(Vector3 worldTarget, float speed)
        {
            if (_transformLocked) return false;

            if (!IsNavigationReady) BuildNavigation();
            if (!IsNavigationReady) return false;

            _poseHold = false;
            _scriptedSpeed = Mathf.Max(0.02f, speed);
            _scriptedRepaths = 0;

            if (!TryStartPath(worldTarget, PathKind.Scripted)) return false;

            _roamPhase = RoamPhase.Wait;
            return true;
        }

        /// <summary>取消脚本化移动（不触发完成事件）。</summary>
        public void ScriptedStop()
        {
            if (_pathKind == PathKind.Scripted) StopPath();
            _scriptedSpeed = 0f;
        }

        /// <summary>当前位置到某点是否有可行路径（不改变当前移动）。流程脚本挑落点时用。</summary>
        public bool HasPathTo(Vector3 worldPoint)
        {
            if (!IsNavigationReady) BuildNavigation();
            if (!IsNavigationReady) return false;

            Vector3 snapped;
            if (!_nav.TrySnap(worldPoint, 0.6f, out snapped)) return false;

            _scratchPath.Clear();
            return _nav.FindPath(transform.position, snapped, _scratchPath) && _scratchPath.Count > 0;
        }

        /// <summary>取离 want 最近的可走点（在 radius 内）。</summary>
        public bool TryGetWalkablePointNear(Vector3 want, float radius, out Vector3 result)
        {
            if (!IsNavigationReady) BuildNavigation();
            if (!IsNavigationReady) { result = want; return false; }
            return _nav.TryGetPointNear(want, radius, out result);
        }

        /// <summary>
        /// 直接指定姿势并锁定，直到下一次 <see cref="ScriptedMove"/> 或 <see cref="SuspendAi"/>。
        /// 用于「低头进食」「趴窝睡觉」「被叫到时的警觉」。
        /// </summary>
        public void SetMoodImmediate(CatMood mood)
        {
            if (Brain == null) return;

            Brain.ForceMood(mood);
            _appliedMood = (CatMood)(-1);   // 强制重放，避免同状态被 ApplyMood 跳过
            ApplyMood(mood);
            _poseHold = true;
        }

        /// <summary>立刻转向某个世界坐标（只在原地做，不位移）。</summary>
        public void FacePoint(Vector3 worldPoint, bool instant = true)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;

            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (instant)
            {
                transform.rotation = want;
                _faceDir = dir.normalized;
                _faceDirInit = true;
                return;
            }

            // 非瞬间：走和移动同一套"目标方向低通 + 角度自适应转速"，不会突然甩头
            _faceDir = dir.normalized;
            _faceDirInit = true;
            float angle = Quaternion.Angle(transform.rotation, want);
            float step = turnSpeed * Time.deltaTime * Mathf.Clamp01(0.18f + angle / 45f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, step);
        }

        /// <summary>硬摆位（离床落点、猫窝中心这种必须精确的地方用）。</summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
        }

        /// <summary>
        /// 锁位置。锁住后本组件的 Update 直接返回，不读感知、不动画、不写 Transform。
        /// 开场趴卧期间由 CatOpeningController 调用。
        /// </summary>
        public void SetTransformLocked(bool locked)
        {
            bool wasScripted = _pathKind == PathKind.Scripted;

            _transformLocked = locked;
            if (locked)
            {
                StopPath();
                _pouncing = false;
            }

            if (locked && wasScripted && ScriptedPathEnded != null) ScriptedPathEnded(false);
        }

        /// <summary>把位置对齐到脚下地面（离床落地后用一次，避免半空或陷进地板）。</summary>
        public void SnapToFloor()
        {
            if (!IsNavigationReady) return;
            Vector3 p = transform.position;
            if (_nav.TrySnap(p, 0.6f, out p))
                transform.position = p;
        }

        /// <summary>播一次性动作（吃/喝/坐）。duration 留空则用剪辑长度。</summary>
        public void PlayAction(CatMood mood, float duration = -1f)
        {
            if (!CatClips.IsOneShot(mood) && mood != CatMood.Eat && mood != CatMood.Drink)
                return;
            if (mood == CatMood.Pounce)
            {
                Pounce(transform.position + transform.forward * pounceForwardDistance);
                return;
            }

            float dur = duration;
            if (dur <= 0f && !_clipLength.TryGetValue(mood, out dur)) dur = 1.5f;

            Brain.PlayOneShot(mood, dur);
            ApplyMood(mood);
        }

        /// <summary>进入睡觉（常驻直到被打断）。</summary>
        public void Sleep()
        {
            Brain.ForceMood(CatMood.Sleep);
            ApplyMood(CatMood.Sleep);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
            Gizmos.DrawWireCube(new Vector3(boundsCenter.x, boundsCenter.y + 0.25f, boundsCenter.z),
                                new Vector3(boundsSize.x, 0.5f, boundsSize.z));

            if (debugDrawNav && _nav != null) _nav.DrawGizmos(boundsCenter.y + 0.02f);

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, 2.2f);
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, 5f);
        }

        /// <summary>可选的定点目标（食物碗、水碗、猫窝、躲藏点…）。</summary>
        [System.Serializable]
        public class CatSpot
        {
            public string name = "休息点";

            [Tooltip("最终停留点（猫站在这里做动作）")]
            public Transform point;

            [Tooltip("可选：先走到这个接近点，再从接近点走向 point。留空则直接寻路到 point")]
            public Transform approach;

            [Tooltip("可选：到这个点后转过去面对它（例如饭盆本体）")]
            public Transform faceTarget;

            public CatMood mood = CatMood.Sit;

            [Tooltip("到位后做动作的时长（秒）")]
            public float actSeconds = 1.6f;

            [Range(0f, 4f)] public float weight = 1f;

            [Tooltip("这是食盆：吃完会把饥饿值清空")]
            public bool isFood;
        }
    }
}
