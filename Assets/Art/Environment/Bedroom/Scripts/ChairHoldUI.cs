using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOCTURNE
{
    /// <summary>
    /// 长按坐下的圆形进度 UI + 起身提示。
    ///
    /// 进度环用 Image.Filled + FillMethod.Radial360（顺时针从顶部开始），
    /// 0%~100% 一圈填满；松开即归零。所有 Image 预先放在场景里，本脚本只改 fillAmount。
    /// </summary>
    [DisallowMultipleComponent]
    public class ChairHoldUI : MonoBehaviour
    {
        [Header("引用")]
        public ChairInteractable chair;
        public PlayerSittingController sitting;

        [Header("圆形进度")]
        public CanvasGroup holdGroup;
        [Tooltip("进度环（Filled / Radial360，顺时针）")]
        public Image ringFill;
        public TextMeshProUGUI holdText;

        [Header("起身提示")]
        public CanvasGroup standUpGroup;
        public TextMeshProUGUI standUpText;

        [Header("参数")]
        public float fadeInSpeed = 7f;
        public float fadeOutSpeed = 9f;
        [Tooltip("进度填满后停留多久再隐藏")]
        public float completeHoldSeconds = 0.25f;

        float _alpha;
        float _completeTimer;

        void Awake()
        {
            if (holdText != null) holdText.text = "按住 E 坐下";
            if (standUpGroup != null) standUpGroup.alpha = 0f;
            if (holdGroup != null) holdGroup.alpha = 0f;
            ApplyFill(0f);
        }

        void Update()
        {
            UpdateHold();
            UpdateStandUp();
        }

        void UpdateHold()
        {
            if (chair == null || holdGroup == null) return;

            float p = chair.HoldProgress;
            bool complete = p >= 0.999f;

            if (complete)
            {
                _completeTimer += Time.deltaTime;
                float target = _completeTimer < completeHoldSeconds ? 1f : 0f;
                _alpha = Mathf.MoveTowards(_alpha, target, fadeOutSpeed * Time.deltaTime);
            }
            else
            {
                _completeTimer = 0f;
                float target = (chair.IsHolding && p > 0.005f) ? 1f : 0f;
                float speed = target > _alpha ? fadeInSpeed : fadeOutSpeed;
                _alpha = Mathf.MoveTowards(_alpha, target, speed * Time.deltaTime);
            }
            holdGroup.alpha = _alpha;

            if (holdText != null)
                holdText.text = complete ? "坐下" : "按住 E 坐下";

            ApplyFill(p);
        }

        void ApplyFill(float p)
        {
            if (ringFill != null) ringFill.fillAmount = Mathf.Clamp01(p);
        }

        void UpdateStandUp()
        {
            if (standUpGroup == null) return;
            bool show = sitting != null && sitting.IsSitting;
            float target = show ? 1f : 0f;
            standUpGroup.alpha = Mathf.MoveTowards(standUpGroup.alpha, target, fadeOutSpeed * Time.deltaTime);
        }
    }
}