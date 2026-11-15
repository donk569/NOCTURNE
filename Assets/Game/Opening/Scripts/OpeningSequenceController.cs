using System.Collections;
using UnityEngine;

namespace NOCTURNE.Opening
{
    /// <summary>
    /// 开场序幕总流程。
    ///
    /// 阶段：全黑静默 → 五段内心独白 → 安静 → 两声猫叫 → 被子与呼吸 →
    ///       缓慢睁眼（黑屏淡出 + 模糊恢复）→ 躺卧自由观察 → 玩家按 E 起床 → Playing
    ///
    /// 本脚本只做编排，画面/文字/音频/躺卧各自有独立控制器。
    /// 开场期间时间与需求系统不推进：所有相关系统应查询 GameStateManager.IsGameplayActive。
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public class OpeningSequenceController : MonoBehaviour
    {
        [Header("引用")]
        public GameStateManager stateManager;
        public PlayerLyingController lying;
        public OpeningDialogueUI dialogue;
        public OpeningVisualController visual;
        public OpeningAudioController audioController;
        public NOCTURNE.Cat.CatAgent catAgent;
        [Tooltip("猫咪开场行为。睁眼那一帧由本脚本通知它开始叫，黑屏期间它一声都不能出")]
        public CatOpeningController catOpening;

        [Header("独白文案（一段一条，可含换行）")]
        [TextArea(2, 8)]
        public string[] paragraphs = new string[0];

        [Header("独白节奏")]
        [Tooltip("每段停留秒数。长度不足或为 0 时，按字数自动估算")]
        public float[] paragraphHoldSeconds = new float[0];
        public float lineFadeIn = 1.6f;
        public float lineFadeOut = 1.4f;
        [Tooltip("段与段之间的空档")]
        public float paragraphGap = 0.9f;
        [Tooltip("最后一段淡出后的停顿")]
        public float endingPause = 2.5f;

        [Header("阶段时间")]
        [Tooltip("进入场景后保持全黑静默的时长")]
        public float blackHoldSeconds = 2.5f;
        [Tooltip("独白结束后、猫叫之前的安静")]
        public float silenceBeforeMeow = 1.5f;
        [Tooltip("第二声猫叫之后多久播被子摩擦")]
        public float meowToBlanketDelay = 0.25f;
        [Tooltip("被子声之后多久开始睁眼")]
        public float meowToEyeOpenDelay = 1.4f;
        [Tooltip("睁眼过程时长（黑屏淡出 + 模糊恢复）")]
        public float eyeOpenDuration = 4.2f;
        [Tooltip("画面清晰后等多久显示「环顾四周」提示")]
        public float observationDelay = 2f;
        [Tooltip("「环顾四周」提示停留时长")]
        public float observationHintSeconds = 3f;
        [Tooltip("提示淡出后多久出现「[E] 起床」")]
        public float getUpHintDelay = 0.6f;

        [Header("启动")]
        [Tooltip("进入 Play 立刻播放开场。接主菜单后可关掉，改由 BeginSequence() 触发")]
        public bool autoStart = true;

        [Tooltip("开场期间是否暂停猫咪 AI（推荐开启，避免猫在独白时乱走）")]
        public bool pauseCatDuringOpening = true;

        [Tooltip("起床后是否由本脚本直接恢复猫咪 AI。挂了 CatOpeningController 时请关掉，它会自己接管离床时序")]
        public bool resumeCatAfterOpening = false;

        [Header("自动估算停留时长")]
        [Tooltip("每个字折算多少秒（段落未配置停留时间时使用）")]
        public float secondsPerCharacter = 0.115f;
        public float minParagraphHold = 2.2f;

        bool _running;

        /// <summary>开场全部结束（玩家已站起）时触发。</summary>
        public event System.Action OpeningFinished;

        public bool IsRunning { get { return _running; } }

        /// <summary>玩家是否已经睁眼（黑屏开始淡出）。猫叫从这一刻才允许出现。</summary>
        public bool EyeOpened { get; private set; }

        void Start()
        {
            if (catOpening == null && catAgent != null) catOpening = catAgent.GetComponent<CatOpeningController>();
            if (catOpening == null) catOpening = FindObjectOfType<CatOpeningController>();
            if (autoStart) StartCoroutine(BeginNextFrame());
        }

        IEnumerator BeginNextFrame()
        {
            // 等一帧，确保所有 Awake/OnEnable 都跑完再接管相机
            yield return null;
            BeginSequence();
        }

        /// <summary>启动 / 重新开始开场。可被主菜单调用。</summary>
        public void BeginSequence()
        {
            StopAllCoroutines();
            ResetEverything();
            StartCoroutine(RunSequence());
        }

        void ResetEverything()
        {
            _running = true;
            EyeOpened = false;

            if (stateManager != null) stateManager.ForceState(GameState.Opening);

            if (lying != null)
            {
                lying.ResetLying();
                lying.GetUpFinished -= OnGetUpFinished;
                lying.GetUpFinished += OnGetUpFinished;
            }

            if (dialogue != null) dialogue.HideAll();
            if (visual != null) visual.ResetToBlack();
            if (audioController != null) audioController.StopAll();
            if (catOpening != null) catOpening.StopOpeningMeow();

            // 开场不该有手电：否则一束光会跟着玩家照在床上
            if (lying != null && lying.fpc != null) lying.fpc.SetFlashlight(false);

            if (catAgent != null && pauseCatDuringOpening) catAgent.enabled = false;
        }

        // ---------------------------------------------------------------- 主流程

        IEnumerator RunSequence()
        {
            // ---- 阶段 1：全黑静默 ----
            if (lying != null) lying.BeginLying(false);
            if (audioController != null) audioController.PlayAmbience();
            if (blackHoldSeconds > 0f) yield return new WaitForSeconds(blackHoldSeconds);

            // ---- 阶段 2：五段内心独白 ----
            if (paragraphs != null)
            {
                for (int i = 0; i < paragraphs.Length; i++)
                {
                    string text = paragraphs[i];
                    if (string.IsNullOrEmpty(text)) continue;

                    float hold = HoldFor(i, text);
                    if (dialogue != null)
                        yield return dialogue.ShowParagraph(text, lineFadeIn, hold, lineFadeOut);
                    else
                        yield return new WaitForSeconds(hold);

                    if (paragraphGap > 0f) yield return new WaitForSeconds(paragraphGap);
                }
            }

            if (endingPause > 0f) yield return new WaitForSeconds(endingPause);

            // ---- 阶段 3：安静（这里不能有任何猫叫，猫叫要等到睁眼） ----
            if (silenceBeforeMeow > 0f) yield return new WaitForSeconds(silenceBeforeMeow);

            // 曾经这里有两声猫叫（audioController.MeowSequence）。
            // 需求变了：黑屏独白期间一声猫叫都不能有，第一声必须发生在睁眼的那一刻，
            // 所以猫叫整体挪到下面 eye-open 的位置，由 CatOpeningController 负责呼吸式地叫。
            if (meowToBlanketDelay > 0f) yield return new WaitForSeconds(meowToBlanketDelay);
            if (audioController != null) audioController.PlayBlanket();
            if (audioController != null) audioController.PlayBreath();

            if (meowToEyeOpenDelay > 0f) yield return new WaitForSeconds(meowToEyeOpenDelay);

            // ---- 阶段 4：缓慢睁眼 ----
            if (lying != null) lying.EnableLook(true);

            // 睁眼这一帧才允许猫出声
            EyeOpened = true;
            if (catOpening != null) catOpening.BeginOpeningMeow();

            if (visual != null) yield return visual.RecoverVision(eyeOpenDuration);

            if (stateManager != null) stateManager.SetState(GameState.Lying);

            // ---- 阶段 5：躺卧观察 ----
            if (observationDelay > 0f) yield return new WaitForSeconds(observationDelay);

            if (dialogue != null)
                yield return dialogue.ShowHint(dialogue.observationHint, observationHintSeconds);

            if (getUpHintDelay > 0f) yield return new WaitForSeconds(getUpHintDelay);

            if (dialogue != null) dialogue.ShowHintPersistent(dialogue.getUpHint);
            if (lying != null) lying.AllowGetUp(true);

            // ---- 阶段 6：等待玩家主动起床（由 PlayerLyingController 驱动） ----
            _running = false;
        }

        float HoldFor(int index, string text)
        {
            if (paragraphHoldSeconds != null && index < paragraphHoldSeconds.Length)
            {
                float configured = paragraphHoldSeconds[index];
                if (configured > 0f) return configured;
            }
            float estimated = text.Replace("\n", "").Length * secondsPerCharacter;
            return Mathf.Max(minParagraphHold, estimated);
        }

        // ---------------------------------------------------------------- 起床完成

        void OnGetUpFinished()
        {
            if (stateManager != null) stateManager.SetState(GameState.Playing);

            if (dialogue != null)
            {
                dialogue.HideHint(dialogue.getUpHint, false);
                dialogue.HideHint(dialogue.observationHint, true);
                dialogue.ClearDialogue();
            }

            if (visual != null) visual.ApplyImmediate(0f, 0f);

            // 猫 AI 在正式游戏阶段才启动
            if (catAgent != null && resumeCatAfterOpening) catAgent.enabled = true;

            if (OpeningFinished != null) OpeningFinished();
        }

        void OnDestroy()
        {
            if (lying != null) lying.GetUpFinished -= OnGetUpFinished;
        }
    }
}
