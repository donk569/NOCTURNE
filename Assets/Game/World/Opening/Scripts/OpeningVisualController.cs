using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NOCTURNE.Opening
{
    /// <summary>
    /// 开场画面控制：黑屏遮罩 + 睡醒模糊。
    ///
    /// 模糊走一个独立的全局 Volume（OpeningFX），里面只放 DepthOfField。
    /// 淡出时直接降 Volume.weight，所以不会污染场景原有的 Global Volume 后处理。
    /// </summary>
    [DisallowMultipleComponent]
    public class OpeningVisualController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("全屏黑色遮罩的 CanvasGroup")]
        public CanvasGroup blackOverlay;

        [Tooltip("开场专用的后处理 Volume（DepthOfField）。留空则没有模糊效果")]
        public Volume openingVolume;

        [Header("参数")]
        [Tooltip("开场时 Volume 的满强度")]
        [Range(0f, 1f)] public float fullBlurWeight = 1f;

        [Tooltip("黑屏淡出曲线：>1 表示后段才真正亮起来，更像睁眼")]
        public float blackOutCurvePower = 0.75f;

        public float BlackAlpha
        {
            get { return blackOverlay != null ? blackOverlay.alpha : 0f; }
        }

        public void ApplyImmediate(float blackAlpha, float blurWeight)
        {
            if (blackOverlay != null) blackOverlay.alpha = Mathf.Clamp01(blackAlpha);
            if (openingVolume != null) openingVolume.weight = Mathf.Clamp01(blurWeight);
        }

        /// <summary>开场初始化：全黑 + 满强度模糊。</summary>
        public void ResetToBlack()
        {
            ApplyImmediate(1f, fullBlurWeight);
        }

        /// <summary>纯黑屏渐变（开场期间用不到，留给以后的分镜转场）。</summary>
        public IEnumerator FadeBlack(float targetAlpha, float duration)
        {
            if (blackOverlay == null) yield break;

            float from = blackOverlay.alpha;
            if (duration <= 0f) { blackOverlay.alpha = targetAlpha; yield break; }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                blackOverlay.alpha = Mathf.Lerp(from, targetAlpha, k * k * (3f - 2f * k));
                yield return null;
            }
            blackOverlay.alpha = targetAlpha;
        }

        /// <summary>
        /// 睁眼：黑屏淡出 + 模糊衰减同时进行。
        /// 黑屏用幂曲线，前段还留有遮挡，后段才真正看清，接近真实的睡醒过程。
        /// </summary>
        public IEnumerator RecoverVision(float duration)
        {
            float startBlack = blackOverlay != null ? blackOverlay.alpha : 0f;
            float startWeight = openingVolume != null ? openingVolume.weight : 0f;

            if (duration <= 0f)
            {
                ApplyImmediate(0f, 0f);
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);

                float blackK = Mathf.Pow(k, Mathf.Max(0.05f, blackOutCurvePower));
                blackK = blackK * blackK * (3f - 2f * blackK);

                if (blackOverlay != null)
                    blackOverlay.alpha = Mathf.Lerp(startBlack, 0f, blackK);

                if (openingVolume != null)
                {
                    // 模糊比黑屏恢复得更快一点，睁眼后先"形"后"清"
                    float blurK = Mathf.Clamp01(k / 0.85f);
                    blurK = blurK * blurK * (3f - 2f * blurK);
                    openingVolume.weight = Mathf.Lerp(startWeight, 0f, blurK);
                }

                yield return null;
            }

            ApplyImmediate(0f, 0f);
        }
    }
}
