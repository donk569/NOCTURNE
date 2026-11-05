using UnityEngine;

namespace NOCTURNE
{
    /// <summary>
    /// 平面镜：用一台镜像相机把场景渲染进 RT，贴到镜面材质上。
    /// 挂在镜面物体本身（PR_BodyMirror_Glass）。法线默认用 transform.forward，
    /// 模型没按预期朝向时可在 Inspector 里改 mirrorNormal 覆盖。
    /// 性能：每帧一次额外渲染，可用 renderInterval 降到每 N 帧一次。
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [ExecuteAlways]
    public class PlanarMirror : MonoBehaviour
    {
        [Header("分辨率")]
        public int textureWidth = 768;
        public int textureHeight = 1152;

        [Header("采样")]
        [Tooltip("每 N 帧渲染一次。1 = 每帧，2~3 在 VR 里更省")]
        [Range(1, 6)] public int renderInterval = 1;
        [Tooltip("镜面平面沿法线的偏移，避免把镜面自己拍进去")]
        public float clipPlaneOffset = 0.02f;
        public LayerMask cullMask = ~0;
        [Tooltip("主相机，留空则自动用 Camera.main")]
        public Camera mainCamera;

        [Header("朝向")]
        [Tooltip("镜面法线（世界方向）。留空则用 transform.forward")]
        public Vector3 mirrorNormal = Vector3.zero;

        Camera _cam;
        RenderTexture _rt;
        MeshRenderer _mr;
        Material _mat;
        int _lastFrame = -999;
        bool _rendering;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        void Start()
        {
            EnsureInit();
        }

        void EnsureInit()
        {
            if (_cam != null && _rt != null) return;
            if (!TryGetComponent(out MeshRenderer mr)) return;
            _mr = mr;

            if (_rt == null)
            {
                _rt = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "PlanarMirrorRT_" + name,
                    hideFlags = HideFlags.DontSave,
                    antiAliasing = 1
                };
                _rt.Create();
            }

            if (_cam == null)
            {
                var go = new GameObject("MirrorCam_" + name);
                go.transform.SetParent(transform, false);
                go.hideFlags = HideFlags.HideAndDontSave;

                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.004f, 0.005f, 0.006f, 1f);
                _cam.cullingMask = cullMask;
                _cam.targetTexture = _rt;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 60f;
                _cam.allowHDR = true;
                _cam.allowMSAA = false;
            }

            // 镜面用独立材质实例，避免污染共享材质
            if (_mat == null && _mr.sharedMaterial != null)
            {
                _mat = new Material(_mr.sharedMaterial);
                _mat.name = _mr.sharedMaterial.name + "_MirrorInstance";
                _mr.material = _mat;
                _mat.SetTexture(BaseMapId, _rt);
                _mat.SetColor("_BaseColor", Color.white);
                _mat.SetFloat("_Metallic", 0f);
                _mat.SetFloat("_Smoothness", 0f);
            }
        }

        Vector3 Normal
        {
            get
            {
                // 注意：FBX 导入的模型常带 ±90° 轴补偿，transform.forward 未必是镜面法线，
                // 所以这里按世界方向解释 mirrorNormal，Inspector 里直接填世界法线即可。
                Vector3 n = mirrorNormal.sqrMagnitude > 1e-6f ? mirrorNormal : transform.forward;
                return n.normalized;
            }
        }

        void OnWillRenderObject()
        {
            EnsureInit();
        }

        // 注意：不要用 OnWillRenderObject 里 Camera.Render()。
        // URP 下那是在渲染管线内部再次启动一次渲染，会报
        // "Recursive rendering is not supported in SRP"，镜面永远是黑的。
        // 改在 LateUpdate 里渲染：此时不在管线上下文中，URP 允许。
        void LateUpdate()
        {
            EnsureInit();
            if (_cam == null || _rt == null) return;
            if (_rendering) return;
            if (Time.frameCount - _lastFrame < renderInterval) return;

            var src = mainCamera != null ? mainCamera : Camera.main;
            if (src == null || src == _cam) return;

            Vector3 n = Normal;
            Vector3 pos = transform.position;

            // 相机在镜面背面就不必渲染
            if (Vector3.Dot(src.transform.position - pos, n) <= 0f) return;
            // 镜面不在视野里也不用渲
            if (_mr != null && !GeometryUtility.TestPlanesAABB(
                    GeometryUtility.CalculateFrustumPlanes(src), _mr.bounds))
                return;

            Vector3 camPos = src.transform.position;

            // 主相机关于镜面平面做镜像
            float dist = Vector3.Dot(camPos - pos, n);
            Vector3 mirrorPos = camPos - 2f * dist * n;
            Vector3 fwd = Vector3.Reflect(src.transform.forward, n);
            Vector3 up = Vector3.Reflect(src.transform.up, n);

            if (fwd.sqrMagnitude < 1e-6f) return;

            _cam.transform.SetPositionAndRotation(mirrorPos, Quaternion.LookRotation(fwd, up));
            _cam.fieldOfView = src.fieldOfView;
            _cam.aspect = src.aspect;
            _cam.projectionMatrix = src.projectionMatrix;

            // 别把镜面自己拍进反射里
            _rendering = true;
            bool wasEnabled = _mr.enabled;
            _mr.enabled = false;
            _cam.Render();
            _mr.enabled = wasEnabled;
            _rendering = false;

            _lastFrame = Time.frameCount;
        }

        void OnDestroy()
        {
            // [ExecuteAlways] 下编辑模式也会走这里，Destroy 在编辑模式是空操作且会报警
            if (_rt != null)
            {
                _rt.Release();
                SafeDestroy(_rt);
            }
            if (_mat != null) SafeDestroy(_mat);
            if (_cam != null) SafeDestroy(_cam.gameObject);
        }

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Gizmos.DrawRay(transform.position, Normal * 0.5f);
        }
    }
}
