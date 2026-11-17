using System.Collections;
using TMPro;
using UnityEngine;

namespace NOCTURNE.Opening
{
    /// <summary>
    /// 开场文字演出。
    /// 所有 UI 对象都在场景里预先建好（OpeningCanvas 下），这里只做淡入淡出，
    /// 不会在运行时 Instance 任何 UI。
    /// </summary>
    [DisallowMultipleComponent]
    public class OpeningDialogueUI : MonoBehaviour
    {
        [Header("引用（场景中预先创建）")]
        public CanvasGroup dialogueGroup;
        public TextMeshProUGUI dialogueText;

        [Header("提示")]
        public CanvasGroup observationHint;
        public CanvasGroup getUpHint;

        [Header("喂食任务提示（场景中预先创建）")]
        [Tooltip("任务提示的 CanvasGroup（开场 UI 的一层）")]
        public CanvasGroup feedHint;
        [Tooltip("任务提示文字")]
        public TextMeshProUGUI feedHintText;

        [Header("淡入淡出")]
        [Tooltip("提示文字淡入时长")]
        public float hintFadeIn = 0.55f;
        [Tooltip("提示文字淡出时长")]
        public float hintFadeOut = 0.9f;

        /// <summary>整段独白播完后是否清空文本。</summary>
        public void ClearDialogue()
        {
            if (dialogueText != null) dialogueText.text = "";
            if (dialogueGroup != null) dialogueGroup.alpha = 0f;
        }

        /// <summary>把所有文字类 UI 归零（开场初始化 / 结束时调用）。</summary>
        public void HideAll()
        {
            ClearDialogue();
            SetGroup(observationHint, 0f);
            SetGroup(getUpHint, 0f);
            SetGroup(feedHint, 0f);
        }

        /// <summary>
        /// 显示喂食任务提示（淡入常显）。复用开场 Canvas 与淡入淡出曲线，
        /// 不在运行时创建任何 UI 对象。
        /// </summary>
        public void ShowFeedHint(string text)
        {
            if (feedHint == null) return;
            if (feedHintText != null && !string.IsNullOrEmpty(text)) feedHintText.text = text;
            StartCoroutine(Fade(feedHint, feedHint.alpha, 1f, hintFadeIn));
        }

        /// <summary>隐藏喂食任务提示（淡出）。</summary>
        public void HideFeedHint(bool instant = false)
        {
            if (feedHint == null) return;
            if (instant) { feedHint.alpha = 0f; return; }
            StartCoroutine(Fade(feedHint, feedHint.alpha, 0f, hintFadeOut));
        }

        /// <summary>淡入 → 停留 → 淡出，一段独白。</summary>
        public IEnumerator ShowParagraph(string text, float fadeIn, float hold, float fadeOut)
        {
            if (dialogueText == null || dialogueGroup == null)
            {
                if (hold > 0f) yield return new WaitForSeconds(hold);
                yield break;
            }

            dialogueText.text = text;
            yield return Fade(dialogueGroup, dialogueGroup.alpha, 1f, fadeIn);

            if (hold > 0f) yield return new WaitForSeconds(hold);

            yield return Fade(dialogueGroup, 1f, 0f, fadeOut);
            dialogueText.text = "";
        }

        /// <summary>显示一条提示，holdSeconds 内自动淡出；<=0 表示常显。</summary>
        public IEnumerator ShowHint(CanvasGroup group, float holdSeconds)
        {
            if (group == null) yield break;

            yield return Fade(group, group.alpha, 1f, hintFadeIn);

            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
                yield return Fade(group, 1f, 0f, hintFadeOut);
            }
        }

        /// <summary>常显提示（不等待）。</summary>
        public void ShowHintPersistent(CanvasGroup group)
        {
            if (group == null) return;
            StartCoroutine(Fade(group, group.alpha, 1f, hintFadeIn));
        }

        public void HideHint(CanvasGroup group, bool instant = false)
        {
            if (group == null) return;
            if (instant) { group.alpha = 0f; return; }
            StartCoroutine(Fade(group, group.alpha, 0f, hintFadeOut));
        }

        /// <summary>透明度渐变。from 由调用方给，避免协程被外部改 alpha 后跳变。</summary>
        public IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null) yield break;

            if (duration <= 0f)
            {
                group.alpha = to;
                yield break;
            }

            float t = 0f;
            group.alpha = from;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                // 平滑一点，避免线性渐变看起来"机械"
                group.alpha = Mathf.Lerp(from, to, k * k * (3f - 2f * k));
                yield return null;
            }
            group.alpha = to;
        }

        static void SetGroup(CanvasGroup g, float a)
        {
            if (g != null) g.alpha = a;
        }
    }
}
