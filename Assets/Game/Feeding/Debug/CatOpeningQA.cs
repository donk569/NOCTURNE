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
    /// 寮€鍦?璧峰簥/璺戝幓楗泦鐨勭湡瀹炶妭濂忛仴娴嬶紙涓嶅仛浠讳綍 timeScale 鍔犻€燂級銆?    ///
    /// 瑙﹀彂锛氬伐绋嬫牴鐩綍瀛樺湪 `.workbuddy/openingqa.on`銆?    /// 杈撳嚭锛歚.workbuddy/openingqa/opening.tsv`锛堢潄鐪煎悗 6s锛岄€愬抚璁板綍姣忔牴楠ㄩ鐨勫眬閮ㄦ鎷夎锛夈€?    ///       `run.tsv`锛堣窇鍘婚キ鐩嗛€愬抚锛氫綅缃?閫熷害/Animator 鐘舵€侊級銆乣report.txt`銆?    /// </summary>
    public static class CatOpeningQA
    {
        const string MarkerRelative = "../.workbuddy/openingqa.on";
        const string DirRelative = "../.workbuddy/openingqa";

        public static string MarkerPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, MarkerRelative)); } }
        public static string DirPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, DirRelative)); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!File.Exists(MarkerPath)) return;
            var go = new GameObject("CatOpeningQARunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        public sealed class Runner : MonoBehaviour
        {
            static readonly StringBuilder _log = new StringBuilder();
            static void L(string s) { _log.AppendLine(s); Debug.Log("[OPENQA] " + s); }

            IEnumerator Start()
            {
                if (Directory.Exists(DirPath)) Directory.Delete(DirPath, true);
                Directory.CreateDirectory(DirPath);

                var seq = UnityEngine.Object.FindObjectOfType<OpeningSequenceController>();
                var lying = UnityEngine.Object.FindObjectOfType<PlayerLyingController>();
                var cat = UnityEngine.Object.FindObjectOfType<CatAgent>();
                var bowl = UnityEngine.Object.FindObjectOfType<CatFoodBowl>();
                if (seq == null || cat == null) { L("FAIL refs"); Finish(); yield break; }

                // ---- 1) 绛夌潄鐪硷紙姝ｅ父閫熷害锛屼笉鍔犻€燂級
                float w = 0f;
                while (!seq.EyeOpened && w < 120f) { w += Time.deltaTime; yield return null; }
                L("eye opened after " + w.ToString("F1") + "s, timeScale=" + Time.timeScale);

                var anim = cat.GetComponent<Animator>();
                var sk = cat.GetComponentInChildren<SkinnedMeshRenderer>(true);
                string[] bones = { "root", "hips", "spine", "spine2", "chest", "neck", "head", "jaw", "tail.01" };
                var bt = new Transform[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    var all = cat.GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < all.Length; j++) if (all[j].name == bones[i]) { bt[i] = all[j]; break; }
                }

                var sb = new StringBuilder();
                sb.Append("t\tstate\tnormalized\tanimSpeed\tcatX\tcatY\tcatZ");
                for (int i = 0; i < bones.Length; i++) sb.Append("\t" + bones[i] + ".x\t" + bones[i] + ".y\t" + bones[i] + ".z");
                sb.AppendLine();

                var prev = new Vector3[bones.Length];
                var prevWorld = new Vector3[bones.Length];
                var maxStep = new float[bones.Length];
                var span = new float[bones.Length];
                var lo = new Vector3[bones.Length];
                var hi = new Vector3[bones.Length];
                var first = new bool[bones.Length];
                var catLo = cat.transform.position;
                var catHi = cat.transform.position;
                for (int i = 0; i < bones.Length; i++) first[i] = true;

                float t = 0f;
                int lines = 0;
                var st = new AnimatorStateInfo();
                while (t < 6f)
                {
                    t += Time.deltaTime;
                    st = anim.GetCurrentAnimatorStateInfo(0);
                    sb.Append(string.Format("{0:F3}\t{1}\t{2:F3}\t{3:F2}\t{4:F3}\t{5:F3}\t{6:F3}",
                        t, st.shortNameHash, st.normalizedTime, anim.speed,
                        cat.transform.position.x, cat.transform.position.y, cat.transform.position.z));

                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bt[i] == null) { sb.Append("\t0\t0\t0"); continue; }
                        Vector3 e = bt[i].localEulerAngles;
                        sb.Append(string.Format("\t{0:F2}\t{1:F2}\t{2:F2}", e.x, e.y, e.z));
                        Vector3 w2 = bt[i].position;
                        if (!first[i]) { maxStep[i] = Mathf.Max(maxStep[i], Vector3.Distance(w2, prevWorld[i])); lo[i] = Vector3.Min(lo[i], w2); hi[i] = Vector3.Max(hi[i], w2); }
                        else { first[i] = false; lo[i] = w2; hi[i] = w2; }
                        prevWorld[i] = w2;
                    }
                    sb.AppendLine();
                    lines++;
                    catLo = Vector3.Min(catLo, cat.transform.position);
                    catHi = Vector3.Max(catHi, cat.transform.position);
                    yield return null;
                }
                File.WriteAllText(Path.Combine(DirPath, "opening.tsv"), sb.ToString());
                L("opening samples=" + lines);
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bt[i] == null) { L("  " + bones[i] + " MISSING"); continue; }
                    L(string.Format("  {0,-8} 涓栫晫浣嶇Щ宄板嘲 {1:F4}m 鍗曞抚鏈€澶?{2:F4}m", bones[i], (hi[i] - lo[i]).magnitude, maxStep[i]));
                }
                L(string.Format("  cat root 涓栫晫浣嶇Щ宄板嘲 {0:F4}m", (catHi - catLo).magnitude));

                // ---- 1b) 鐫佺溂鍚?5s锛堟贰鍏ョ粨鏉熴€佺尗浠嶅湪搴婁笂锛夐€愬抚鎶撲富鐩告満鐢婚潰锛岀敤鏉ュ仛鍍忕礌宸垎鎵?鎶芥悙"
                yield return new WaitForSeconds(5f);
                var qaCamGo = new GameObject("QA_OpenCam");
                var qaCam = qaCamGo.AddComponent<Camera>();
                qaCam.fieldOfView = 40f;
                qaCam.nearClipPlane = 0.02f;
                qaCam.farClipPlane = 40f;
                qaCam.enabled = false;
                var qaLightGo = new GameObject("QA_OpenLight");
                var qaLight = qaLightGo.AddComponent<Light>();
                qaLight.type = LightType.Point;
                qaLight.range = 4f;
                qaLight.intensity = 6f;
                Vector3 qaFocus = cat.transform.position + Vector3.up * 0.20f;
                qaCam.transform.position = qaFocus + new Vector3(0.55f, 0.12f, 0.55f);
                qaCam.transform.LookAt(qaFocus);
                qaLightGo.transform.position = qaFocus + new Vector3(0.5f, 0.8f, 0.5f);

                for (int f = 0; f < 12; f++)
                {
                    var crt2 = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
                    qaCam.targetTexture = crt2;
                    qaCam.Render();
                    RenderTexture.active = crt2;
                    var tex2 = new Texture2D(512, 512, TextureFormat.RGB24, false);
                    tex2.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                    tex2.Apply();
                    RenderTexture.active = null;
                    qaCam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(crt2);
                    File.WriteAllBytes(Path.Combine(DirPath, "tw_" + f.ToString("00") + ".png"), tex2.EncodeToPNG());
                    Object.Destroy(tex2);
                    var stq = anim.GetCurrentAnimatorStateInfo(0);
                    L(string.Format("tw{0,2} head={1} neckRot={2} state={3} norm={4:F3}",
                        f, bt[6] != null ? bt[6].position.ToString("F4") : "-",
                        bt[5] != null ? bt[5].localEulerAngles.ToString("F1") : "-",
                        stq.shortNameHash, stq.normalizedTime));
                    yield return new WaitForSeconds(0.10f);
                }
                Object.Destroy(qaCamGo);
                Object.Destroy(qaLightGo);

                // ---- 2) 璧峰簥 鈫?绛夊簥杈?鈫?鍠傞 鈫?璁板綍璺戝幓楗泦
                float cg = 0f;
                while (cg < 30f && (lying == null || !lying.CanGetUp)) { cg += Time.deltaTime; yield return null; }
                L("CanGetUp after " + cg.ToString("F1") + "s");
                if (lying != null) lying.RequestGetUp();
                float g = 0f;
                while (g < 15f && (lying == null || lying.IsGettingUp || lying.IsLying)) { g += Time.deltaTime; yield return null; }

                var feed = UnityEngine.Object.FindObjectOfType<CatFeedingSequenceController>();
                float wait = 0f;
                while (wait < 25f && (feed == null || feed.Phase != FeedingPhase.WaitingForFood)) { wait += Time.deltaTime; yield return null; }
                L("waiting for food after " + wait.ToString("F1") + "s, cat=" + cat.transform.position.ToString("F2"));

                yield return new WaitForSeconds(2f);
                bowl.Fill();

                var sb2 = new StringBuilder();
                sb2.AppendLine("t\tphase\tstate\tnormalized\tanimSpeed\tcatX\tcatY\tcatZ\tspeed\tstep\tfwdYaw");
                Vector3 last = cat.transform.position;
                float rt = 0f, maxStep2 = 0f, prevYaw = cat.transform.eulerAngles.y;
                float maxYawStep = 0f;
                var phases = new System.Collections.Generic.HashSet<string>();
                while (rt < 25f)
                {
                    rt += Time.deltaTime;
                    var p = cat.transform.position;
                    float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(last.x, 0, last.z));
                    st = anim.GetCurrentAnimatorStateInfo(0);
                    float yaw = cat.transform.eulerAngles.y;
                    float dyaw = Mathf.Abs(Mathf.DeltaAngle(prevYaw, yaw));
                    if (dyaw > maxYawStep) maxYawStep = dyaw;
                    prevYaw = yaw;
                    if (step > maxStep2) maxStep2 = step;
                    sb2.AppendLine(string.Format("{0:F3}\t{1}\t{2}\t{3:F2}\t{4:F2}\t{5:F3}\t{6:F3}\t{7:F3}\t{8:F3}\t{9:F4}\t{10:F1}",
                        rt, feed != null ? feed.Phase.ToString() : "-", st.shortNameHash, st.normalizedTime, anim.speed,
                        p.x, p.y, p.z, cat.CurrentSpeed, step, yaw));
                    last = p;
                    if (feed != null) phases.Add(feed.Phase.ToString());
                    if (feed != null && feed.Phase == FeedingPhase.Eating && rt > 6f) break;
                    yield return null;
                }
                File.WriteAllText(Path.Combine(DirPath, "run.tsv"), sb2.ToString());
                L(string.Format("run samples t={0:F1}s maxStep={1:F4}m maxYawStep={2:F1}掳 phases={3}",
                    rt, maxStep2, maxYawStep, string.Join(",", new System.Collections.Generic.List<string>(phases).ToArray())));

                Finish();
            }

            void Finish()
            {
                File.WriteAllText(Path.Combine(DirPath, "report.txt"), _log.ToString());
                try { File.Delete(MarkerPath); } catch { }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
    }
}

