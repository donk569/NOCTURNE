using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using NOCTURNE.Cat;

namespace NOCTURNE.DebugTools
{
    /// <summary>
    /// 走路动画的"眼睛"检查：进入 Play Mode 后把猫摆到客厅空地，
    /// 用一台跟拍相机对 walk / run 各录一组定帧 PNG，同时把骨骼世界坐标写进 TSV。
    ///
    /// 只看 Animator 状态名和 clip 绑定数量证明不了"腿在动"，所以这里直接出图。
    /// 启动条件：工程根目录存在 `.workbuddy/walkqa.on`。
    /// </summary>
    public static class CatWalkQA
    {
        const string MarkerRelative = "../.workbuddy/walkqa.on";
        const string DirRelative = "../.workbuddy/walkqa";

        public static string MarkerPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, MarkerRelative)); } }
        public static string DirPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, DirRelative)); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!File.Exists(MarkerPath)) return;
            var go = new GameObject("CatWalkQARunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        public sealed class Runner : MonoBehaviour
        {
            static readonly List<string> _log = new List<string>();

            static void L(string s) { _log.Add(s); Debug.Log("[WALKQA] " + s); }

            IEnumerator Start()
            {
                if (Directory.Exists(DirPath)) Directory.Delete(DirPath, true);
                Directory.CreateDirectory(DirPath);

                // 让开场的摄像机动画先跑完，避免抢相机
                yield return new WaitForSeconds(1.5f);

                // 关掉所有会抢猫的流程脚本，让 QA 独占
                var openers = new List<MonoBehaviour>();
                var allMb = FindObjectsOfType<MonoBehaviour>();
                for (int i = 0; i < allMb.Length; i++)
                {
                    if (allMb[i] == null) continue;
                    string tn = allMb[i].GetType().Name;
                    if (tn == "CatOpeningController" || tn == "OpeningSequenceController" ||
                        tn == "CatFeedingSequenceController" || tn == "PlayerLyingController")
                    {
                        allMb[i].enabled = false;
                        openers.Add(allMb[i]);
                    }
                }
                L("disabled flow scripts: " + openers.Count);

                var cat = FindObjectOfType<CatAgent>();
                if (cat == null) { L("FAIL no CatAgent"); Finish(); yield break; }

                Color oldAmb = RenderSettings.ambientLight;
                float oldInt = RenderSettings.ambientIntensity;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
                RenderSettings.ambientIntensity = 1f;

                var camGo = new GameObject("QA_Cam");
                var cam = camGo.AddComponent<Camera>();
                var src = Camera.main;
                if (src != null)
                {
                    cam.fieldOfView = 42f;
                    cam.nearClipPlane = 0.02f;
                    cam.farClipPlane = 60f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
                }
                cam.enabled = false;

                var lightGo = new GameObject("QA_Light");
                var li = lightGo.AddComponent<Light>();
                li.type = LightType.Point;
                li.range = 6f;
                li.intensity = 12f;
                li.color = new Color(1f, 0.97f, 0.92f);
                lightGo.transform.position = cat.transform.position + new Vector3(0.4f, 1.4f, 0.4f);

                cat.enabled = true;
                cat.SetTransformLocked(false);
                cat.SuspendAi(true);
                cat.canMove = true;
                L("nav ready=" + cat.IsNavigationReady + " locked=" + cat.IsTransformLocked);

                // 客厅空地：床尾往门口走的一段
                Vector3 startA = new Vector3(-0.6f, 0.02f, -3.2f);
                Vector3 endA = new Vector3(-0.6f, 0.02f, -6.2f);

                yield return RecordGait(cat, cam, lightGo.transform, "walk", startA, endA, 0.34f, 40);
                yield return RecordGait(cat, cam, lightGo.transform, "run", startA, endA, 0.90f, 24);
                yield return RecordGait(cat, cam, lightGo.transform, "walk_front", startA, endA, 0.34f, 40);
                yield return RecordEat(cat, cam, lightGo.transform);

                RenderSettings.ambientLight = oldAmb;
                RenderSettings.ambientIntensity = oldInt;
                Destroy(camGo);
                Destroy(lightGo);
                Finish();
            }

            static int _shot;            IEnumerator RecordGait(CatAgent cat, Camera cam, Transform lightT, string tag,
                                   Vector3 start, Vector3 end, float speed, int frames)
            {
                cat.ScriptedStop();
                cat.PlaceAt(start, Quaternion.LookRotation((end - start).normalized, Vector3.up));
                cat.SnapToFloor();
                yield return null;
                yield return null;

                bool front = tag.Contains("front");
                float dist = front ? 1.05f : 0.95f;

                var sb = new StringBuilder();
                sb.AppendLine("f\tt\tstate\tnormalized\tposX\tposZ\tspeed\t"
                              + "toeLx\ttoeLy\ttoeLz\ttoeRx\ttoeRy\ttoeRz\tfootLy\tfootRy\thipsY\theadY");

                bool ok = cat.ScriptedMove(end, speed);
                yield return null;
                yield return null;
                L(tag + " scriptedMove=" + ok + " state=" + cat.DebugState + " pos=" + cat.transform.position.ToString("F2"));

                var anim = cat.GetComponent<Animator>();
                var bones = cat.GetComponentsInChildren<Transform>(true);
                Transform toeL = null, toeR = null, footL = null, footR = null, hips = null, head = null;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].name == "toe.L") toeL = bones[i];
                    else if (bones[i].name == "toe.R") toeR = bones[i];
                    else if (bones[i].name == "foot.L") footL = bones[i];
                    else if (bones[i].name == "foot.R") footR = bones[i];
                    else if (bones[i].name == "hips") hips = bones[i];
                    else if (bones[i].name == "head") head = bones[i];
                }

                float t0 = Time.time;
                for (int f = 0; f < frames; f++)
                {
                    // 相机跟拍：侧面看步幅，正面看左右腿是否分开
                    Vector3 fwd = cat.transform.forward; fwd.y = 0f; fwd.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    Vector3 focus = cat.transform.position + Vector3.up * 0.16f;
                    Vector3 camPos = front
                        ? focus + fwd * dist + Vector3.up * 0.14f
                        : focus + right * dist + Vector3.up * 0.08f;
                    cam.transform.position = camPos;
                    cam.transform.LookAt(focus);
                    lightT.position = cat.transform.position + new Vector3(0.5f, 1.2f, 0.5f);

                    // 渲染到 RT，避免 ScreenCapture 抓帧时序不确定
                    var rt = RenderTexture.GetTemporary(640, 640, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(640, 640, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(Path.Combine(DirPath, tag + "_" + f.ToString("00") + ".png"), tex.EncodeToPNG());
                    Object.Destroy(tex);

                    var st = anim.GetCurrentAnimatorStateInfo(0);
                    sb.AppendLine(string.Format("{0}\t{1:F3}\t{2}\t{3:F2}\t{4:F3}\t{5:F3}\t{6:F3}\t{7:F3}\t{8:F3}\t{9:F3}\t{10:F3}\t{11:F3}\t{12:F3}\t{13:F3}\t{14:F3}\t{15:F3}\t{16:F3}",
                        f, Time.time - t0, StateName(anim, st), st.normalizedTime,
                        cat.transform.position.x, cat.transform.position.z, cat.CurrentSpeed,
                        toeL ? toeL.position.x : 0f, toeL ? toeL.position.y : 0f, toeL ? toeL.position.z : 0f,
                        toeR ? toeR.position.x : 0f, toeR ? toeR.position.y : 0f, toeR ? toeR.position.z : 0f,
                        footL ? footL.position.y : 0f, footR ? footR.position.y : 0f,
                        hips ? hips.position.y : 0f, head ? head.position.y : 0f));
                    _shot++;
                    if (f == 3 || f == 12) L(tag + " f" + f + " " + cat.DebugState);
                    yield return new WaitForSeconds(0.05f);
                }
                File.WriteAllText(Path.Combine(DirPath, tag + ".tsv"), sb.ToString());
                L("recorded " + tag + " frames=" + frames);
            }

            IEnumerator RecordEat(CatAgent cat, Camera cam, Transform lightT)
            {
                var bowl = FindObjectOfType<NOCTURNE.Feeding.CatFoodBowl>();
                if (bowl == null) { L("FAIL no bowl"); yield break; }

                bowl.ResetBowl();
                bowl.Fill();
                yield return new WaitForSeconds(2.2f);

                Vector3 eatPos = bowl.eatingPoint != null ? bowl.eatingPoint.position : bowl.transform.position;
                cat.PlaceAt(eatPos, Quaternion.LookRotation(
                    (bowl.transform.position - eatPos).normalized, Vector3.up));
                cat.FacePoint(bowl.transform.position);
                yield return null;
                cat.SetMoodImmediate(CatMood.Eat);

                var look = cat.GetComponent<CatLookAt>();
                if (look != null)
                {
                    look.target = bowl.foodVisualRoot != null ? bowl.foodVisualRoot : bowl.transform;
                    look.enableLook = true;
                }

                yield return new WaitForSeconds(1.2f);

                Vector3 food = bowl.foodVisualRoot != null ? bowl.foodVisualRoot.position : bowl.transform.position;
                Vector3 nose = cat.HeadBone != null ? cat.HeadBone.position : cat.transform.position;
                L("eat pos=" + eatPos.ToString("F3") + " bowl=" + bowl.transform.position.ToString("F3")
                  + " food=" + food.ToString("F3") + " headBone=" + nose.ToString("F3")
                  + " headToFood=" + Vector3.Distance(nose, food).ToString("F3"));

                Vector3 fwd = cat.transform.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 focus = food + Vector3.up * 0.04f;

                Vector3[] cams = new Vector3[]
                {
                    focus + right * 0.55f + Vector3.up * 0.12f - fwd * 0.10f,
                    focus + (fwd + right).normalized * 0.55f + Vector3.up * 0.18f,
                    focus + Vector3.up * 0.55f - fwd * 0.10f
                };
                for (int i = 0; i < cams.Length; i++)
                {
                    cam.transform.position = cams[i];
                    cam.transform.LookAt(focus);
                    lightT.position = focus + new Vector3(0.45f, 0.75f, 0.45f);
                    var rt = RenderTexture.GetTemporary(720, 720, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(720, 720, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(Path.Combine(DirPath, "eat_" + i.ToString("00") + ".png"), tex.EncodeToPNG());
                    Object.Destroy(tex);
                    yield return null;
                }
                L("recorded eat");
            }

            static string StateName(Animator a, AnimatorStateInfo st)
            {
                var clips = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.animationClips : null;
                if (clips != null)
                    for (int i = 0; i < clips.Length; i++)
                        if (clips[i] != null && Animator.StringToHash(clips[i].name) == st.shortNameHash) return clips[i].name;
                return st.shortNameHash.ToString();
            }

            void Finish()
            {
                File.WriteAllText(Path.Combine(DirPath, "run.log"), string.Join("\n", _log.ToArray()));
                try { File.Delete(MarkerPath); } catch { }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
    }
}
