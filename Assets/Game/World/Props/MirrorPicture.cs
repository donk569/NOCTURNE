using UnityEngine;

namespace NOCTURNE
{
    /// <summary>
    /// 固定画面镜子。
    ///
    /// 不再做实时反射（PlanarMirror 那种镜像相机既费性能又容易出穿刺/反光问题），
    /// 改成：镜面用一张预先做好的画面 + Unlit 材质，玩家走近才淡入，走远淡出。
    ///
    /// 之所以用 Unlit：Lit 材质在镜面上会产生高光/环境反射，正是"刺眼反光"的来源。
    /// 需要换画面时调用 SetPicture()，或直接在 Inspector 里改 mirrorPicture。
    /// </summary>
    [DisallowMultipleComponent]
    public class MirrorPicture : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("镜面渲染器。留空则用本物体的 MeshRenderer")]
        public MeshRenderer mirrorRenderer;

        [Tooltip("镜前的触发区，留空则不自动触发（只显示静音画面）")]
        public TriggerVolume trigger;

        [Header("画面")]
        [Tooltip("固定镜面画面。留空则保持黑镜面")]
        public Texture2D mirrorPicture;

        [Tooltip("备用画面，运行时可用 SetPicture(index) 切换（留给以后的幻觉效果）")]
        public Texture2D[] alternativePictures;

        [Header("亮度")]
        [Tooltip("没有玩家靠近时的颜色（近黑，像暗玻璃）")]
        public Color idleColor = new Color(0.020f, 0.022f, 0.026f, 1f);

        [Tooltip("玩家靠近时画面达到的最高亮度。别调太亮，否则会像发光屏")]
        [Range(0f, 1f)] public float activeBrightness = 0.62f;

        [Header("过渡")]
        public float fadeInTime = 0.9f;
        public float fadeOutTime = 0.7f;

        Material _mat;
        MeshRenderer _mr;
        float _t;            // 0 = idle，1 = 完全显示
        float _target;
        bool _hooked;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            if (mirrorRenderer == null) mirrorRenderer = GetComponent<MeshRenderer>();
            _mr = mirrorRenderer;
            EnsureMaterial();
        }

        void OnEnable()
        {
            HookTrigger();
        }

        void OnDisable()
        {
            UnhookTrigger();
        }

        void HookTrigger()
        {
            if (_hooked || trigger == null) return;
            trigger.Entered += OnTriggerEntered;
            trigger.Exited += OnTriggerExited;
            _hooked = true;
            if (trigger.IsOccupied) _target = 1f;
        }

        void UnhookTrigger()
        {
            if (!_hooked || trigger == null) return;
            trigger.Entered -= OnTriggerEntered;
            trigger.Exited -= OnTriggerExited;
            _hooked = false;
        }

        void EnsureMaterial()
        {
            if (_mr == null) return;
            if (_mat != null) return;

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                _mat = new Material(unlit);
                _mat.name = "MirrorPicture_Runtime";
            }
            else if (_mr.sharedMaterial != null)
            {
                _mat = new Material(_mr.sharedMaterial);
            }
            else return;

            _mat.SetTexture(BaseMapId, mirrorPicture);
            ApplyTint(_mat, idleColor);
            _mr.sharedMaterial = _mat;
        }

        void ApplyTint(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        void OnTriggerEntered(Collider other) { _target = 1f; }
        void OnTriggerExited(Collider other) { _target = 0f; }

        void Update()
        {
            if (_mat == null) return;

            float speed = _target > _t ? fadeInTime : fadeOutTime;
            if (speed <= 0.001f) _t = _target;
            else _t = Mathf.MoveTowards(_t, _target, Time.deltaTime / speed);

            Color active = new Color(activeBrightness, activeBrightness, activeBrightness, 1f);
            Color c = Color.Lerp(idleColor, active, Smooth(_t));
            ApplyTint(_mat, c);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>运行中替换镜面画面（留给以后的幻觉/多画面系统）。</summary>
        public void SetPicture(Texture2D tex)
        {
            mirrorPicture = tex;
            EnsureMaterial();
            if (_mat != null) _mat.SetTexture(BaseMapId, tex);
        }

        public void SetPicture(int index)
        {
            if (alternativePictures == null || index < 0 || index >= alternativePictures.Length) return;
            SetPicture(alternativePictures[index]);
        }

        /// <summary>强制显示/隐藏（不走触发器）。</summary>
        public void SetVisible(bool visible)
        {
            _target = visible ? 1f : 0f;
        }
    }
}
