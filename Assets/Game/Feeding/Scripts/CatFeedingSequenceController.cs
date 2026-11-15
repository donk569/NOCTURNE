using System.Collections;
using UnityEngine;
using NOCTURNE.Cat;
using NOCTURNE.Opening;

namespace NOCTURNE.Feeding
{
    /// <summary>
    /// 首次喂食流程总编排（只做编排，不碰 Transform / Animator / 寻路的细节）。
    ///
    /// 阶段
    ///   Opening → 玩家起床 → GetUp（等猫跳下床）→ ApproachPlayer → Meow →
    ///   WaitingForFood → RunToFood → Eating → ReturnToBed → Sleeping
    ///
    /// 职责边界
    ///   · 位移一律走 CatAgent.ScriptedMove（室内网格 + A* + 开关门），本脚本不写坐标
    ///   · 姿势一律走 CatAgent.SetMoodImmediate（进食/警觉/睡觉）
    ///   · 食物状态一律问 CatFoodBowl（HasFood / StateChanged）
    ///   · 提示文字一律走 OpeningDialogueUI（场景里预先建好的 UI，不运行时创建）
    ///
    /// 时序上有个关键点：玩家起床的同一帧，CatOpeningController 也会收到事件并开始"跳下床"。
    /// 这里在事件里第一时间 SuspendAi(true)，这样落地那一帧交还 AI 时猫不会先自己闲逛两秒。
    ///
    /// 吃完后进入 Sleeping 只是"流程阶段"，猫此时由 CatBedController 接管（位置锁死）。
    /// 玩家再次倒粮会把它叫醒，走第二轮 RunToFood → Eating → ReturnToBed。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatFeedingSequenceController : MonoBehaviour
    {
        [Header("引用")]
        public CatAgent cat;
        public CatOpeningController opening;
        public PlayerLyingController lying;
        public CatFoodBowl bowl;
        public CatFoodBagInteractable bag;
        public CatBedController bed;
        public OpeningDialogueUI ui;
        public Transform player;
        public AudioSource catVoice;

        [Header("音效")]
        [Tooltip("饥饿叫唤（复用 Opening/Audio/cat_hungry.mp3）")]
        public AudioClip meowClip;

        [Tooltip("进食时的轻微咀嚼声（复用 blanket_rustle，低音量、略提高音高）")]
        public AudioClip eatClip;

        [Range(0f, 1f)] public float meowVolume = 0.5f;
        [Range(0f, 1f)] public float eatVolume = 0.16f;

        [Header("参数")]
        [Tooltip("走到玩家跟前时保持的距离（米）")]
        public float approachDistance = 1.0f;

        [Tooltip("跑向饭盆的速度（m/s）。要贴近 Run 剪辑的天然速度 1.0，否则脚底打滑")]
        public float runSpeed = 0.90f;

        [Tooltip("回猫窝的速度（m/s）")]
        public float returnSpeed = 0.3f;

        [Tooltip("叫几声")]
        public int meowCount = 2;

        [Tooltip("两声猫叫的间隔（秒）")]
        public float meowInterval = 1.4f;

        [Tooltip("床边等待时第一声叫的延迟（秒）")]
        public float firstWaitMeowDelay = 1.4f;

        [Tooltip("床边等待时两次叫声的最小间隔（秒）")]
        public float waitMeowMin = 2.8f;

        [Tooltip("床边等待时两次叫声的最大间隔（秒）")]
        public float waitMeowMax = 5.5f;

        [Tooltip("一次进食时长（秒），与饭盆的视觉递减对齐")]
        public float eatSeconds = 10f;

        [Tooltip("吃完抬头到出发回窝之间的停顿（秒）")]
        public float afterEatingPause = 1.1f;

        [Header("进食时低头对准")]
        [Tooltip("进食期间把头部注视目标从玩家切到饭盆，否则头是抬着的，看起来在空气里嚼")]
        public bool aimHeadAtBowl = true;

        [Tooltip("等饭期间：玩家走远超过这个距离，猫会跟过去（米）")]
        public float followDistance = 2.2f;

        [Tooltip("提示文案")]
        [TextArea(1, 3)]
        public string hungryHint = "它好像饿了，去客厅给它喂点吃的吧。";

        [Tooltip("猫叫前多久显示提示（秒）")]
        public float hintDelay = 0.35f;

        [Header("启动")]
        [Tooltip("进入 Play 后自动接管。关掉则等外部调用 BeginSequence()")]
        public bool autoStart = true;

        public bool logPhase = true;

        [Header("状态（只读）")]
        [SerializeField] FeedingPhase _phase = FeedingPhase.Opening;

        public FeedingPhase Phase { get { return _phase; } }

        bool _started;
        Coroutine _eatingSound;
        int _cycle;

        // ---------------------------------------------------------------- 生命周期

        void Start()
        {
            if (cat == null) cat = FindObjectOfType<CatAgent>();
            if (lying == null) lying = FindObjectOfType<PlayerLyingController>();
            if (opening == null && cat != null) opening = cat.GetComponent<CatOpeningController>();
            if (ui == null) ui = FindObjectOfType<OpeningDialogueUI>();
            if (player == null && cat != null && cat.player != null) player = cat.player;

            if (bowl != null) bowl.StateChanged += OnBowlStateChanged;

            if (!autoStart) return;

            if (lying != null)
            {
                lying.GetUpFinished -= OnPlayerGetUpFinished;
                lying.GetUpFinished += OnPlayerGetUpFinished;
            }
            else
            {
                // 没有开场流程的场景（调试用）：直接开始
                BeginSequence();
            }
        }

        void OnDestroy()
        {
            if (lying != null) lying.GetUpFinished -= OnPlayerGetUpFinished;
            if (bowl != null) bowl.StateChanged -= OnBowlStateChanged;
        }

        /// <summary>外部（主菜单/调试）主动开始。</summary>
        public void BeginSequence()
        {
            if (_started) return;
            _started = true;

            SetPhase(_phase == FeedingPhase.Opening ? FeedingPhase.GetUp : _phase);

            if (cat != null) cat.SuspendAi(true);
            StartCoroutine(CoSequence());
        }

        void OnPlayerGetUpFinished()
        {
            BeginSequence();
        }

        void SetPhase(FeedingPhase next)
        {
            if (_phase == next) return;
            _phase = next;
            if (logPhase) Debug.Log("[CatFeeding] 阶段 → " + next);
        }

        // ---------------------------------------------------------------- 主流程

        IEnumerator CoSequence()
        {
            // ---- 1) 等猫自己从床上跳下来（CatOpeningController 负责落地与交还 AI）
            float t = 0f;
            while (opening != null && opening.IsLocked && t < 8f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.15f);

            // ---- 2) 跳下床之后就守在床边：不走过去、不跟着玩家、更不自己跑去客厅
            //
            // 需求写得很清楚：猫落床后留在原地，只是转身盯着玩家，间歇撒娇叫。
            // 之前这里会先 CoMoveTo 到玩家跟前（还有一段"玩家走远就追过去"的逻辑），
            // 那会让玩家觉得猫在带路/催人，所以整段删掉。
            SetPhase(FeedingPhase.ApproachPlayer);
            if (cat != null)
            {
                if (!cat.IsAiSuspended) cat.SuspendAi(true);
                cat.FacePoint(PlayerPosition());
                cat.SetMoodImmediate(CatMood.Idle);
            }

            if (hintDelay > 0f) yield return new WaitForSeconds(hintDelay);
            if (ui != null) ui.ShowFeedHint(hungryHint);

            // ---- 3) 床边等待：只转身 + 间歇叫
            SetPhase(FeedingPhase.WaitingForFood);

            float meowTimer = Mathf.Max(0.4f, firstWaitMeowDelay);

            while (true)
            {
                if (bowl == null) yield break;

                if (!bowl.HasFood)
                {
                    // 持续面向玩家：每帧推一点（FacePoint 的非 instant 版本按转速推进），
                    // 比每 0.8 秒瞬移一次朝向自然得多
                    if (cat != null) cat.FacePoint(PlayerPosition(), false);

                    // 间歇撒娇叫，间隔随机，绝不连续循环同一段
                    meowTimer -= Time.deltaTime;
                    if (meowTimer <= 0f)
                    {
                        PlayWaitMeow();
                        meowTimer = Random.Range(Mathf.Min(waitMeowMin, waitMeowMax),
                                                 Mathf.Max(waitMeowMin, waitMeowMax));
                    }

                    yield return null;
                    continue;
                }

                // 碗里有粮了：停止等待叫声，立刻出发
                if (cat != null) cat.SetMoodImmediate(CatMood.Idle);
                break;
            }

            // ---- 4) 跑向饭盆 + 进食（循环回床）
            while (true)
            {
                if (bowl == null) yield break;

                // ---- 5) 跑去吃饭
                _cycle++;
                SetPhase(FeedingPhase.RunToFood);
                if (cat != null) cat.SuspendAi(true);

                Vector3 eatPos = bowl.eatingPoint != null
                    ? bowl.eatingPoint.position
                    : bowl.transform.position;

                yield return StartCoroutine(CoMoveTo(eatPos, runSpeed, "跑去饭盆"));

                // 寻路网格的格子是 12cm，走到 eatingPoint 时通常还差 10cm 左右。
                // 进食必须嘴对碗，所以最后这一小段用 0.15s 滑过去（不是瞬移，肉眼看是"凑近一步"）。
                if (cat != null)
                    yield return StartCoroutine(CoGlideTo(eatPos, 0.15f, bowl.transform.position));

                CatLookAt look = cat != null ? cat.GetComponent<CatLookAt>() : null;
                Transform prevLookTarget = look != null ? look.target : null;

                if (cat != null)
                {
                    cat.FacePoint(bowl.transform.position);
                    // ---- 6) 低头进食
                    SetPhase(FeedingPhase.Eating);
                    cat.SetMoodImmediate(CatMood.Eat);

                    // 头要真的低到碗里：把注视目标换成饭盆里的粮
                    if (aimHeadAtBowl && look != null)
                    {
                        look.target = bowl.foodVisualRoot != null ? bowl.foodVisualRoot : bowl.transform;
                        look.enableLook = true;
                    }
                }

                StartEatingSound();
                bowl.BeginEating(eatSeconds);

                // 逻辑吃完（饭盆自己把视觉同步清空）才继续
                while (bowl.HasFood) yield return null;

                StopEatingSound();

                // 吃完把注视目标还给玩家（继续"盯着你"）
                if (look != null) look.target = prevLookTarget;

                if (cat != null) cat.SetMoodImmediate(CatMood.Idle);
                if (afterEatingPause > 0f) yield return new WaitForSeconds(afterEatingPause);

                // ---- 7) 回猫窝
                SetPhase(FeedingPhase.ReturnToBed);
                yield return StartCoroutine(CoMoveTo(BedPosition(), returnSpeed, "回猫窝"));

                // ---- 8) 趴下睡觉（位置由 CatBedController 锁死）
                SetPhase(FeedingPhase.Sleeping);
                if (bed != null) bed.PutToSleep(cat);
                if (logPhase) Debug.Log("[CatFeeding] 第 " + _cycle + " 轮结束，猫已进窝");

                // 睡到下一次有人倒粮
                while (!bowl.HasFood) yield return null;
                if (bed != null) bed.WakeUp(cat);
            }
        }

        // ---------------------------------------------------------------- 子步骤

        /// <summary>
        /// 走到目标点，最多重试 3 次；每次都用 CatAgent 自己的寻路（开门、绕家具）。
        /// 失败只记日志并重试，绝不直接写坐标糊过去。
        /// </summary>
        IEnumerator CoMoveTo(Vector3 target, float speed, string label)
        {
            if (cat == null) yield break;

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool done = false;
                bool arrived = false;

                System.Action<bool> handler = delegate (bool ok)
                {
                    arrived = ok;
                    done = true;
                };

                cat.ScriptedPathEnded += handler;
                bool started = cat.ScriptedMove(target, speed);

                float elapsed = 0f;
                while (started && !done && elapsed < 45f)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                cat.ScriptedPathEnded -= handler;

                if (started && arrived) yield break;

                // 猫自己的自愈逻辑（就地重规划）已经试过了，这里只做"重开一次完整寻路"
                if (attempt < maxAttempts)
                {
                    if (attempt >= 2)
                        Debug.LogWarning(string.Format(
                            "[CatFeeding] {0} 第 {1} 次失败（started={2}, arrived={3}, 目标 {4}），重试",
                            label, attempt, started, arrived, target.ToString("F2")), this);

                    yield return new WaitForSeconds(0.5f);
                }
            }

            Debug.LogError("[CatFeeding] " + label + " 连续失败，流程可能停在这里（不会瞬移兜底）", this);
        }

        /// <summary>
        /// 最后一段"凑近"：把猫从寻路落点平滑挪到精确的交互点，同时转向目标。
        /// 用插值而不是 PlaceAt，避免玩家看到一帧瞬移。
        /// </summary>
        IEnumerator CoGlideTo(Vector3 target, float duration, Vector3 lookAtWorld)
        {
            if (cat == null) yield break;

            Vector3 from = cat.transform.position;
            Vector3 flat = target - from;
            flat.y = 0f;
            if (flat.magnitude < 0.02f) yield break;

            Quaternion rotFrom = cat.transform.rotation;
            Vector3 lookDir = lookAtWorld - from;
            lookDir.y = 0f;
            Quaternion rotTo = lookDir.sqrMagnitude > 1e-5f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : rotFrom;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                cat.transform.position = Vector3.Lerp(from, new Vector3(target.x, from.y, target.z), k);
                cat.transform.rotation = Quaternion.Slerp(rotFrom, rotTo, k);
                yield return null;
            }
            cat.transform.position = new Vector3(target.x, from.y, target.z);
            cat.transform.rotation = rotTo;   // 到位就已经正对碗口，后面不用再瞬移甩头
        }

        /// <summary>
        /// 床边等待时叫一声：只叫一声、每次音高略有差别，靠外层计时器拉开间隔。
        /// 绝不能自己循环播放同一段音频。
        /// </summary>
        void PlayWaitMeow()
        {
            if (catVoice == null || meowClip == null) return;
            catVoice.pitch = Random.Range(0.96f, 1.06f);
            catVoice.PlayOneShot(meowClip, meowVolume);
        }

        IEnumerator CoMeow()
        {            int count = Mathf.Max(1, meowCount);

            for (int i = 0; i < count; i++)
            {
                if (catVoice != null && meowClip != null)
                {
                    // 第一声略低、第二声略高，避免两次一模一样
                    catVoice.pitch = i % 2 == 0 ? 0.97f : 1.04f;
                    catVoice.PlayOneShot(meowClip, meowVolume);
                }

                if (i < count - 1)
                    yield return new WaitForSeconds(meowInterval);
                else
                    yield return new WaitForSeconds(0.45f);
            }
        }

        void StartEatingSound()
        {
            if (eatClip == null || catVoice == null) return;
            StopEatingSound();
            _eatingSound = StartCoroutine(CoEatingSound());
        }

        IEnumerator CoEatingSound()
        {
            // 轻微咀嚼：随机间隔地播一次，不要连成一片
            while (true)
            {
                catVoice.pitch = Random.Range(1.15f, 1.3f);
                catVoice.PlayOneShot(eatClip, eatVolume);
                yield return new WaitForSeconds(Random.Range(1.1f, 2.2f));
            }
        }

        void StopEatingSound()
        {
            if (_eatingSound != null)
            {
                StopCoroutine(_eatingSound);
                _eatingSound = null;
            }
            if (catVoice != null) catVoice.pitch = 1f;
        }

        void OnBowlStateChanged(CatFoodBowlState oldState, CatFoodBowlState newState)
        {
            // 有粮了就把"它好像饿了"的提示收掉（需求：喂食流程完成后隐藏提示）
            if (newState == CatFoodBowlState.Filled && ui != null) ui.HideFeedHint();
        }

        // ---------------------------------------------------------------- 工具

        Vector3 PlayerPosition()
        {
            if (player != null) return player.position;
            if (cat != null && cat.player != null) return cat.player.position;
            return transform.position;
        }

        Vector3 BedPosition()
        {
            if (bed == null) return cat != null ? cat.transform.position : transform.position;
            return bed.SleepPoint.position;
        }

        /// <summary>
        /// 在玩家周围找一个"能真正走过去、离玩家尽量近"的落点。
        ///
        /// 为什么不能只用"玩家 + 反方向 × 距离"：玩家起床后站在床西侧那条走道上，
        /// 那条走道被 床 / 书桌 / 椅子 / 床头 围成了一个 0.4m 宽的封闭口袋，
        /// 猫（半径 0.15）进不去 —— 实测 A* 直接无解。
        /// 所以这里以玩家为圆心，由近到远、由正面到侧面撒点，只接受"真的有路"的点：
        /// 猫会退而求其次走到床脚叫，而不是卡在那里一步不动。
        /// </summary>
        Vector3 ApproachPointFor(Vector3 target)
        {
            Vector3 from = cat != null ? cat.transform.position : transform.position;
            Vector3 dir = from - target;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.01f)
                dir = cat != null ? -cat.transform.forward : Vector3.back;

            dir.Normalize();
            Vector3 ideal = target + dir * approachDistance;

            if (cat == null) return ideal;
            if (cat.HasPathTo(ideal)) return ideal;

            // 由正面开始左右交替扫，半径逐级放大
            float baseAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float[] radii = { approachDistance, approachDistance + 0.6f, approachDistance + 1.2f, 2.0f, 2.8f };

            for (int r = 0; r < radii.Length; r++)
            {
                for (int i = 0; i < 9; i++)
                {
                    float offset = (i % 2 == 1) ? (i / 2 + 1) * 40f : -(i / 2) * 40f;
                    Vector3 d = Quaternion.Euler(0f, baseAngle + offset, 0f) * Vector3.forward;
                    Vector3 p = target + d * radii[r];
                    if (cat.HasPathTo(p)) return p;
                }
            }

            Debug.LogWarning("[CatFeeding] 玩家周围 3m 内没有猫能走到的落点，退回理想点", this);
            return ideal;
        }

        void OnDrawGizmosSelected()
        {
            if (bowl != null && bowl.eatingPoint != null)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                Gizmos.DrawWireSphere(bowl.eatingPoint.position, 0.08f);
            }
            if (bed != null)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
                Gizmos.DrawWireSphere(bed.SleepPoint.position, 0.1f);
            }
        }
    }
}
