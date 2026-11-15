using UnityEngine;
using UnityEditor;

namespace NOCTURNE.DebugTools
{
    /// <summary>
    /// 编辑器内截图工具：摆一台临时相机渲染到 PNG。
    /// 只给开发期验收用，不影响运行时。
    /// </summary>
    public static class DevShot
    {
        public static string Shot(string path, Vector3 pos, Vector3 look, float fov = 55f, int w = 1280, int h = 720)
        {
            var go = new GameObject("DevShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            go.transform.position = pos;
            go.transform.LookAt(look);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            return path;
        }
    }
}
