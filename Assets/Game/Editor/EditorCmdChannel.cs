#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using NOCTURNE;
using NOCTURNE.Cat;
using NOCTURNE.Feeding;
using NOCTURNE.Opening;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 不依赖 MCP 的编辑器指令通道。
    ///
    /// 背景：这台机器上 MCP 插件在 Play 模式里会整段崩（8080 无监听），
    /// 没法从外部驱动编辑器。所以改成"文件即指令"：
    ///   外部往 .workbuddy/cmd/ 丢一个 *.cmd（首行是命令名，后面是 key=value），
    ///   编辑器每次域重载/每秒轮询时执行，结果写到同名 .out.txt，然后删掉指令文件。
    ///
    /// 用法（Python 侧）：
    ///   1) 写 .workbuddy/cmd/xxx.cmd
    ///   2) 把 Unity 窗口切前台触发 Auto Refresh（脚本有改动时才会重编译）
    ///   3) 轮询读 .workbuddy/cmd/xxx.out.txt
    ///
    /// 只做"检查/生成/改场景"，不做运行时逻辑。
    /// </summary>
    [InitializeOnLoad]
    public static class EditorCmdChannel
    {
        static string CmdDir
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../.workbuddy/cmd")); }
        }

        static double _nextPoll;

        static EditorCmdChannel()
        {
            EditorApplication.update += Tick;
            EditorApplication.delayCall += Poll;
            Debug.Log("[EditorCmd] 通道已加载，监听 " + CmdDir);
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void OnScriptsReloaded()
        {
            Poll();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;

            Poll();
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!Directory.Exists(CmdDir)) return;

            string[] files;
            try { files = Directory.GetFiles(CmdDir, "*.cmd"); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string name = Path.GetFileNameWithoutExtension(file);
                string[] lines = new string[0];
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                string result;
                try
                {
                    result = Run(name, lines);
                }
                catch (Exception e)
                {
                    result = "EXCEPTION\n" + e.ToString();
                }

                try
                {
                    File.WriteAllText(Path.Combine(CmdDir, name + ".out.txt"), result);
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[EditorCmd] 回写失败：" + e.Message);
                }

                Debug.Log("[EditorCmd] " + name + " 完成");
            }
        }

        // ------------------------------------------------------------------ 分发

        static string Run(string cmd, string[] args)
        {
            var a = ParseArgs(args);
            switch (cmd)
            {
                case "inspect_cat": return CmdInspectCat();
                case "shot_cat": return CmdShotCat(a);
                case "inspect_opening": return CmdInspectOpening();
                case "measure": return CmdMeasure(a);
                case "add_eyes": return CatLooksBuilder.AddEyes(a);
                case "add_bell": return CatLooksBuilder.AddBell(a);
                case "setup_opening": return CatLooksBuilder.SetupOpening(a);
                case "set_player_speed": return CatLooksBuilder.SetPlayerSpeed(a);
                case "save_scene": return CmdSaveScene();
                case "boost_walk": return CatLooksBuilder.BoostWalkStride(a);
                default:
                    return "未知命令：" + cmd + "\n可用：inspect_cat / inspect_opening / shot_cat / measure / add_eyes / add_bell / setup_opening / set_player_speed / save_scene";
            }
        }

        /// <summary>保存当前场景（改完物件必须落盘，否则下次打开就没了）。</summary>
        static string CmdSaveScene()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "save " + scene.name + " -> " + ok;
        }

        static Dictionary<string, string> ParseArgs(string[] lines)
        {
            var d = new Dictionary<string, string>();
            for (int i = 1; i < lines.Length; i++)
            {
                string l = lines[i];
                if (string.IsNullOrEmpty(l)) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                d[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
            }
            return d;
        }

        static float F(Dictionary<string, string> a, string k, float def)
        {
            string v;
            if (!a.TryGetValue(k, out v)) return def;
            float f;
            return float.TryParse(v, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out f) ? f : def;
        }

        static string S(Dictionary<string, string> a, string k, string def)
        {
            string v;
            return a.TryGetValue(k, out v) ? v : def;
        }

        // ------------------------------------------------------------------ 检查：猫咪

        static string CmdInspectCat()
        {
            var sb = new StringBuilder();
            var agent = UnityEngine.Object.FindObjectOfType<CatAgent>();
            if (agent == null) return "找不到 CatAgent";

            Transform cat = agent.transform;
            sb.AppendLine("=== 猫咪 ===");
            sb.AppendLine("go=" + cat.name + " pos=" + V(cat.position) + " euler=" + V(cat.eulerAngles)
                          + " lossyScale=" + V(cat.lossyScale));
            sb.AppendLine("父级=" + (cat.parent == null ? "-" : cat.parent.name));

            var anim = agent.GetComponent<Animator>();
            if (anim != null)
            {
                sb.AppendLine("Animator: controller=" + (anim.runtimeAnimatorController == null ? "-" : anim.runtimeAnimatorController.name)
                              + " applyRootMotion=" + anim.applyRootMotion + " speed=" + anim.speed
                              + " culling=" + anim.cullingMode);
                var st = anim.GetCurrentAnimatorStateInfo(0);
                sb.AppendLine("  当前状态 hash=" + st.shortNameHash + " normalizedTime=" + st.normalizedTime.ToString("F2")
                              + " length=" + st.length.ToString("F2") + " speed=" + st.speed.ToString("F2"));
            }

            var smr = cat.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null)
            {
                sb.AppendLine("SkinnedMesh: " + (smr.sharedMesh == null ? "-" : smr.sharedMesh.name)
                              + " verts=" + (smr.sharedMesh == null ? 0 : smr.sharedMesh.vertexCount)
                              + " boundsCenter=" + V(smr.bounds.center) + " boundsSize=" + V(smr.bounds.size));
                sb.AppendLine("  根骨=" + (smr.rootBone == null ? "-" : smr.rootBone.name)
                              + " 骨骼数=" + (smr.bones == null ? 0 : smr.bones.Length));
                sb.AppendLine("  材质数=" + smr.sharedMaterials.Length);
                for (int i = 0; i < smr.sharedMaterials.Length; i++)
                {
                    var m = smr.sharedMaterials[i];
                    if (m == null) { sb.AppendLine("   [" + i + "] null"); continue; }
                    sb.AppendLine("   [" + i + "] " + m.name + " shader=" + m.shader.name);
                    sb.AppendLine("        _BaseColor=" + Col(m, "_BaseColor") + " _Color=" + Col(m, "_Color"));
                    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    if (tex == null && m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
                    sb.AppendLine("        albedo=" + (tex == null ? "-" : tex.name)
                                  + " smooth=" + P(m, "_Smoothness") + " metal=" + P(m, "_Metallic"));
                }
            }

            sb.AppendLine("=== 骨骼（只列头/颈/尾/腿） ===");
            var bones = cat.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = bones[i];
                string n = b.name;
                if (n.IndexOf("tail", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("head", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("neck", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("thigh", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("shin", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("foot", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("toe", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("root", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("hips", StringComparison.OrdinalIgnoreCase) < 0) continue;

                sb.AppendLine(string.Format("  {0,-12} world={1} localRot={2} lossyScale={3}",
                    n, V(b.position), V(b.localEulerAngles), V(b.lossyScale)));
            }

            sb.AppendLine("=== 碰撞体 ===");
            var cols = cat.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                sb.AppendLine(string.Format("  {0} type={1} enabled={2} trigger={3} center={4} bounds={5}",
                    c.name, c.GetType().Name, c.enabled, c.isTrigger,
                    V(c.bounds.center), V(c.bounds.size)));
            }

            sb.AppendLine("=== 其它脚本 ===");
            var mbs = cat.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < mbs.Length; i++)
                if (mbs[i] != null) sb.AppendLine("  " + mbs[i].GetType().Name + " enabled=" + mbs[i].enabled);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ 检查：开场

        static string CmdInspectOpening()
        {
            var sb = new StringBuilder();
            var oc = UnityEngine.Object.FindObjectOfType<CatOpeningController>();
            if (oc == null) return "找不到 CatOpeningController";

            sb.AppendLine("=== CatOpeningController ===");
            sb.AppendLine("cat=" + (oc.cat == null ? "-" : oc.cat.name + " " + V(oc.cat.position)));
            sb.AppendLine("bedOpeningPosition=" + (oc.bedOpeningPosition == null ? "-" :
                oc.bedOpeningPosition.name + " pos=" + V(oc.bedOpeningPosition.position) +
                " euler=" + V(oc.bedOpeningPosition.eulerAngles)));
            sb.AppendLine("floorExitPosition=" + (oc.floorExitPosition == null ? "-" :
                oc.floorExitPosition.name + " pos=" + V(oc.floorExitPosition.position)));
            sb.AppendLine("openingClipName=" + oc.openingClipName + " settleOnSurface=" + oc.settleOnSurface
                          + " settleMaxShift=" + oc.settleMaxShift);
            sb.AppendLine("lookAt=" + (oc.lookAt == null ? "-" : oc.lookAt.GetType().Name));

            var lying = UnityEngine.Object.FindObjectOfType<PlayerLyingController>();
            if (lying != null)
            {
                sb.AppendLine("=== 玩家躺卧 ===");
                sb.AppendLine("player=" + V(lying.transform.position) + " euler=" + V(lying.transform.eulerAngles));
                var fpc = UnityEngine.Object.FindObjectOfType<FirstPersonController>();
                if (fpc != null)
                {
                    sb.AppendLine("  cameraRig local=" + V(fpc.cameraRig.localPosition)
                                  + " world=" + V(fpc.cameraRig.position)
                                  + " walkSpeed=" + fpc.walkSpeed + " sprintSpeed=" + fpc.sprintSpeed
                                  + " crouchSpeed=" + fpc.crouchSpeed);
                }
            }

            // 从床上方往下打射线，看究竟是哪个碰撞体被当成"床面"
            if (oc.bedOpeningPosition != null)
            {
                Vector3 p = oc.bedOpeningPosition.position;
                Vector3 origin = new Vector3(p.x, p.y + 1.2f, p.z);
                sb.AppendLine("=== 床面射线（床上锚点正上方 1.2m 向下） ===");
                RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 3f, ~0, QueryTriggerInteraction.Collide);
                Array.Sort(hits, delegate (RaycastHit x, RaycastHit y) { return y.point.y.CompareTo(x.point.y); });
                for (int i = 0; i < hits.Length; i++)
                    sb.AppendLine(string.Format("  y={0:F4} {1} (layer {2}, trigger={3})",
                        hits[i].point.y, hits[i].collider.name, LayerMask.LayerToName(hits[i].collider.gameObject.layer),
                        hits[i].collider.isTrigger));

                // 被子/床的 Renderer 包围盒顶面
                sb.AppendLine("=== 床附近 Renderer 顶面 ===");
                var rends = UnityEngine.Object.FindObjectsOfType<Renderer>();
                for (int i = 0; i < rends.Length; i++)
                {
                    Bounds b = rends[i].bounds;
                    if (Vector3.Distance(new Vector3(b.center.x, 0f, b.center.z), new Vector3(p.x, 0f, p.z)) > 1.6f) continue;
                    if (b.max.y > p.y + 0.6f) continue;
                    sb.AppendLine(string.Format("  {0} top={1:F4} center={2} size={3}",
                        rends[i].name, b.max.y, V(b.center), V(b.size)));
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ 拍照

        /// <summary>临时相机 + 补光渲染一张猫的图，用来肉眼验收模型/姿态（写完自动清理）。</summary>
        static string CmdShotCat(Dictionary<string, string> a)
        {
            var agent = UnityEngine.Object.FindObjectOfType<CatAgent>();
            if (agent == null) return "找不到 CatAgent";

            Transform cat = agent.transform;
            string outName = S(a, "file", "cat_studio");

            // 相机默认放在猫正前方偏右、略高于头
            Vector3 target = cat.position + Vector3.up * F(a, "ty", 0.12f);
            float yaw = F(a, "yaw", 0f);
            float dist = F(a, "dist", 0.9f);
            float height = F(a, "h", 0.22f);
            Vector3 dir = Quaternion.Euler(0f, cat.eulerAngles.y + yaw, 0f) * Vector3.forward;
            Vector3 camPos = target + dir * dist + Vector3.up * height;

            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = F(a, "fov", 40f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
            go.transform.position = camPos;
            go.transform.LookAt(target + Vector3.up * F(a, "aimY", 0f));

            var lightGo = new GameObject("~ShotLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = F(a, "light", 1.5f);
            light.color = new Color(1f, 0.97f, 0.92f);
            lightGo.transform.rotation = Quaternion.Euler(35f, cat.eulerAngles.y + 150f, 0f);

            var fillGo = new GameObject("~ShotFill");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.6f;
            fill.color = new Color(0.75f, 0.82f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(15f, cat.eulerAngles.y - 60f, 0f);

            int size = (int)F(a, "size", 640f);
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../.workbuddy/" + outName + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());

            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(lightGo);
            UnityEngine.Object.DestroyImmediate(fillGo);

            return "saved " + path + "\ncat=" + V(cat.position) + " euler=" + V(cat.eulerAngles)
                   + "\ncam=" + V(camPos) + " -> " + V(target);
        }

        // ------------------------------------------------------------------ 量尺寸

        static string CmdMeasure(Dictionary<string, string> a)
        {
            string go = S(a, "go", "");
            if (go.Length == 0) return "需要 go=对象名";

            var t = GameObject.Find(go);
            if (t == null) return "找不到 " + go;

            var sb = new StringBuilder();
            sb.AppendLine(go + " pos=" + V(t.transform.position) + " euler=" + V(t.transform.eulerAngles));
            var rends = t.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
                sb.AppendLine("  render " + rends[i].name + " bounds=" + V(rends[i].bounds.center) + " size=" + V(rends[i].bounds.size));
            var cols = t.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                sb.AppendLine("  collider " + cols[i].name + " center=" + V(cols[i].bounds.center) + " size=" + V(cols[i].bounds.size));

            // 逐高度向下射线，找真实地面
            Vector3 p = t.transform.position;
            sb.AppendLine("  向下射线：");
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, p.y + 1f, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, delegate (RaycastHit x, RaycastHit y) { return y.point.y.CompareTo(x.point.y); });
            for (int i = 0; i < hits.Length; i++)
                sb.AppendLine(string.Format("    y={0:F4} {1}", hits[i].point.y, hits[i].collider.name));

            return sb.ToString();
        }

        // ------------------------------------------------------------------ 小工具

        static string V(Vector3 v)
        {
            return string.Format("({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
        }

        static string Col(Material m, string p)
        {
            if (!m.HasProperty(p)) return "-";
            Color c = m.GetColor(p);
            return string.Format("({0:F2},{1:F2},{2:F2},{3:F2})", c.r, c.g, c.b, c.a);
        }

        static string P(Material m, string p)
        {
            if (!m.HasProperty(p)) return "-";
            return m.GetFloat(p).ToString("F2");
        }
    }
}
#endif
