#if UNITY_EDITOR // 调试/自测代码：仅编辑器构建，不进入玩家包
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using NOCTURNE.Cat;
using NOCTURNE.Feeding;
using NOCTURNE.Opening;

namespace NOCTURNE.DebugTools
{
    /// <summary>
    /// 首次喂食流程的自动验证脚本（仅开发期用）。
    ///
    /// 为什么存在：这台机器上 Unity MCP 插件在 Play 模式里会反复掉线，
    /// 没法从外部逐帧驱动流程。所以改成"游戏内自驱动 + 写日志到磁盘"，
    /// 外部只看 `.workbuddy/selftest.log` 就能知道每一步是否真的发生了。
    ///
    /// 启动条件：工程根目录存在 `.workbuddy/selftest.on` 标记文件。
    /// 没有标记文件时本脚本完全不介入（连 Update 都不挂）。
    ///
    /// 验证内容
    ///   1 开场猫趴床贴地 → 2 玩家起床 → 3 猫跳下床 → 4 走到玩家附近叫 →
    ///   5 提示出现 → 6 玩家走到客厅（猫跟过来）→ 7 拿起猫粮袋 → 8 倒粮 →
    ///   9 猫跑去吃 → 10 猫粮逐颗减少到 0 → 11 回猫窝趴下 →
    ///   12 再次倒粮把猫叫醒，走第二轮
    /// </summary>
    public static class CatFeedingSelfTest
    {
        const string MarkerRelative = "../.workbuddy/selftest.on";
        const string LogRelative = "../.workbuddy/selftest.log";

        public static string MarkerPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, MarkerRelative)); } }
        public static string LogPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, LogRelative)); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!File.Exists(MarkerPath)) return;

            if (File.Exists(LogPath)) File.Delete(LogPath);

            var go = new GameObject("CatFeedingSelfTestRunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        public sealed class Runner : MonoBehaviour
        {
            static readonly StringBuilder _buf = new StringBuilder();

            // Play 里 MCP 会掉线，Console 读不到，所以自己挂日志钩子把警告/错误抄进文件
            static int _warnCount;
            static int _errCount;
            static readonly StringBuilder _problems = new StringBuilder();

            public static void Log(string line)
            {
                string s = "[SELFTEST] " + line;
                Debug.Log(s);
                File.AppendAllText(LogPath, s + "\n");
            }

            void OnEnable() { Application.logMessageReceived += OnUnityLog; }
            void OnDisable() { Application.logMessageReceived -= OnUnityLog; }

            static void OnUnityLog(string msg, string stack, LogType type)
            {
                if (type != LogType.Warning && type != LogType.Error &&
                    type != LogType.Exception && type != LogType.Assert) return;
                if (msg != null && msg.StartsWith("[SELFTEST]")) return;

                if (type == LogType.Warning) _warnCount++; else _errCount++;

                // 只留前 300 字，避免日志炸掉
                string one = (msg ?? "").Replace("\n", " ");
                if (one.Length > 160) one = one.Substring(0, 160) + "…";
                _problems.AppendLine("[" + type + "] " + one);
                File.AppendAllText(LogPath, "[UNITY-" + type + "] " + one + "\n");
            }

            IEnumerator Start()
            {
                Log("start, unity=" + Application.unityVersion);

                var cat = FindObjectOfType<CatAgent>();
                var bowl = FindObjectOfType<CatFoodBowl>();
                var bag = FindObjectOfType<CatFoodBagInteractable>();
                var bed = FindObjectOfType<CatBedController>();
                var seq = FindObjectOfType<CatFeedingSequenceController>();
                var lying = FindObjectOfType<PlayerLyingController>();
                var ui = FindObjectOfType<OpeningDialogueUI>();
                var fpc = FindObjectOfType<FirstPersonController>();

                if (cat == null || bowl == null || bag == null || bed == null || seq == null)
                {
                    Log("FAIL 引用缺失 cat=" + (cat != null) + " bowl=" + (bowl != null) +
                        " bag=" + (bag != null) + " bed=" + (bed != null) + " seq=" + (seq != null));
                    yield break;
                }

                // ---- 1) 快进开场
                // 期间统计"猫身上任何音源是否响过"：黑屏阶段必须完全静音。
                // 只统计黑屏（BlackOverlay alpha > 0.5）那一段 —— 睁眼之后猫叫是应该的。
                var catAudio = cat.GetComponentsInChildren<AudioSource>(true);
                var voiceGo = GameObject.Find("CatFeeding/CatVoice");
                var voiceSrc = voiceGo != null ? voiceGo.GetComponent<AudioSource>() : null;
                var openingSeq = UnityEngine.Object.FindObjectOfType(System.Type.GetType("NOCTURNE.Opening.OpeningSequenceController, Assembly-CSharp")) as MonoBehaviour;
                var overlayGo = GameObject.Find("OpeningCanvas/BlackOverlay");
                var overlayImg = overlayGo != null ? overlayGo.GetComponent<UnityEngine.UI.Image>() : null;

                float catAudioSeconds = 0f;
                string firstClip = null;
                float firstAlpha = -1f;
                int ringsBefore = NOCTURNE.Cat.CatBell.TotalRings;

                // 睁眼之后是否真的开始叫（和上面"黑屏必须静音"配对检查）
                float postMeowSeconds = 0f;
                int postMeowStarts = 0;
                bool postWasPlaying = false;
                string postFirstClip = null;

                Time.timeScale = 6f;
                float t = 0f;
                while (lying != null && !lying.CanGetUp && t < 40f)
                {
                    t += Time.unscaledDeltaTime;
                    // 「黑屏」的判定用开场流程自己的标志位：睁眼之前一律算黑屏。
                    // 只看 alpha 会被缓动曲线骗 —— 淡出前 30% 的 alpha 仍然接近 1。
                    bool black = openingSeq == null || !(bool)openingSeq.GetType().GetProperty("EyeOpened").GetValue(openingSeq, null);
                    if (black)
                    {
                        for (int i = 0; i < catAudio.Length; i++)
                        {
                            if (catAudio[i] != null && catAudio[i].isPlaying)
                            {
                                catAudioSeconds += Time.unscaledDeltaTime;
                                if (firstClip == null)
                                {
                                    firstClip = catAudio[i].gameObject.name + ":" + (catAudio[i].clip == null ? "playOneShot" : catAudio[i].clip.name);
                                    firstAlpha = overlayImg != null ? overlayImg.color.a : -1f;
                                }
                                break;
                            }
                        }
                        if (voiceSrc != null && voiceSrc.isPlaying)
                        {
                            catAudioSeconds += Time.unscaledDeltaTime;
                            if (firstClip == null) firstClip = "voice:" + (voiceSrc.clip == null ? "?" : voiceSrc.clip.name);
                        }
                    }
                    else
                    {
                        // 睁眼之后：必须开始叫，而且要"叫一阵停一阵"，不能一条音频死循环
                        bool playing = false;
                        string clipName = null;
                        for (int i = 0; i < catAudio.Length; i++)
                        {
                            if (catAudio[i] != null && catAudio[i].isPlaying)
                            {
                                playing = true;
                                clipName = catAudio[i].clip == null ? "playOneShot" : catAudio[i].clip.name;
                                break;
                            }
                        }
                        if (voiceSrc != null && voiceSrc.isPlaying)
                        {
                            playing = true;
                            clipName = clipName ?? ("voice:" + (voiceSrc.clip == null ? "?" : voiceSrc.clip.name));
                        }
                        if (playing)
                        {
                            postMeowSeconds += Time.unscaledDeltaTime;
                            if (!postWasPlaying)
                            {
                                postMeowStarts++;
                                if (postFirstClip == null) postFirstClip = clipName;
                            }
                        }
                        postWasPlaying = playing;
                    }
                    yield return null;
                }
                Time.timeScale = 1f;

                Log(string.Format("A0b 睁眼之后猫叫 {0:F2}s，起播 {1} 次，首次={2}",
                    postMeowSeconds, postMeowStarts, postFirstClip == null ? "无" : postFirstClip));

                Log(string.Format("A0 黑屏期间猫音源累计播放 {0:F2}s（必须为 0）首次={1} 当时的黑屏alpha={2:F2} 期间响铃={3}次",
                    catAudioSeconds, firstClip == null ? "无" : firstClip, firstAlpha, NOCTURNE.Cat.CatBell.TotalRings - ringsBefore));

                var head = FindDeep(cat.transform, "head");
                Transform cam = fpc != null ? fpc.cameraRig : null;

                // ---- 睁眼后猫头抖动采样：看峰峰值，更要看"单帧跳变"（抽搐=单帧一大跳）
                if (head != null)
                {
                    // 先跳过 0.4s：自测刚把 timeScale 从 6 改回 1，头一两帧是追赶造成的假跳变
                    yield return new WaitForSeconds(0.4f);
                    Vector3 lo = head.position, hi = head.position;
                    Vector3 prevH = head.position;
                    float headMaxStep = 0f;
                    float jt = 0f;
                    while (jt < 1.2f)
                    {
                        jt += Time.deltaTime;
                        Vector3 hp = head.position;
                        lo = Vector3.Min(lo, hp);
                        hi = Vector3.Max(hi, hp);
                        float step = Vector3.Distance(hp, prevH);
                        if (step > headMaxStep) headMaxStep = step;
                        prevH = hp;
                        yield return null;
                    }
                    Log(string.Format("A1 头部运动：峰峰值 {0:F4}m，单帧最大跳变 {1:F4}m（抽搐会 > 0.01）",
                        (hi - lo).magnitude, headMaxStep));
                }

                Log(string.Format("A 开场：猫={0} 可起床={1}",
                    cat.transform.position.ToString("F3"),
                    lying != null && lying.CanGetUp));
                if (cam != null)
                {
                    Vector3 toHead = (head != null ? head.position : cat.transform.position) - cam.position;
                    Log(string.Format("A2 相机={0} 朝向={1} 猫头={2}",
                        cam.position.ToString("F3"), cam.forward.ToString("F3"),
                        head == null ? "-" : head.position.ToString("F3")));
                    var hips = FindDeep(cat.transform, "hips");
                    var rootBone = FindDeep(cat.transform, "root");
                    Log(string.Format("A4 变换朝向={0} root骨骼={1} hips={2}",
                        cat.transform.forward.ToString("F2"),
                        rootBone == null ? "-" : rootBone.forward.ToString("F2"),
                        hips == null ? "-" : hips.forward.ToString("F2")));
                    Log(string.Format("A3 相机→猫头：距离={0:F2}m 偏航={1:F0}° 俯仰={2:F0}°（相机中心为 0）",
                        toHead.magnitude,
                        Vector3.SignedAngle(new Vector3(cam.forward.x, 0f, cam.forward.z),
                                            new Vector3(toHead.x, 0f, toHead.z), Vector3.up),
                        Vector3.Angle(cam.forward, toHead) * Mathf.Sign(Vector3.Dot(toHead, cam.up))));
                }

                yield return Shot("opening");

                // ---- 2) 起床
                if (lying != null) lying.RequestGetUp();
                t = 0f;
                while (t < 10f && (lying == null || lying.IsGettingUp || lying.IsLying)) { t += Time.deltaTime; yield return null; }

                // ---- 3) 等猫跳下床、进入床边等待（顺便记录跳跃弧线，证明是"跳"不是"瞬移/滑落"）
                t = 0f;
                float yStart = cat.transform.position.y;
                float yMax = yStart;
                float yMin = 99f;
                while (t < 20f && seq.Phase != FeedingPhase.WaitingForFood)
                {
                    t += Time.deltaTime;
                    float y = cat.transform.position.y;
                    if (y > yMax) yMax = y;
                    if (y < yMin) yMin = y;
                    yield return null;
                }
                Log(string.Format("B1 跳下床：起点 y={0:F3} 过程最高 y={1:F3} 最低 y={2:F3} 用时 {3:F2}s（最高 > 起点 = 有起跳弧线）",
                    yStart, yMax, yMin, t));
                Vector3 standPos = lying != null ? lying.transform.position : Vector3.zero;
                Log(string.Format("B 起床后：阶段={0} 猫={1} 在地面={2} 玩家站位={3} 距躺卧锚点={4:F2}m",
                    seq.Phase, cat.transform.position.ToString("F2"), cat.transform.position.y < 0.1f,
                    standPos.ToString("F2"),
                    new Vector2(standPos.x - 0f, standPos.z - 1.86f).magnitude));

                // 玩家必须拿回控制权：FPC 与胶囊体开着、躺卧控制器不再接管
                if (fpc != null)
                {
                    var cc = fpc.GetComponent<CharacterController>();
                    Log(string.Format("B2 玩家控制权：fpc.enabled={0} cc.enabled={1} lying.IsLying={2} lying.IsGettingUp={3} lying.enabled={4}",
                        fpc.enabled, cc != null && cc.enabled,
                        lying != null && lying.IsLying, lying != null && lying.IsGettingUp,
                        lying != null && lying.enabled));
                }

                yield return new WaitForSeconds(2.5f);
                Log(string.Format("C 叫唤后：阶段={0} 提示alpha={1:F2} 猫={2}",
                    seq.Phase, ui != null && ui.feedHint != null ? ui.feedHint.alpha : -1f, cat.transform.position.ToString("F2")));

                // ---- 4) 玩家走到客厅（验证"等饭期间跟过来"）
                MovePlayer(fpc, new Vector3(0.60f, 0.02f, -3.72f));
                yield return new WaitForSeconds(0.4f);

                Vector3 catBefore = cat.transform.position;
                yield return new WaitForSeconds(14f);
                float moved = Vector3.Distance(catBefore, cat.transform.position);
                Log(string.Format("D 玩家到客厅后 14s：猫移动了 {0:F2}m，位置={1}，阶段={2}",
                    moved, cat.transform.position.ToString("F2"), seq.Phase));

                // ---- 5) 拿起猫粮
                AimAt(fpc, new Vector3(-0.05f, 0.58f, -3.90f));
                yield return new WaitForSeconds(0.4f);
                yield return Shot("bag");
                var target = fpc != null ? fpc.CurrentTarget : null;
                Log("E 准星对着猫粮袋：target=" + (target == null ? "null" : target.GetType().Name + " 提示=[" + target.Prompt + "]"));

                if (target != null) target.Interact();
                yield return new WaitForSeconds(0.8f);
                int bagCount = FindObjectsOfType<CatFoodBagInteractable>().Length;
                Log(string.Format("F 拿起后：bag={0} 碰撞体={1} 距相机={2:F2}m 场景中袋子数量={3}",
                    bag.State, bag.GetComponent<Collider>().enabled,
                    Vector3.Distance(bag.transform.position, fpc.cameraRig.position),
                    bagCount));

                // ---- 5b) 放回茶几 → 再拿起（验收项：可放回、不重复生成）
                Vector3 tablePos = bag.tableAnchor != null ? bag.tableAnchor.position : bag.transform.position;
                Quaternion tableRot = bag.tableAnchor != null ? bag.tableAnchor.rotation : bag.transform.rotation;
                bag.ReturnToTable();
                yield return new WaitForSeconds(1.0f);
                Log(string.Format("F2 放回茶几：state={0} 回到原位偏差={1:F3}m 朝向偏差={2:F1}° 碰撞体={3} 手持单例已清={4}",
                    bag.State,
                    Vector3.Distance(bag.transform.position, tablePos),
                    Quaternion.Angle(bag.transform.rotation, tableRot),
                    bag.GetComponent<Collider>().enabled,
                    CatFoodBagInteractable.Held == null));

                bag.PickUp();
                yield return new WaitForSeconds(0.7f);
                Log(string.Format("F3 重新拿起：state={0} 碰撞体={1} 场景中袋子数量={2}",
                    bag.State, bag.GetComponent<Collider>().enabled,
                    FindObjectsOfType<CatFoodBagInteractable>().Length));

                // ---- 6) 走到饭盆前倒粮
                MovePlayer(fpc, new Vector3(-1.40f, 0.02f, -6.18f));
                yield return new WaitForSeconds(0.4f);
                AimAt(fpc, new Vector3(-2.02f, 0.05f, -6.10f));
                yield return new WaitForSeconds(0.4f);
                target = fpc != null ? fpc.CurrentTarget : null;
                Log("G 准星对着饭盆：target=" + (target == null ? "null" : target.GetType().Name + " 提示=[" + target.Prompt + "]"));
                Log("G2 长按倒粮阈值=" + bowl.pourHoldSeconds.ToString("F2") + "s 允许距离=" + bowl.pourRange.ToString("F2") + "m");

                // 先按一小会儿再松手：必须取消，盆里不能有粮
                bowl.DebugHoldPour(bowl.pourHoldSeconds * 0.5f);
                float midProgress = bowl.PourHoldProgress;
                bowl.DebugReleasePour();
                yield return new WaitForSeconds(0.2f);
                Log(string.Format("G3 半途松手：进度曾到 {0:F0}%，取消后进度 {1:F0}%，盆={2} 粮={3:F2}",
                    midProgress * 100f, bowl.PourHoldProgress * 100f, bowl.State, bowl.FoodAmount));

                // 再按住足够久：这次必须真的倒
                bowl.DebugHoldPour(bowl.pourHoldSeconds + 0.15f);
                yield return new WaitForSeconds(0.9f);
                Log(string.Format("H 倒粮中：bag={0} 盆={1}({2:F2}) 颗粒={3}",
                    bag.State, bowl.State, bowl.FoodAmount, CountKibbles(bowl)));

                yield return new WaitForSeconds(0.35f);
                Log(string.Format("H2 袋子倾角={0:F1}° 状态={1} 颗粒={2}",
                    bag.TiltDegrees, bag.State, CountKibbles(bowl)));
                yield return Shot("pour");

                yield return new WaitForSeconds(1.6f);
                AimAt(fpc, new Vector3(-2.02f, 0.10f, -6.10f));                yield return new WaitForSeconds(0.4f);
                yield return Shot("bowl");

                // ---- 7) 等猫吃完、回窝
                t = 0f;
                float lastKibbleLog = -10f;
                FeedingPhase lastPhase = seq.Phase;

                // 跑动平滑度采样：跑向饭盆期间，逐帧看"走了多远"和"在不在播走路动画"。
                // 走路动画在播但没位移 = 原地踏步；位移有跳动 = 抽帧。
                Vector3 prevPos = cat.transform.position;
                float runTime = 0f, runDist = 0f, walkAnimTime = 0f, walkStallTime = 0f;
                float backTime = 0f, backDist = 0f, backStallTime = 0f;
                float maxStep = 0f;
                bool shotWalk = false, shotEat = false;
                bool firstRunDone = false;
                int runWalkFrames = 0, runRunFrames = 0;

                // 尾巴摆动采样：记录尾骨世界朝向的左右摆幅（度）
                var tail = FindDeep(cat.transform, "tail.01");
                float tailMin = 999f, tailMax = -999f, tailPrev = 0f;
                bool tailInit = false;

                while (t < 70f)
                {
                    t += Time.deltaTime;

                    Vector3 flat = new Vector3(cat.transform.position.x, 0f, cat.transform.position.z);
                    Vector3 prevFlat = new Vector3(prevPos.x, 0f, prevPos.z);
                    float step = Vector3.Distance(flat, prevFlat);
                    bool walking = IsAnim(cat, "Cat_Walk") || IsAnim(cat, "Cat_Run");

                    if (seq.Phase == FeedingPhase.RunToFood && !firstRunDone)
                    {
                        runTime += Time.deltaTime;
                        runDist += step;
                        if (step > maxStep) maxStep = step;
                        if (IsAnim(cat, "Cat_Run")) runRunFrames++;
                        else if (IsAnim(cat, "Cat_Walk")) runWalkFrames++;
                        if (walking)
                        {
                            walkAnimTime += Time.deltaTime;
                            if (step < 0.002f) walkStallTime += Time.deltaTime;
                        }
                    }
                    else if (seq.Phase == FeedingPhase.ReturnToBed)
                    {
                        if (!firstRunDone) firstRunDone = true;
                        backTime += Time.deltaTime;
                        backDist += step;
                        if (walking && step < 0.002f) backStallTime += Time.deltaTime;
                    }

                    if (tail != null)
                    {
                        // 用"尾巴朝向与身体前方的水平夹角"来量摆动：
                        // 直接读 eulerAngles 会在 ±180 处翻转，算出来的摆幅是假的
                        Vector3 tailDir = tail.rotation * Vector3.up;
                        tailDir.y = 0f;
                        Vector3 fwd = cat.transform.forward;
                        fwd.y = 0f;
                        if (tailDir.sqrMagnitude > 1e-6f && fwd.sqrMagnitude > 1e-6f)
                        {
                            float ang = Vector3.Angle(fwd, tailDir);
                            if (!tailInit) { tailPrev = ang; tailInit = true; }
                            else
                            {
                                if (ang < tailMin) tailMin = ang;
                                if (ang > tailMax) tailMax = ang;
                            }
                        }
                    }

                    prevPos = cat.transform.position;

                    if (seq.Phase != lastPhase)
                    {
                        lastPhase = seq.Phase;
                        Log(string.Format("I 阶段 → {0}（盆={1} 颗粒={2} 猫={3}）",
                            lastPhase, bowl.State, CountKibbles(bowl), cat.transform.position.ToString("F2")));

                        // 嘴对碗：进入进食后量一次"嘴到碗里粮堆"的距离
                        if (lastPhase == FeedingPhase.Eating && head != null)
                        {
                            // 直接量"猫身上离粮最近的顶点"—— 这才是嘴有没有贴到粮的硬指标
                            var sk = cat.GetComponentInChildren<SkinnedMeshRenderer>(true);
                            var baked = new Mesh();
                            sk.BakeMesh(baked);
                            Vector3[] vs = baked.vertices;
                            Vector3 foodPos0 = bowl.foodVisualRoot != null ? bowl.foodVisualRoot.position : bowl.transform.position;
                            var m4 = Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one);
                            float bestD = 999f;
                            Vector3 nearest = Vector3.zero;
                            for (int vi = 0; vi < vs.Length; vi++)
                            {
                                Vector3 wv = m4.MultiplyPoint3x4(vs[vi]);
                                float dd = Vector3.Distance(wv, foodPos0);
                                if (dd < bestD) { bestD = dd; nearest = wv; }
                            }
                            Destroy(baked);
                            Vector3 muzzle = nearest;
                            Vector3 food = bowl.foodVisualRoot != null ? bowl.foodVisualRoot.position : bowl.transform.position;
                            float dy = muzzle.y - food.y;
                            Log(string.Format("I2 嘴对碗：最近顶点={0} 粮={1} 直线 {2:F3}m 水平 {3:F3}m 垂直差 {4:F3}m",
                                muzzle.ToString("F3"), food.ToString("F3"), bestD,
                                new Vector2(muzzle.x - food.x, muzzle.z - food.z).magnitude, dy));
                        }
                    }

                    if (t - lastKibbleLog > 2f && bowl.State == CatFoodBowlState.Eating)
                    {
                        lastKibbleLog = t;
                        Log(string.Format("J 进食中：food={0:F2} 颗粒={1}", bowl.FoodAmount, CountKibbles(bowl)));
                    }

                    // 跑动中拍一张（检查步幅/尾巴），刚进入 Eating 再拍一张（检查嘴对没对准碗）
                    if (!shotWalk && seq.Phase == FeedingPhase.RunToFood && t > 1.2f)
                    {
                        shotWalk = true;
                        StartCoroutine(Shot("walk"));
                    }
                    if (!shotEat && seq.Phase == FeedingPhase.Eating && t > 1.0f)
                    {
                        shotEat = true;
                        StartCoroutine(Shot("eat"));
                    }

                    if (seq.Phase == FeedingPhase.Sleeping) break;
                    yield return null;
                }

                Log(string.Format(
                    "P 跑动平滑度：去程 {0:F2}s/{1:F2}m 平均 {2:F2}m/s，动画期间原地踏步 {3:F3}s（占 {4:F1}%），单帧最大位移 {5:F3}m，状态帧数 Walk={6} Run={7}",
                    runTime, runDist, runTime > 0f ? runDist / runTime : 0f,
                    walkStallTime, walkAnimTime > 0f ? walkStallTime / walkAnimTime * 100f : 0f, maxStep,
                    runWalkFrames, runRunFrames));

                Log(string.Format(
                    "Q 回程平滑度：{0:F2}s/{1:F2}m 平均 {2:F2}m/s，原地踏步 {3:F3}s",
                    backTime, backDist, backTime > 0f ? backDist / backTime : 0f, backStallTime));

                yield return new WaitForSeconds(1.2f);
                Log(string.Format("K 睡觉：猫={0} yaw={1:F0} 睡姿={2} 位置锁={3} 床占用={4} 盆={5} 食物可见={6}",
                    cat.transform.position.ToString("F3"), cat.transform.eulerAngles.y,
                    IsAnim(cat, "Cat_Sleep"), cat.IsTransformLocked, bed.IsOccupied, bowl.State,
                    bowl.foodVisualRoot != null && bowl.foodVisualRoot.gameObject.activeSelf));

                // ---- 8) 第二轮：再次倒粮
                MovePlayer(fpc, new Vector3(0.30f, 0.02f, -3.60f));
                yield return new WaitForSeconds(0.3f);
                AimAt(fpc, new Vector3(-1.38f, 0.10f, -2.90f));
                yield return new WaitForSeconds(0.4f);
                yield return Shot("bed");

                MovePlayer(fpc, new Vector3(-1.40f, 0.02f, -6.18f));
                yield return new WaitForSeconds(0.4f);
                AimAt(fpc, new Vector3(-2.02f, 0.05f, -6.10f));
                yield return new WaitForSeconds(0.4f);
                target = fpc != null ? fpc.CurrentTarget : null;
                Log("L 第二轮准星：target=" + (target == null ? "null" : target.GetType().Name + " 提示=[" + target.Prompt + "]"));

                bowl.DebugHoldPour(bowl.pourHoldSeconds + 0.15f);

                t = 0f;
                lastPhase = seq.Phase;
                bool wokeUp = false, ateAgain = false;
                while (t < 45f)
                {
                    t += Time.deltaTime;
                    if (!wokeUp && !cat.IsTransformLocked) { wokeUp = true; Log("M 猫被叫醒，解除位置锁"); }
                    if (seq.Phase == FeedingPhase.Eating) { ateAgain = true; }
                    if (seq.Phase != lastPhase)
                    {
                        lastPhase = seq.Phase;
                        Log(string.Format("N 第二轮阶段 → {0}（盆={1} 猫={2}）", lastPhase, bowl.State, cat.transform.position.ToString("F2")));
                    }
                    if (ateAgain && seq.Phase == FeedingPhase.ReturnToBed) break;
                    yield return null;
                }

                Log(string.Format("O 第二轮结果：叫醒={0} 再次进食={1} 盆={2}", wokeUp, ateAgain, bowl.State));

                // ---- 9) 收尾：收提示
                if (ui != null) ui.HideFeedHint(true);

                if (tail != null && tailMin < 900f)
                    Log(string.Format("X 尾巴摆动：与身体前方夹角 {0:F1}° ~ {1:F1}°（摆幅 {2:F1}°）",
                        tailMin, tailMax, tailMax - tailMin));
                else
                    Log("X 尾巴摆动：没采到数据（tail.01 未找到）");

                Log(string.Format("Y 铃铛：全程响 {0} 次（移动时才响）", NOCTURNE.Cat.CatBell.TotalRings));
                Log(string.Format("Z 控制台统计：警告 {0} 条，错误 {1} 条", _warnCount, _errCount));
                if (_problems.Length > 0) Log("Z2 问题清单：\n" + _problems.ToString().TrimEnd());

                Log("DONE 全流程结束");
                Time.timeScale = 1f;
            }

            static Transform FindDeep(Transform root, string name)
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
                return null;
            }

            static IEnumerator Shot(string name)
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../.workbuddy/shot_" + name + ".png"));
                ScreenCapture.CaptureScreenshot(path);
                Log("SHOT " + name + " → " + path);
                yield return null;
                yield return null;
            }

            static IEnumerator WaitPhase(CatFeedingSequenceController seq, FeedingPhase want, float timeout, System.Action<float> elapsed)
            {
                float t = 0f;
                while (t < timeout && seq.Phase != want) { t += Time.deltaTime; elapsed(t); yield return null; }
            }

            static void MovePlayer(FirstPersonController fpc, Vector3 pos)
            {
                if (fpc == null) return;
                var cc = fpc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                fpc.transform.position = pos;
                if (cc != null) cc.enabled = true;
            }

            static void AimAt(FirstPersonController fpc, Vector3 world)
            {
                if (fpc == null || fpc.cameraRig == null) return;
                Vector3 d = (world - fpc.cameraRig.position).normalized;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                fpc.SetLook(yaw, pitch);
            }

            static int CountKibbles(CatFoodBowl bowl)
            {
                int n = 0;
                if (bowl.kibbles == null) return 0;
                for (int i = 0; i < bowl.kibbles.Length; i++)
                    if (bowl.kibbles[i] != null && bowl.kibbles[i].gameObject.activeSelf) n++;
                return n;
            }

            static bool IsAnim(CatAgent cat, string name)
            {
                var a = cat.GetComponent<Animator>();
                return a != null && a.GetCurrentAnimatorStateInfo(0).IsName(name);
            }
        }
    }
}
#endif
