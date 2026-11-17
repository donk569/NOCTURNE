#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using NOCTURNE;
using NOCTURNE.Cat;
using NOCTURNE.Opening;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 猫咪外观件（眼睛、项圈、铃铛）的程序化建模与接线。
    ///
    /// 为什么要程序化建：项目里没有现成的眼珠/铃铛模型资产，
    /// 而"只建空物体"是不允许的（需求明确写了），所以这里真的生成网格与材质，
    /// 并挂到 FBX 里的 head / neck 骨骼下（骨骼带 100 倍缩放，全部按世界尺寸换算）。
    ///
    /// 入口：菜单 NOCTURNE/猫咪/重建外观件，或编辑器指令通道 add_eyes / add_bell。
    /// </summary>
    public static class CatLooksBuilder
    {
        const string MeshDir = EditorPaths.CatMeshDir;
        const string MatDir = EditorPaths.CatMaterialDir;
        const string AudioDir = EditorPaths.CatAudioDir;

        // ------------------------------------------------------------------ 命令入口

        /// <summary>重建眼睛。参数是相对 head 骨骼的世界偏移。</summary>
        public static string AddEyes(Dictionary<string, string> args)
        {
            var agent = UnityEngine.Object.FindObjectOfType<CatAgent>();
            if (agent == null) return "找不到 CatAgent";
            var head = FindDeep(agent.transform, "head");
            if (head == null) return "找不到 head 骨骼";

            float x = Arg(args, "x", 0.0355f);
            float y = Arg(args, "y", 0.0165f);
            float z = Arg(args, "z", 0.0560f);
            float s = Arg(args, "s", 1f);
            float yaw = Arg(args, "yaw", 0f);
            float pitch = Arg(args, "pitch", 0f);

            EnsureFolders();
            var scleraMat = MakeLit(MatDir + "/PR_CatEyeSclera.mat", new Color(0.86f, 0.85f, 0.82f), 0.42f, 0f);
            var irisMat = MakeLit(MatDir + "/PR_CatEyeIris.mat", new Color(0.72f, 0.52f, 0.16f), 0.68f, 0f);
            var pupilMat = MakeLit(MatDir + "/PR_CatEyePupil.mat", new Color(0.03f, 0.03f, 0.035f), 0.55f, 0f);
            var glintMat = MakeLit(MatDir + "/PR_CatEyeGlint.mat", new Color(1f, 1f, 1f), 0.9f, 0f);

            // 清掉历史版本：早期把眼睛挂在猫根节点下，会残留一套在原地
            var stray = agent.transform.Find("CatEyes");
            if (stray != null) UnityEngine.Object.DestroyImmediate(stray.gameObject);

            var root = EnsureChild(head, "CatEyes");   // 必须挂在 head 骨骼下，否则头一转眼睛就留在原地
            ClearChildren(root.transform);

            // 眼睛中心（世界）＝ head 骨骼位置 + 世界偏移。
            // 注意这里不能用 head.rotation 去转偏移：这个骨架的 head 骨骼自身是歪的
            // （导出时绕 X 转了 26°，父级 root 又转了 180°），
            // 按世界方向摆最直观，挂到 head 下之后自然跟着头转。
            Vector3 basePos = head.position + new Vector3(0f, y, z);
            Vector3 side = agent.transform.right;
            Quaternion faceRot = agent.transform.rotation * Quaternion.Euler(pitch, yaw, 0f);

            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? -1f : 1f;
                Vector3 eyePos = basePos + side * (x * sign);
                string sideName = i == 0 ? "EyeL" : "EyeR";

                var eye = new GameObject(sideName);
                eye.transform.SetParent(root.transform, false);
                eye.transform.position = eyePos;
                eye.transform.rotation = faceRot;

                // 巩膜：压扁的球，嵌在眼皮里只露出一层弧面
                AddPart(eye.transform, "Sclera", scleraMat,
                        SphereMesh("M_CatEyeSclera", 20, 14),
                        Vector3.zero, new Vector3(0.0115f, 0.0092f, 0.0052f) * s);

                AddPart(eye.transform, "Iris", irisMat,
                        SphereMesh("M_CatEyeIris", 18, 12),
                        new Vector3(0f, 0f, 0.0034f) * s, new Vector3(0.0069f, 0.0071f, 0.0024f) * s);

                // 竖瞳：猫是竖着的椭圆瞳孔
                AddPart(eye.transform, "Pupil", pupilMat,
                        SphereMesh("M_CatEyePupil", 14, 10),
                        new Vector3(0f, 0f, 0.0046f) * s, new Vector3(0.0026f, 0.0044f, 0.0014f) * s);

                // 高光点：偏左上，让眼睛"有神"
                AddPart(eye.transform, "Glint", glintMat,
                        SphereMesh("M_CatEyeGlint", 8, 6),
                        new Vector3(-0.0022f, 0.0028f, 0.0052f) * s, new Vector3(0.0014f, 0.0014f, 0.0009f) * s);
            }

            EditorUtility.SetDirty(agent.gameObject);
            return string.Format("眼睛已重建：x={0:F4} y={1:F4} z={2:F4} 缩放={3:F2}\nhead={4} 眼心={5}",
                x, y, z, s, V(head.position), V(basePos));
        }

        /// <summary>重建项圈 + 铃铛，并挂 CatBell / CatBodyMotion。</summary>
        public static string AddBell(Dictionary<string, string> args)
        {
            var agent = UnityEngine.Object.FindObjectOfType<CatAgent>();
            if (agent == null) return "找不到 CatAgent";
            var neck = FindDeep(agent.transform, "neck");
            if (neck == null) return "找不到 neck 骨骼";

            float radius = Arg(args, "radius", 0.058f);
            float bellR = Arg(args, "bell", 0.0105f);
            float drop = Arg(args, "drop", 0.045f);

            EnsureFolders();

            var collarMat = MakeLit(MatDir + "/PR_CatCollar.mat", new Color(0.115f, 0.098f, 0.092f), 0.28f, 0f);
            var brassMat = MakeLit(MatDir + "/PR_CatBellBrass.mat", new Color(0.62f, 0.505f, 0.245f), 0.58f, 0.85f);

            var root = EnsureChild(agent.transform, "CatCollar");
            ClearChildren(root.transform);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // ---- 量脖子：在指定高度上，蒙皮网格实际有多粗？
            // 直接写一个"看起来差不多"的半径是不行的——这个模型脖子和胸连成一片，
            // 半径小了整圈项圈就埋进毛里看不见，大了像个呼啦圈。
            // 项圈挂在脖子根：这模型的脖子和胸连成一片，也没有圆形截面，
            // 所以不能用"一个圆环"去套——那样必然一边埋进毛里、一边翘出来像翅膀。
            // 做法：直接量这一圈的身体轮廓，沿轮廓生成一条带子（截面是圆的细管）。
            float ringY = neck.position.y + Arg(args, "up", -0.010f);
            float tube = Arg(args, "tube", 0.0042f);
            float inflate = Arg(args, "inflate", 0.0035f);

            Vector3[] outline;
            if (!SampleBodyOutline(agent, ringY, neck.position, 36, out outline))
                return "采样不到身体轮廓，无法生成项圈";

            // 肩部比脖子宽得多，直接照轮廓走会架成一个圆环；
            // 把半径收敛到"最外点 × 0.78"，两侧就会埋进毛里，只露出脖子正面那一段。
            float maxR = Arg(args, "maxr", 0f);
            if (maxR <= 0f)
            {
                float outer = 0f;
                for (int i = 0; i < outline.Length; i++)
                {
                    float d = new Vector2(outline[i].x - neck.position.x, outline[i].z - neck.position.z).magnitude;
                    if (d > outer) outer = d;
                }
                maxR = outer * 0.70f;
            }
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 rel = outline[i] - neck.position;
                Vector2 flat = new Vector2(rel.x, rel.z);
                float d = flat.magnitude;
                if (d > maxR && d > 1e-4f)
                {
                    flat *= maxR / d;
                    outline[i] = new Vector3(neck.position.x + flat.x, ringY, neck.position.z + flat.y);
                }
            }

            radius = 0f;
            for (int i = 0; i < outline.Length; i++)
            {
                float d = new Vector2(outline[i].x - neck.position.x, outline[i].z - neck.position.z).magnitude;
                if (d > radius) radius = d;
            }
            radius += inflate;

            var collarMesh = SaveMesh(CollarTube("M_CatCollar", outline, inflate, tube, neck.position, ringY, agent.transform));

            var ring = new GameObject("CollarBand");
            ring.transform.SetParent(root.transform, false);
            ring.transform.position = Vector3.zero;
            ring.transform.rotation = Quaternion.identity;
            var mf = ring.AddComponent<MeshFilter>();
            mf.sharedMesh = collarMesh;
            var mr = ring.AddComponent<MeshRenderer>();
            mr.sharedMaterial = collarMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // ---- 铃铛：挂在轮廓最靠前的那一点下方，稍微往外推，避免埋进胸毛
            var bellRoot = new GameObject("Bell");
            bellRoot.transform.SetParent(root.transform, false);

            Vector3 frontMost = outline[0];
            float best = -9999f;
            for (int i = 0; i < outline.Length; i++)
            {
                float f = Vector3.Dot(outline[i] - neck.position, agent.transform.forward);
                if (f > best) { best = f; frontMost = outline[i]; }
            }

            float bellDrop = Arg(args, "drop", 0.026f);
            float bellY = ringY - bellDrop;

            // 铃铛不要跟着项圈轮廓走（轮廓被收敛过，会把铃铛一起吸进胸毛里），
            // 单独从正前方打射线拿胸口表面，再往前挪半个铃铛
            // 射线打的是胶囊体，比可见的毛皮瘦一圈，所以这里量蒙皮顶点更准
            // 铃铛：在"猫的局部坐标"里量这个高度上最靠前的毛皮，再往前推一个铃铛半径。
            // 之前用世界坐标推，猫一有 yaw 铃铛就偏到胸口侧面（本项目踩过）。
            float localY = bellY - agent.transform.position.y;
            float localFrontZ = MeshFrontLocalZ(agent, localY);
            Vector3 localBell = new Vector3(0f, localY, localFrontZ + bellR * 1.25f);
            Vector3 bellPos = agent.transform.TransformPoint(localBell);

            bellRoot.transform.position = bellPos;

            var bellMesh = SaveMesh(SphereMesh("M_CatBell", 24, 16));
            var holder = new GameObject("BellBody");
            holder.transform.SetParent(bellRoot.transform, false);
            holder.transform.position = bellPos;
            holder.transform.rotation = agent.transform.rotation;
            var bmf = holder.AddComponent<MeshFilter>();
            bmf.sharedMesh = bellMesh;
            var bmr = holder.AddComponent<MeshRenderer>();
            bmr.sharedMaterial = brassMat;
            ScaleToWorld(holder, new Vector3(bellR * 2f, bellR * 1.95f, bellR * 2f));

            var loop = new GameObject("BellLoop");
            loop.transform.SetParent(bellRoot.transform, false);
            loop.transform.position = bellPos + Vector3.up * (bellR * 1.0f);
            loop.transform.rotation = agent.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            var loopMesh = SaveMesh(TorusMesh("M_CatBellLoop", bellR * 0.40f, bellR * 0.15f, 16, 8));
            var lmf = loop.AddComponent<MeshFilter>();
            lmf.sharedMesh = loopMesh;
            var lmr = loop.AddComponent<MeshRenderer>();
            lmr.sharedMaterial = brassMat;
            ScaleToWorld(loop, new Vector3(bellR * 2f, bellR * 2f, bellR * 2f));

            // ---- 音频：3D 音源 + 铃铛组件
            var src = agent.GetComponent<AudioSource>();
            if (src == null) src = agent.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 0.9f;
            src.maxDistance = 8f;
            src.dopplerLevel = 0.35f;

            var clip = EnsureBellClip();
            var bell = agent.GetComponent<CatBell>();
            if (bell == null) bell = agent.gameObject.AddComponent<CatBell>();
            bell.source = src;
            bell.clips = new AudioClip[] { clip };

            var motion = agent.GetComponent<CatBodyMotion>();
            if (motion == null) motion = agent.gameObject.AddComponent<CatBodyMotion>();

            EditorUtility.SetDirty(agent.gameObject);
            var ringRend = ring.GetComponent<Renderer>();
            var bellRend = holder.GetComponent<Renderer>();
            return string.Format("铃铛已建(v4)：项圈半径={0:F3} 铃铛半径={1:F4} 悬挂量={2:F3}\nneck={3}\n项圈位置={4} 渲染尺寸={5}\n铃铛位置={6} 渲染尺寸={7}\n音效={8}",
                radius, bellR, drop, V(neck.position), V(ring.transform.position),
                ringRend == null ? "无" : V(ringRend.bounds.size),
                V(bellRoot.transform.position),
                bellRend == null ? "无" : V(bellRend.bounds.size),
                clip == null ? "缺失" : clip.name);
        }

        /// <summary>把开场锚点摆到玩家脸边、换成坐姿凝视，并接好叫声。</summary>
        public static string SetupOpening(Dictionary<string, string> args)
        {
            var oc = UnityEngine.Object.FindObjectOfType<CatOpeningController>();
            if (oc == null) return "找不到 CatOpeningController";

            float x = Arg(args, "x", 0.13f);
            float y = Arg(args, "y", 0.712f);
            float z = Arg(args, "z", 1.56f);
            float yaw = Arg(args, "yaw", 0f);
            string clip = args.ContainsKey("clip") ? args["clip"] : "Cat_Sit";

            var anchor = oc.bedOpeningPosition;
            if (anchor == null) return "CatOpeningController.bedOpeningPosition 为空";

            anchor.position = new Vector3(x, y, z);
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            oc.openingClipName = clip;
            oc.applyOnStart = true;
            oc.settlePasses = 3;
            oc.sinkIntoSurface = 0.012f;

            oc.meowClips = new AudioClip[]
            {
                AssetDatabase.LoadAssetAtPath<AudioClip>(EditorPaths.SfxCatAttention),
                AssetDatabase.LoadAssetAtPath<AudioClip>(EditorPaths.SfxCatHungry),
                AssetDatabase.LoadAssetAtPath<AudioClip>(EditorPaths.SfxCatSweet)
            };
            int found = 0;
            for (int i = 0; i < oc.meowClips.Length; i++) if (oc.meowClips[i] != null) found++;

            EditorUtility.SetDirty(oc);
            EditorUtility.SetDirty(anchor.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(oc.gameObject.scene);

            return string.Format("开场锚点 → {0} / yaw={1:F0} / 剪辑={2} / 叫声 {3}/{4} 个可用",
                V(anchor.position), yaw, clip, found, oc.meowClips.Length);
        }

        /// <summary>玩家移动速度按比例缩放（写进场景里的序列化值，不改代码默认）。</summary>
        public static string SetPlayerSpeed(Dictionary<string, string> args)
        {
            var fpc = UnityEngine.Object.FindObjectOfType<FirstPersonController>();
            if (fpc == null) return "找不到 FirstPersonController";

            float factor = Arg(args, "factor", 0.667f);
            float oldWalk = fpc.walkSpeed, oldRun = fpc.sprintSpeed, oldCrouch = fpc.crouchSpeed;

            fpc.walkSpeed = oldWalk * factor;
            fpc.sprintSpeed = oldRun * factor;
            fpc.crouchSpeed = oldCrouch * factor;

            EditorUtility.SetDirty(fpc);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(fpc.gameObject.scene);

            return string.Format("玩家速度 ×{0:F3}：走 {1:F2}→{2:F2}，跑 {3:F2}→{4:F2}，蹲 {5:F2}→{6:F2}",
                factor, oldWalk, fpc.walkSpeed, oldRun, fpc.sprintSpeed, oldCrouch, fpc.crouchSpeed);
        }

        /// <summary>
        /// 放大走路剪辑里腿骨的摆动幅度（真正的"步子变大"）。
        ///
        /// 原理：Cat_Walk 的关键帧里，大腿/小腿是四元数旋转曲线。
        /// 以每条曲线的"平均姿势"为中心，把每个关键帧的角度偏差按 factor 放大，
        /// 步幅就变长；步频不变，但配合 CatAgent.walkClipNaturalSpeed 一起放大后，
        /// 角色为了走同样速度会自动降低播放倍率 → 迈步更大、频率更低，正是需求要的效果。
        ///
        /// 只处理 thigh / shin / foot / toe，不碰脊椎和头，避免走路变得晃。
        /// </summary>
        public static string BoostWalkStride(Dictionary<string, string> args)
        {
            float factor = Arg(args, "factor", 1.45f);
            string fbx = EditorPaths.CatModelFbx;
            string savePath = EditorPaths.CatWalkWideAnim;

            var assets = AssetDatabase.LoadAllAssetsAtPath(fbx);
            AnimationClip src = null;
            for (int i = 0; i < assets.Length; i++)
            {
                var clip = assets[i] as AnimationClip;
                if (clip != null && clip.name == "Cat_Walk") { src = clip; break; }
            }
            if (src == null) return "FBX 里找不到 Cat_Walk 剪辑";

            var dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(savePath);
            if (dst == null)
            {
                dst = new AnimationClip { name = "AC_Cat_WalkWide" };
                AssetDatabase.CreateAsset(dst, savePath);
            }
            dst.ClearCurves();

            var bindings = AnimationUtility.GetCurveBindings(src);
            var rotGroups = new Dictionary<string, AnimationCurve[]>();

            int touched = 0;
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding b = bindings[i];
                AnimationCurve c = AnimationUtility.GetEditorCurve(src, b);

                bool isLeg = b.path.IndexOf("thigh", StringComparison.OrdinalIgnoreCase) >= 0
                          || b.path.IndexOf("shin", StringComparison.OrdinalIgnoreCase) >= 0
                          || b.path.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0
                          || b.path.IndexOf("toe", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isLeg && b.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal))
                {
                    string key = b.path;
                    if (!rotGroups.ContainsKey(key)) rotGroups[key] = new AnimationCurve[4];
                    int idx = ComponentIndex(b.propertyName);
                    if (idx >= 0) rotGroups[key][idx] = c;
                    continue;
                }

                // 其它曲线原样复制
                dst.SetCurve(b.path, b.type, b.propertyName, c);
            }

            // 腿骨四元数：绕平均姿势放大角度偏差
            foreach (var kv in rotGroups)
            {
                AnimationCurve[] q = kv.Value;
                if (q[0] == null || q[1] == null || q[2] == null || q[3] == null) continue;

                int keys = q[0].length;
                if (keys < 2) continue;

                var qs = new Quaternion[keys];
                for (int k = 0; k < keys; k++)
                    qs[k] = new Quaternion(q[0].keys[k].value, q[1].keys[k].value, q[2].keys[k].value, q[3].keys[k].value).normalized;

                // 平均姿势（归一化平均），作为放大中心
                float sx = 0f, sy = 0f, sz = 0f, sw = 0f;
                for (int k = 0; k < keys; k++) { sx += qs[k].x; sy += qs[k].y; sz += qs[k].z; sw += qs[k].w; }
                var mean = new Quaternion(sx, sy, sz, sw).normalized;

                var outX = new Keyframe[keys];
                var outY = new Keyframe[keys];
                var outZ = new Keyframe[keys];
                var outW = new Keyframe[keys];
                for (int k = 0; k < keys; k++)
                {
                    Quaternion amplified = Quaternion.Slerp(mean, qs[k], factor);
                    var t = q[0].keys[k];
                    outX[k] = new Keyframe(t.time, amplified.x, t.inTangent, t.outTangent);
                    outY[k] = new Keyframe(t.time, amplified.y, t.inTangent, t.outTangent);
                    outZ[k] = new Keyframe(t.time, amplified.z, t.inTangent, t.outTangent);
                    outW[k] = new Keyframe(t.time, amplified.w, t.inTangent, t.outTangent);
                }

                dst.SetCurve(kv.Key, typeof(Transform), "m_LocalRotation.x", new AnimationCurve(outX));
                dst.SetCurve(kv.Key, typeof(Transform), "m_LocalRotation.y", new AnimationCurve(outY));
                dst.SetCurve(kv.Key, typeof(Transform), "m_LocalRotation.z", new AnimationCurve(outZ));
                dst.SetCurve(kv.Key, typeof(Transform), "m_LocalRotation.w", new AnimationCurve(outW));
                touched++;
            }

            var settings = AnimationUtility.GetAnimationClipSettings(src);
            AnimationUtility.SetAnimationClipSettings(dst, settings);
            dst.wrapMode = WrapMode.Loop;
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();

            // ---- 换到状态机上
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(EditorPaths.CatAnimatorController);
            string swapped = "控制器没换";
            if (ac != null)
            {
                var layers = ac.layers;
                bool done = false;
                for (int l = 0; l < layers.Length && !done; l++)
                {
                    var states = layers[l].stateMachine.states;
                    for (int i = 0; i < states.Length; i++)
                    {
                        if (states[i].state.name == "Cat_Walk")
                        {
                            states[i].state.motion = dst;
                            done = true;
                            break;
                        }
                    }
                }
                if (done)
                {
                    EditorUtility.SetDirty(ac);
                    AssetDatabase.SaveAssets();
                    swapped = "已替换 Cat_Walk 状态";
                }
            }

            // ---- 剪辑天然速度同步放大，否则角色会"腿动得比地面快"（滑步）
            var agent = UnityEngine.Object.FindObjectOfType<CatAgent>();
            float naturalOld = 0f, naturalNew = 0f;
            if (agent != null)
            {
                naturalOld = agent.walkClipNaturalSpeed;
                agent.walkClipNaturalSpeed = naturalOld * factor;
                naturalNew = agent.walkClipNaturalSpeed;
                EditorUtility.SetDirty(agent);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
            }

            return string.Format("走路步幅 ×{0:F2}：{1} 段腿骨曲线被放大，{2}\n剪辑长度 {3:F2}s（原 {4:F2}s）\n" +
                                 "walkClipNaturalSpeed {5:F3} → {6:F3}",
                                 factor, touched, swapped, dst.length, src.length, naturalOld, naturalNew);
        }

        static int ComponentIndex(string propertyName)
        {
            if (propertyName.EndsWith(".x")) return 0;
            if (propertyName.EndsWith(".y")) return 1;
            if (propertyName.EndsWith(".z")) return 2;
            if (propertyName.EndsWith(".w")) return 3;
            return -1;
        }

        // ------------------------------------------------------------------ 音频合成

        static AudioClip EnsureBellClip()
        {
            string path = AudioDir + "/cat_bell.wav";
            var exist = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (exist != null) return exist;

            Directory.CreateDirectory(AudioDir);
            const int rate = 44100;
            const float dur = 0.55f;
            int n = (int)(rate * dur);
            float[] buf = new float[n];

            // 金属铃：几个不成谐波关系的分音 + 指数衰减 + 起始的撞击噪声
            float[] partials = new float[] { 2350f, 3140f, 4180f, 5260f, 6890f };
            float[] gains = new float[] { 0.55f, 0.42f, 0.30f, 0.20f, 0.12f };
            float[] decays = new float[] { 3.4f, 4.6f, 6.0f, 7.5f, 9.5f };

            var rnd = new System.Random(20261112);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[p] * t) * gains[p] * Mathf.Exp(-decays[p] * t);

                // 撞击瞬间的短噪声（铃舌碰壳）
                if (t < 0.02f)
                    s += ((float)rnd.NextDouble() * 2f - 1f) * 0.5f * Mathf.Exp(-180f * t);

                // 尾巴收干净，避免爆音
                float fade = t > dur - 0.05f ? (dur - t) / 0.05f : 1f;
                buf[i] = Mathf.Clamp(s * 0.42f * fade, -1f, 1f);
            }

            WriteWav(path, buf, rate);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static void WriteWav(string path, float[] data, int rate)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                int samples = data.Length;
                int byteRate = rate * 2;
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + samples * 2);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(rate);
                bw.Write(byteRate);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                    bw.Write((short)Mathf.Clamp(Mathf.RoundToInt(data[i] * 32767f), -32768, 32767));
            }
        }

        // ------------------------------------------------------------------ 网格

        static Mesh SphereMesh(string name, int lon, int lat)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();

            for (int y = 0; y <= lat; y++)
            {
                float vv = (float)y / lat;
                float phi = vv * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float uu = (float)x / lon;
                    float theta = uu * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * 0.5f);
                    uv.Add(new Vector2(uu, 1f - vv));
                }
            }
            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x;
                    int b = a + lon + 1;
                    tri.Add(a); tri.Add(b); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                }
            }

            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh TorusMesh(string name, float major, float minor, int seg, int sides)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();

            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg;
                float a = u * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(a) * major, 0f, Mathf.Sin(a) * major);

                for (int j = 0; j <= sides; j++)
                {
                    float vv = (float)j / sides;
                    float b = vv * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b));
                    v.Add(center + dir * minor);
                    uv.Add(new Vector2(u, vv));
                }
            }
            for (int i = 0; i < seg; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j;
                    int b = a + sides + 1;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b);
                    tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
                }
            }

            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>
        /// 在给定世界高度上量身体半径：把蒙皮网格烘焙成顶点，取该高度附近的点，
        /// 算它们到"脖子中轴"的水平距离，取 92 分位（避开胡须那种飘出去的顶点）。
        /// </summary>
        static bool MeasureRingRadius(CatAgent agent, float y, Vector3 axis, out float radius, out float centerZ)
        {
            radius = 0f;
            centerZ = agent.transform.position.z;

            var smr = agent.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null) return false;

            var scratch = new Mesh();
            smr.BakeMesh(scratch);
            Vector3[] verts = scratch.vertices;
            // 注意：BakeMesh 的输出已经抵消了渲染节点自身的缩放，只能用位置+旋转
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            UnityEngine.Object.DestroyImmediate(scratch);

            // 只取"脖子所在的这一小段"：高度对齐 + 前后不出脖子范围。
            // 不限制 z 的话会量到整条躯干，半径直接变成体长的一半。
            var cand = new List<float>();
            float cx = axis.x;          // 中轴用脖子骨骼，不是猫根节点
            float cz = axis.z;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(verts[i]);
                if (Mathf.Abs(w.y - y) > 0.014f) continue;
                if (Mathf.Abs(w.z - cz) > 0.055f) continue;   // 只取脖子所在的这一小段
                float dx = w.x - cx;
                float dz = w.z - cz;
                cand.Add(Mathf.Sqrt(dx * dx + dz * dz));
            }
            if (cand.Count < 10) return false;

            cand.Sort();

            // 胡须会飘出去很远，先按中位数把明显离群的顶点剔掉，再取 85 分位
            float median = cand[cand.Count / 2];
            float limit = median * 1.8f;
            int kept = 0;
            for (int i = 0; i < cand.Count; i++) if (cand[i] <= limit) kept = i + 1;
            if (kept < 8) kept = cand.Count;

            int idx = Mathf.Clamp((int)(kept * 0.85f), 0, kept - 1);
            radius = Mathf.Max(0.012f, cand[idx]);
            centerZ = cz;
            return true;
        }

        /// <summary>
        /// 在给定高度上采样身体的外轮廓（按角度分桶取最外点），用于生成贴合的项圈带。
        /// </summary>
        static bool SampleBodyOutline(CatAgent agent, float y, Vector3 axis, int bins, out Vector3[] outline)
        {
            outline = null;

            var smr = agent.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null) return false;

            var scratch = new Mesh();
            smr.BakeMesh(scratch);
            Vector3[] verts = scratch.vertices;
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            UnityEngine.Object.DestroyImmediate(scratch);

            var radius = new float[bins];
            var sumZ = new float[bins];
            var cnt = new int[bins];
            for (int i = 0; i < bins; i++) radius[i] = 0f;

            float minX = axis.x - 0.10f, maxX = axis.x + 0.10f;
            float minZ = axis.z - 0.16f, maxZ = axis.z + 0.16f;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(verts[i]);
                if (Mathf.Abs(w.y - y) > 0.010f) continue;
                if (w.x < minX || w.x > maxX || w.z < minZ || w.z > maxZ) continue;

                float dx = w.x - axis.x;
                float dz = w.z - axis.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r < 0.005f || r > 0.12f) continue;

                float ang = Mathf.Atan2(dz, dx);            // -PI..PI
                int b = Mathf.Clamp((int)((ang + Mathf.PI) / (Mathf.PI * 2f) * bins), 0, bins - 1);
                if (r > radius[b]) { radius[b] = r; sumZ[b] = ang; }
                cnt[b]++;
            }

            // 空桶用相邻桶插值，再做一次环形平滑，避免轮廓有锯齿
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < bins; i++)
                {
                    if (cnt[i] > 0 && pass == 0) continue;
                    float l = radius[(i + bins - 1) % bins];
                    float r2 = radius[(i + 1) % bins];
                    float avg = (l + r2) * 0.5f;
                    if (avg > 0f) radius[i] = avg;
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                var copy = (float[])radius.Clone();
                for (int i = 0; i < bins; i++)
                {
                    float a = copy[(i + bins - 1) % bins];
                    float b2 = copy[i];
                    float c2 = copy[(i + 1) % bins];
                    radius[i] = (a + b2 * 2f + c2) * 0.25f;
                }
            }

            bool any = false;
            for (int i = 0; i < bins; i++) if (radius[i] > 0.005f) any = true;
            if (!any) return false;

            outline = new Vector3[bins];
            for (int i = 0; i < bins; i++)
            {
                float ang = (float)i / bins * Mathf.PI * 2f - Mathf.PI;
                float r = Mathf.Max(0.008f, radius[i]);
                outline[i] = new Vector3(axis.x + Mathf.Cos(ang) * r, y, axis.z + Mathf.Sin(ang) * r);
            }
            return true;
        }

        /// <summary>沿一条闭合轮廓生成圆截面管子（项圈带）。</summary>
        static Mesh CollarTube(string name, Vector3[] path, float inflate, float thickness, Vector3 axis, float y, Transform space)
        {
            int n = path.Length;
            const int sides = 8;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();

            for (int i = 0; i <= n; i++)
            {
                int idx = i % n;
                Vector3 p = path[idx];
                // 往外推一点，避免和毛皮 z-fighting；同时稍微压扁，看起来像布的带子
                Vector3 outward = new Vector3(p.x - axis.x, 0f, p.z - axis.z).normalized;
                Vector3 c = p + outward * inflate;

                for (int j = 0; j <= sides; j++)
                {
                    float a = (float)j / sides * Mathf.PI * 2f;
                    // 截面：上下方向压扁（带子），朝外方向厚一点
                    Vector3 offset = outward * (Mathf.Cos(a) * thickness * 0.7f)
                                   + Vector3.up * (Mathf.Sin(a) * thickness * 1.15f);
                    v.Add(c + offset);
                    uv.Add(new Vector2((float)i / n, (float)j / sides));
                }
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j;
                    int b = a + sides + 1;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b);
                    tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
                }
            }

            // 顶点要烘到"猫的局部坐标"：带子挂在猫下面，用世界坐标烘会被父节点再叠加一次，
            // 猫一挪位置项圈就飞走（本项目实测踩过）
            if (space != null)
            {
                for (int i = 0; i < v.Count; i++) v[i] = space.InverseTransformPoint(v[i]);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>在猫的局部坐标里，量某个高度上最靠前（+z）的毛皮位置。</summary>
        static float MeshFrontLocalZ(CatAgent agent, float localY)
        {
            var smr = agent.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null) return 0.06f;

            var scratch = new Mesh();
            smr.BakeMesh(scratch);
            Vector3[] verts = scratch.vertices;
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            UnityEngine.Object.DestroyImmediate(scratch);

            var zs = new List<float>();
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(verts[i]);
                Vector3 l = agent.transform.InverseTransformPoint(w);
                if (Mathf.Abs(l.y - localY) > 0.015f) continue;
                if (Mathf.Abs(l.x) > 0.05f) continue;
                zs.Add(l.z);
            }
            if (zs.Count < 6) return 0.06f;

            zs.Sort();
            int idx = Mathf.Clamp((int)(zs.Count * 0.95f), 0, zs.Count - 1);
            return zs[idx];
        }

        /// <summary>量某个高度上身体最靠前（+z）的毛皮位置，用来挂铃铛。</summary>
        static float MeshFrontZ(CatAgent agent, float y, Vector3 axis)
        {
            var smr = agent.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null) return 0f;

            var scratch = new Mesh();
            smr.BakeMesh(scratch);
            Vector3[] verts = scratch.vertices;
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            UnityEngine.Object.DestroyImmediate(scratch);

            var zs = new List<float>();
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(verts[i]);
                if (Mathf.Abs(w.y - y) > 0.012f) continue;
                if (Mathf.Abs(w.x - axis.x) > 0.05f) continue;
                zs.Add(w.z);
            }
            if (zs.Count < 6) return 0f;

            zs.Sort();
            int idx = Mathf.Clamp((int)(zs.Count * 0.95f), 0, zs.Count - 1);
            return zs[idx];
        }

        /// <summary>从身体侧面水平打射线，返回命中点的 x（命中不到返回 false）。</summary>
        static bool RaycastBodySide(CatAgent agent, float y, out float hitX)
        {
            hitX = 0f;
            Vector3 c = agent.transform.position;
            Vector3 origin = new Vector3(c.x + 0.45f, y, c.z);
            RaycastHit h;
            if (!Physics.Raycast(origin, Vector3.left, out h, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            hitX = h.point.x;
            return true;
        }

        /// <summary>从正前方往身体打射线，返回命中点的 z。</summary>
        static bool RaycastBodyFront(CatAgent agent, float y, out float hitZ)
        {
            hitZ = 0f;
            Vector3 c = agent.transform.position;
            Vector3 dir = -agent.transform.forward;
            Vector3 origin = c + agent.transform.forward * 0.6f + Vector3.up * (y - c.y);
            RaycastHit h;
            if (!Physics.Raycast(origin, dir, out h, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            hitZ = h.point.z;
            return true;
        }

        static void AddPart(Transform parent, string name, Material mat, Mesh mesh, Vector3 localPos, Vector3 worldScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = parent.position + parent.rotation * localPos;
            go.transform.rotation = parent.rotation;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SaveMesh(mesh);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            ScaleToWorld(go, worldScale);
        }

        /// <summary>把物体缩放到指定世界尺寸（骨骼链有 100 倍缩放，localScale 不能直接写）。</summary>
        static void ScaleToWorld(GameObject go, Vector3 worldSize)
        {
            Vector3 ls = go.transform.lossyScale;
            go.transform.localScale = new Vector3(
                worldSize.x / Mathf.Max(1e-6f, Mathf.Abs(ls.x)),
                worldSize.y / Mathf.Max(1e-6f, Mathf.Abs(ls.y)),
                worldSize.z / Mathf.Max(1e-6f, Mathf.Abs(ls.z)));
        }

        static Material MakeLit(string path, Color color, float smoothness, float metallic)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh SaveMesh(Mesh mesh)
        {
            Directory.CreateDirectory(MeshDir);
            string path = MeshDir + "/" + mesh.name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null)
            {
                var verts = mesh.vertices; var uvs = mesh.uv; var tris = mesh.triangles;
                old.Clear();
                old.name = mesh.name;
                old.vertices = verts;
                if (uvs != null && uvs.Length == verts.Length) old.uv = uvs;
                old.triangles = tris;
                old.RecalculateNormals();
                old.RecalculateBounds();
                EditorUtility.SetDirty(old);
                UnityEngine.Object.DestroyImmediate(mesh);
                return old;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        static Transform FindDeep(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            return null;
        }

        static void EnsureFolders()
        {
            Directory.CreateDirectory(MeshDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(AudioDir);
        }

        static float Arg(Dictionary<string, string> a, string k, float def)
        {
            string v;
            if (a == null || !a.TryGetValue(k, out v)) return def;
            float f;
            return float.TryParse(v, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out f) ? f : def;
        }

        static string V(Vector3 v)
        {
            return string.Format("({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
        }

        // ------------------------------------------------------------------ 菜单

        [MenuItem("NOCTURNE/猫咪/重建外观件（眼睛 + 项圈铃铛）", false, 20)]
        static void MenuRebuildLooks()
        {
            string e = AddEyes(null);
            string b = AddBell(null);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.Object.FindObjectOfType<CatAgent>().gameObject.scene);
            Debug.Log("[CatLooksBuilder] " + e);
            Debug.Log("[CatLooksBuilder] " + b);
        }

        [MenuItem("NOCTURNE/猫咪/开场摆位（脸边凝视）", false, 21)]
        static void MenuSetupOpening()
        {
            Debug.Log("[CatLooksBuilder] " + SetupOpening(null));
        }
    }
}
#endif
