using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using NOCTURNE.Cat;
using NOCTURNE.Feeding;
using NOCTURNE.Opening;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 一次性把「首次喂食流程」需要的新物件真实建进场景（不是运行时生成）：
    ///
    ///   · PROP_CatFoodBag  茶几上的猫粮袋（胖袋身 / 封口 / 正面标签 / 交互点 / 撒粮口）
    ///   · PROP_CatBed      沙发对面墙角的猫窝（底座 / 围边 / 坐垫 / CatSleepPoint）
    ///   · FoodPoint        饭盆前的进食站位（猫要站在这里低头）
    ///   · PourAnchor       倒粮视觉锚点
    ///   · 16 颗猫粮颗粒     比原来的 7 颗更能表现"逐渐减少"
    ///   · HoldPoint        玩家手持点（CameraRig 下）
    ///   · FeedHint         任务提示（OpeningCanvas 下，预先创建，只改 alpha）
    ///   · CatFeeding       流程编排 + 猫叫音源
    ///
    /// 网格/材质/贴图全部程序化生成后落成真实资产（Assets/Art/Environment/Bedroom 下），
    /// 不是运行时 new Mesh，Scene 视图里能直接看到、能在 Hierarchy 里选中。
    ///
    /// 重复执行是安全的：会先删掉旧的 PROP_ 物件再重建；饭盆只补缺失的子节点与组件。
    /// </summary>
    public static class CatFeedingPropsBuilder
    {
        const string MeshDir = "Assets/Art/Environment/Bedroom/Meshes";
        const string MatDir = "Assets/Art/Environment/Bedroom/Materials";
        const string TexDir = "Assets/Art/Environment/Bedroom/Textures";
        const string OpeningAudioDir = "Assets/Game/Opening/Audio";
        const string FeedingAudioDir = "Assets/Game/Feeding/Audio";

        // 茶几台面（实测 M_PR_CoffeeTable bounds：center(-0.42,0.22,-4.10) extents(0.5,0.22,0.32)）
        static readonly Vector3 BagPos = new Vector3(-0.05f, 0.44f, -3.90f);
        // 袋子正面（标签）朝 +Z：玩家从卧室门（北侧）进客厅时第一眼就能看到包装
        const float BagYaw = 194f;

        // 沙发在 +X 侧朝 -X，所以"沙发对面的墙"是 -X 侧；饭盆占了西南角，猫窝放这面墙的另一个角
        static readonly Vector3 BedPos = new Vector3(-1.38f, 0f, -2.90f);
        const float BedYaw = 32f;
        const float SleepYaw = 132f;

        // 饭盆（场景里已有，位置 -2.02,0,-6.10）
        static readonly Vector3 BowlPos = new Vector3(-2.02f, 0f, -6.10f);
        static readonly Vector3 EatPointPos = new Vector3(-1.70f, 0f, -6.10f);

        [MenuItem("NOCTURNE/场景/构建喂食物件（猫粮袋 + 猫窝 + 接线）")]
        public static void Build()
        {
            EnsureFolders();

            var bagMat = MakeMaterial("PR_CatFoodBag", new Color(0.545f, 0.480f, 0.380f), 0.10f);
            var bagTopMat = MakeMaterial("PR_CatFoodBagSeal", new Color(0.360f, 0.310f, 0.250f), 0.14f);
            var labelMat = MakeMaterial("PR_CatFoodLabel", new Color(0.860f, 0.820f, 0.735f), 0.08f);
            var bedBaseMat = MakeMaterial("PR_CatBedBase", new Color(0.470f, 0.430f, 0.385f), 0.06f);
            var bedRimMat = MakeMaterial("PR_CatBedRim", new Color(0.560f, 0.505f, 0.435f), 0.05f);
            var bedCushionMat = MakeMaterial("PR_CatBedCushion", new Color(0.720f, 0.665f, 0.580f), 0.05f);

            var kibbleMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/PR_CatFood.mat");
            if (kibbleMat == null) kibbleMat = MakeMaterial("PR_CatFood", new Color(0.360f, 0.235f, 0.125f), 0.12f);

            GameObject bag = BuildBag(bagMat, bagTopMat, labelMat);
            GameObject bed = BuildBed(bedBaseMat, bedRimMat, bedCushionMat);

            CatFoodBowl bowl = ConfigureBowl(kibbleMat);
            Transform hold = ConfigurePlayer();

            OpeningDialogueUI ui = ConfigureUI();
            ConfigureBag(bag, hold);
            ConfigureBed(bed);
            ConfigureController(bowl, bag, bed, ui);

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[CatFeedingBuilder] 完成：猫粮袋 " + BagPos + " / 猫窝 " + BedPos +
                      " / 饭盆进食点 " + EatPointPos + "。场景已保存。", bag);
        }

        // ---------------------------------------------------------------- 猫粮袋

        static GameObject BuildBag(Material bodyMat, Material topMat, Material labelMat)
        {
            DeleteIfExists("PROP_CatFoodBag");

            var root = new GameObject("PROP_CatFoodBag");
            root.transform.SetParent(FindOrCreate("Props").transform, false);
            root.transform.position = BagPos;
            root.transform.rotation = Quaternion.Euler(0f, BagYaw, 0f);

            const float rx = 0.090f, ry = 0.145f, rz = 0.053f;
            const float bagPower = 0.6f;

            // 袋身：超椭球（e=0.6 圆角更明显，像鼓起来的软包装），底在 y=0
            var bodyMesh = SaveMesh(SuperEllipsoid("M_CatFoodBagBody", rx, ry, rz, bagPower, 44, 26));
            MakeChild(root, "BagBody", bodyMat, bodyMesh, new Vector3(0f, ry, 0f));

            // 顶部封口：压扁的小方块，稍微前倾
            var sealMesh = SaveMesh(Box("M_CatFoodBagSeal", new Vector3(0.158f, 0.020f, 0.062f)));
            var seal = MakeChild(root, "BagTop", topMat, sealMesh, new Vector3(0f, ry * 2f + 0.004f, 0.004f));
            seal.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);

            // 正面标签：贴合袋身曲面的贴片（带 UV），贴图程序化生成
            var labelMesh = SaveMesh(SuperPatch("M_CatFoodBagLabel", rx, ry, rz, bagPower, 0.40f, 0.66f, 10, 10, 0.0035f));
            MakeChild(root, "BagFrontLabel", labelMat, labelMesh, new Vector3(0f, ry, 0f));

            Texture2D tex = MakeLabelTexture();
            if (tex != null)
            {
                labelMat.SetTexture("_BaseMap", tex);
                labelMat.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(labelMat);
            }

            // 交互点 + 撒粮口（都是空物体，给脚本/特效做锚点）
            var interact = new GameObject("BagInteractionPoint");
            interact.transform.SetParent(root.transform, false);
            interact.transform.localPosition = new Vector3(0f, ry, 0f);

            var spout = new GameObject("BagSpout");
            spout.transform.SetParent(root.transform, false);
            spout.transform.localPosition = new Vector3(0f, ry * 2f + 0.01f, -0.01f);

            var ps = spout.AddComponent<ParticleSystem>();
            ConfigurePourParticles(ps);

            // 碰撞体：给交互射线用（手持期间脚本会关掉）
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, ry + 0.01f, 0f);
            col.size = new Vector3(0.185f, ry * 2f + 0.03f, 0.115f);

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 1.2f;
            audio.maxDistance = 12f;
            audio.rolloffMode = AudioRolloffMode.Linear;

            var bag = root.AddComponent<CatFoodBagInteractable>();
            bag.audioSource = audio;
            bag.pourFx = ps;

            // 桌面原位锚点：放回时回到这个精确位置
            var anchor = new GameObject("PROP_CatFoodBagTableAnchor");
            anchor.transform.SetParent(root.transform.parent, false);
            anchor.transform.position = BagPos;
            anchor.transform.rotation = Quaternion.Euler(0f, BagYaw, 0f);
            bag.tableAnchor = anchor.transform;

            return root;
        }

        static void ConfigurePourParticles(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.42f;
            main.startSpeed = 0.55f;
            main.startSize = 0.009f;
            main.startColor = new Color(0.360f, 0.235f, 0.125f, 1f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.enabled = false;          // 全部由 Emit() 主动发射，不持续喷

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.012f;
            shape.rotation = new Vector3(90f, 0f, 0f);   // +Z 转向 -Y，朝下撒

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                string matPath = MatDir + "/PR_CatKibbleParticle.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(sh);
                    mat.name = "PR_CatKibbleParticle";
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.SetColor("_BaseColor", new Color(0.360f, 0.235f, 0.125f, 1f));
                EditorUtility.SetDirty(mat);
                renderer.sharedMaterial = mat;
            }
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            var kibble = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/M_Kibble.asset");
            if (kibble != null) renderer.mesh = kibble;
            renderer.alignment = ParticleSystemRenderSpace.World;
        }

        // ---------------------------------------------------------------- 猫窝

        static GameObject BuildBed(Material baseMat, Material rimMat, Material cushionMat)
        {
            DeleteIfExists("PROP_CatBed");

            var root = new GameObject("PROP_CatBed");
            root.transform.SetParent(FindOrCreate("Furniture").transform, false);
            root.transform.position = BedPos;
            root.transform.rotation = Quaternion.Euler(0f, BedYaw, 0f);

            // 底座：矮圆柱
            var baseMesh = SaveMesh(Lathe("M_CatBedBase", new Vector2[]
            {
                new Vector2(0f, 0f), new Vector2(0.30f, 0f),
                new Vector2(0.30f, 0.05f), new Vector2(0f, 0.05f)
            }, 44, false, true));
            MakeChild(root, "BedBase", baseMat, baseMesh, Vector3.zero);

            // 围边：闭合截面（环），有柔软卷边。首尾自动相接，不要重复写第一点
            var rimMesh = SaveMesh(Lathe("M_CatBedRim", new Vector2[]
            {
                new Vector2(0.190f, 0.046f),
                new Vector2(0.292f, 0.052f),
                new Vector2(0.300f, 0.092f),
                new Vector2(0.280f, 0.126f),
                new Vector2(0.230f, 0.132f),
                new Vector2(0.194f, 0.102f)
            }, 48, true, true));
            MakeChild(root, "BedRim", rimMat, rimMesh, Vector3.zero);

            // 坐垫：中间微凹的软垫（中心最低，向外抬高到围边下方）
            var cushionMesh = SaveMesh(Lathe("M_CatBedCushion", new Vector2[]
            {
                new Vector2(0f, 0.056f),
                new Vector2(0.075f, 0.062f),
                new Vector2(0.140f, 0.076f),
                new Vector2(0.190f, 0.086f),
                new Vector2(0.190f, 0.030f),
                new Vector2(0f, 0.030f)
            }, 44, false, true));
            MakeChild(root, "BedCushion", cushionMat, cushionMesh, Vector3.zero);

            var sleep = new GameObject("CatSleepPoint");
            sleep.transform.SetParent(root.transform, false);
            // 坐垫中心表面：局部 (0, 0.056, 0)，朝向房间内侧
            sleep.transform.localPosition = new Vector3(0f, 0.056f, 0f);
            sleep.transform.localRotation = Quaternion.Euler(0f, SleepYaw - BedYaw, 0f);

            return root;
        }

        // ---------------------------------------------------------------- 饭盆

        static CatFoodBowl ConfigureBowl(Material kibbleMat)
        {
            var root = GameObject.Find("CatFoodBowl");
            if (root == null)
            {
                Debug.LogError("[CatFeedingBuilder] 场景里找不到 CatFoodBowl，饭盆部分跳过");
                return null;
            }

            var foodRoot = root.transform.Find("CatFood");
            if (foodRoot == null) foodRoot = root.transform.Find("CatFoodVisual");
            if (foodRoot == null)
            {
                Debug.LogError("[CatFeedingBuilder] 饭盆下找不到 CatFood 食物根节点");
                return null;
            }

            // 进食站位：盆前 0.32m，朝向盆（-X）
            var eat = root.transform.Find("FoodPoint");
            if (eat == null)
            {
                var go = new GameObject("FoodPoint");
                go.transform.SetParent(root.transform, false);
                go.transform.position = EatPointPos;
                go.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                eat = go.transform;
            }

            // 倒粮锚点：盆口上方
            var pour = root.transform.Find("PourAnchor");
            if (pour == null)
            {
                var go = new GameObject("PourAnchor");
                go.transform.SetParent(root.transform, false);
                go.transform.position = BowlPos + Vector3.up * 0.22f;
                pour = go.transform;
            }

            // 补足猫粮颗粒到 16 颗（原来只有 7 颗，不够表现"逐渐减少"）
            var kibbles = EnsureKibbles(foodRoot, kibbleMat, 16);

            var bowl = root.GetComponent<CatFoodBowl>();
            if (bowl == null) bowl = root.AddComponent<CatFoodBowl>();

            bowl.foodVisualRoot = foodRoot;
            bowl.mound = foodRoot;
            bowl.kibbles = kibbles;
            bowl.moundFullScale = foodRoot.localScale;
            bowl.eatingPoint = eat;
            bowl.approachPoint = root.transform.Find("FoodApproachPoint");
            bowl.pourAnchor = pour;
            bowl.fillDuration = 1.7f;
            bowl.eatDuration = 10f;

            var audio = root.GetComponent<AudioSource>();
            if (audio == null) audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 1f;
            audio.maxDistance = 10f;
            bowl.audioSource = audio;
            bowl.fillClip = EnsurePourClip();

            // 场景里的作者状态也摆成"空盆"：评审打开场景看到的就是空盆，不是 Awake 才隐藏
            if (foodRoot.gameObject.activeSelf) foodRoot.gameObject.SetActive(false);

            EditorUtility.SetDirty(root);
            return bowl;
        }

        static Transform[] EnsureKibbles(Transform foodRoot, Material kibbleMat, int want)
        {
            var list = new List<Transform>();
            for (int i = 0; i < foodRoot.childCount; i++)
                if (foodRoot.GetChild(i).name.StartsWith("Kibble_")) list.Add(foodRoot.GetChild(i));

            var kibbleMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/M_Kibble.asset");
            if (kibbleMesh == null)
            {
                Debug.LogError("[CatFeedingBuilder] 缺少 M_Kibble 网格，无法补猫粮颗粒");
                return list.ToArray();
            }

            Vector3 baseScale = list.Count > 0 ? list[0].localScale : new Vector3(0.0065f, 0.0065f, 0.0065f);

            // 固定种子：每次重建都长得一样，方便对比
            var rng = new System.Random(20261112);

            while (list.Count < want)
            {
                int idx = list.Count;
                var go = new GameObject("Kibble_" + idx);
                go.transform.SetParent(foodRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = kibbleMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kibbleMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // 盆内散布：半径 ≤ 0.048，高度 0.030~0.046（盆内壁深 5cm）
                double ang = rng.NextDouble() * Mathf.PI * 2.0;
                double rad = Mathf.Sqrt((float)rng.NextDouble()) * 0.048;
                float y = 0.030f + (float)rng.NextDouble() * 0.016f;

                go.transform.localPosition = new Vector3(
                    (float)(System.Math.Cos(ang) * rad),
                    y,
                    (float)(System.Math.Sin(ang) * rad));
                go.transform.localRotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f);
                go.transform.localScale = baseScale * (0.82f + (float)rng.NextDouble() * 0.36f);

                list.Add(go.transform);
            }

            return list.ToArray();
        }

        // ---------------------------------------------------------------- 玩家手持点

        static Transform ConfigurePlayer()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("[CatFeedingBuilder] 找不到 Player");
                return null;
            }

            var rig = player.transform.Find("CameraRig");
            if (rig == null) rig = player.transform;

            var existing = rig.Find("HoldPoint");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("HoldPoint");
            go.transform.SetParent(rig, false);
            // 相机在眼睛高度，手持点略低略右，位于视锥前方 0.4m
            go.transform.localPosition = new Vector3(0.20f, -0.26f, 0.40f);
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        static void ConfigureBag(GameObject bag, Transform hold)
        {
            if (bag == null) return;
            var comp = bag.GetComponent<CatFoodBagInteractable>();
            if (comp == null) return;

            comp.holdPoint = hold;
            comp.pourClip = EnsurePourClip();
            comp.putBackTime = 0.42f;
            EditorUtility.SetDirty(comp);
        }

        static void ConfigureBed(GameObject bed)
        {
            if (bed == null) return;

            var ctrl = bed.GetComponent<CatBedController>();
            if (ctrl == null) ctrl = bed.AddComponent<CatBedController>();

            ctrl.sleepPoint = bed.transform.Find("CatSleepPoint");
            ctrl.cat = Object.FindObjectOfType<CatAgent>();
            var catGo = GameObject.Find("CAT_Nocturne");
            if (catGo != null) ctrl.lookAt = catGo.GetComponent<CatLookAt>();
            ctrl.settleOnCushion = true;
            ctrl.useSleepPointHeight = true;

            EditorUtility.SetDirty(ctrl);
        }

        // ---------------------------------------------------------------- UI

        static OpeningDialogueUI ConfigureUI()
        {
            var canvas = GameObject.Find("OpeningCanvas");
            var ui = Object.FindObjectOfType<OpeningDialogueUI>();
            if (canvas == null || ui == null)
            {
                Debug.LogError("[CatFeedingBuilder] 找不到 OpeningCanvas / OpeningDialogueUI");
                return ui;
            }

            var old = canvas.transform.Find("FeedHint");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var go = new GameObject("FeedHint", typeof(RectTransform), typeof(CanvasRenderer),
                                    typeof(TextMeshProUGUI), typeof(CanvasGroup));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);

            // 底部居中、屏幕下 1/6 处，不挡视线中心
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 150f);
            rt.sizeDelta = new Vector2(1200f, 90f);

            var text = go.GetComponent<TextMeshProUGUI>();
            var refText = GameObject.Find("GetUpHint");
            if (refText != null)
            {
                var rt2 = refText.GetComponent<TextMeshProUGUI>();
                if (rt2 != null)
                {
                    text.font = rt2.font;
                    text.fontSharedMaterial = rt2.fontSharedMaterial;
                }
            }
            text.text = "它好像饿了，去客厅给它喂点吃的吧。";
            text.fontSize = 30f;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.color = new Color(0.93f, 0.88f, 0.76f, 1f);
            text.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            shadow.effectDistance = new Vector2(0f, -1.5f);

            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            ui.feedHint = group;
            ui.feedHintText = text;
            EditorUtility.SetDirty(ui);
            return ui;
        }

        // ---------------------------------------------------------------- 流程编排

        static void ConfigureController(CatFoodBowl bowl, GameObject bag, GameObject bed, OpeningDialogueUI ui)
        {
            DeleteIfExists("CatFeeding");

            var root = new GameObject("CatFeeding");
            var voice = new GameObject("CatVoice");
            voice.transform.SetParent(root.transform, false);
            var audio = voice.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 1.5f;
            audio.maxDistance = 16f;
            audio.rolloffMode = AudioRolloffMode.Linear;

            var seq = root.AddComponent<CatFeedingSequenceController>();
            var catGo = GameObject.Find("CAT_Nocturne");
            if (catGo != null)
            {
                seq.cat = catGo.GetComponent<CatAgent>();
                seq.opening = catGo.GetComponent<CatOpeningController>();
            }
            seq.lying = Object.FindObjectOfType<PlayerLyingController>();
            seq.bowl = bowl;
            seq.bag = bag != null ? bag.GetComponent<CatFoodBagInteractable>() : null;
            seq.bed = bed != null ? bed.GetComponent<CatBedController>() : null;
            seq.ui = ui;
            var player = GameObject.Find("Player");
            if (player != null) seq.player = player.transform;
            seq.catVoice = audio;
            seq.meowClip = AssetDatabase.LoadAssetAtPath<AudioClip>(OpeningAudioDir + "/cat_hungry.mp3");
            seq.eatClip = AssetDatabase.LoadAssetAtPath<AudioClip>(OpeningAudioDir + "/blanket_rustle.wav");

            EditorUtility.SetDirty(seq);
        }

        // ---------------------------------------------------------------- 资源工具

        static void EnsureFolders()
        {
            EnsureFolder("Assets/Art/Environment/Bedroom");
            EnsureFolder(MeshDir);
            EnsureFolder(MatDir);
            EnsureFolder(TexDir);
            EnsureFolder("Assets/Game/Feeding/Scripts");
            EnsureFolder(FeedingAudioDir);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Material MakeMaterial(string name, Color color, float smoothness)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = name;
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 保存网格资产并返回"实际可用的那个 Mesh 引用"。
        /// 覆盖已有资产时必须返回旧引用：新 new 出来的临时 Mesh 会被销毁，
        /// 直接把临时 Mesh 赋给 MeshFilter 会得到一个 null（上一版就是踩这个坑）。
        /// </summary>
        static Mesh SaveMesh(Mesh mesh)
        {
            string path = MeshDir + "/" + mesh.name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null)
            {
                var verts = mesh.vertices;
                var uvs = mesh.uv;
                var tris = mesh.triangles;
                string meshName = mesh.name;

                old.Clear();
                old.name = meshName;
                old.vertices = verts;
                if (uvs != null && uvs.Length == verts.Length) old.uv = uvs;
                old.triangles = tris;
                old.RecalculateNormals();
                old.RecalculateBounds();

                EditorUtility.SetDirty(old);
                Object.DestroyImmediate(mesh);
                return old;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject MakeChild(GameObject parent, string name, Material mat, Mesh mesh, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        static void DeleteIfExists(string name)
        {
            var existing = GameObject.Find(name);
            while (existing != null)
            {
                Object.DestroyImmediate(existing);
                existing = GameObject.Find(name);
            }
            var anchor = GameObject.Find(name + "TableAnchor");
            if (anchor != null) Object.DestroyImmediate(anchor);
        }

        static GameObject FindOrCreate(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) return go;
            return new GameObject(name);
        }

        // ---------------------------------------------------------------- 网格生成

        /// <summary>
        /// 旋转体：profile 是从下往上的 (半径, 高度) 剖面。
        /// closedLoop=true 表示首尾相接（围边这种环），否则按开口曲面处理，
        /// 半径为 0 的点自动收成一个中心顶点（封口）。
        /// </summary>
        static Mesh Lathe(string name, Vector2[] profile, int segments, bool closedLoop, bool flipIfNeeded)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n = profile.Length;

            var ringStart = new int[n];
            var ringCount = new int[n];

            for (int i = 0; i < n; i++)
            {
                float r = profile[i].x;
                ringStart[i] = verts.Count;
                if (r < 1e-4f)
                {
                    verts.Add(new Vector3(0f, profile[i].y, 0f));
                    ringCount[i] = 1;
                }
                else
                {
                    for (int s = 0; s < segments; s++)
                    {
                        float a = Mathf.PI * 2f * s / segments;
                        verts.Add(new Vector3(Mathf.Cos(a) * r, profile[i].y, Mathf.Sin(a) * r));
                    }
                    ringCount[i] = segments;
                }
            }

            int last = closedLoop ? n - 1 : n - 2;
            for (int i = 0; i <= last; i++)
            {
                int j = (i + 1) % n;
                int a0 = ringStart[i], b0 = ringStart[j];
                int ac = ringCount[i], bc = ringCount[j];

                if (ac == 1 && bc == 1) continue;

                if (ac == 1)
                {
                    for (int s = 0; s < bc; s++)
                    {
                        int s1 = (s + 1) % bc;
                        tris.Add(a0); tris.Add(b0 + s1); tris.Add(b0 + s);
                    }
                }
                else if (bc == 1)
                {
                    for (int s = 0; s < ac; s++)
                    {
                        int s1 = (s + 1) % ac;
                        tris.Add(a0 + s); tris.Add(a0 + s1); tris.Add(b0);
                    }
                }
                else
                {
                    for (int s = 0; s < ac; s++)
                    {
                        int s1 = (s + 1) % ac;
                        tris.Add(a0 + s); tris.Add(a0 + s1); tris.Add(b0 + s);
                        tris.Add(a0 + s1); tris.Add(b0 + s1); tris.Add(b0 + s);
                    }
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (flipIfNeeded) FlipIfInsideOut(mesh);
            return mesh;
        }

        /// <summary>有符号体积为负说明面朝里，整体翻面。</summary>
        static void FlipIfInsideOut(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;

            double vol = 0.0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                vol += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }

            if (vol >= 0.0) return;

            for (int i = 0; i < t.Length; i += 3)
            {
                int tmp = t[i + 1];
                t[i + 1] = t[i + 2];
                t[i + 2] = tmp;
            }
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static float SignPow(float b, float e)
        {
            return Mathf.Sign(b) * Mathf.Pow(Mathf.Abs(b), e);
        }

        static Vector3 SuperPoint(float u, float v, float rx, float ry, float rz, float e)
        {
            float cu = SignPow(Mathf.Cos(u), e);
            float su = SignPow(Mathf.Sin(u), e);
            float cv = SignPow(Mathf.Cos(v), e);
            float sv = SignPow(Mathf.Sin(v), e);
            return new Vector3(rx * cv * cu, ry * sv, rz * cv * su);
        }

        /// <summary>圆角方袋：超椭球，e=0.5 时像鼓起的枕头。</summary>
        static Mesh SuperEllipsoid(string name, float rx, float ry, float rz, float e, int segU, int segV)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int j = 0; j <= segV; j++)
            {
                float v = -Mathf.PI * 0.5f + Mathf.PI * j / segV;
                for (int i = 0; i <= segU; i++)
                {
                    float u = -Mathf.PI + Mathf.PI * 2f * i / segU;
                    verts.Add(SuperPoint(u, v, rx, ry, rz, e));
                }
            }

            int row = segU + 1;
            for (int j = 0; j < segV; j++)
            {
                for (int i = 0; i < segU; i++)
                {
                    int a = j * row + i;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            FlipIfInsideOut(mesh);
            return mesh;
        }

        /// <summary>贴在袋身正面的标签：同一张超椭球面上取一小块，沿法线外移，带 0~1 UV。</summary>
        static Mesh SuperPatch(string name, float rx, float ry, float rz, float e,
                               float uSpan, float vSpan, int segU, int segV, float offset)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // 正面 = -Z 方向（u = -π/2）
            float u0 = -Mathf.PI * 0.5f - uSpan * 0.5f;
            float v0 = -vSpan * 0.5f;

            for (int j = 0; j <= segV; j++)
            {
                for (int i = 0; i <= segU; i++)
                {
                    float u = u0 + uSpan * i / segU;
                    float v = v0 + vSpan * j / segV;
                    Vector3 p = SuperPoint(u, v, rx, ry, rz, e);

                    // 沿"从袋心指向该点"的方向外移，近似曲面法线
                    Vector3 nrm = new Vector3(p.x / (rx * rx), p.y / (ry * ry), p.z / (rz * rz)).normalized;
                    verts.Add(p + nrm * offset);
                    uvs.Add(new Vector2((float)i / segU, (float)j / segV));
                }
            }

            int row = segU + 1;
            for (int j = 0; j < segV; j++)
            {
                for (int i = 0; i < segU; i++)
                {
                    int a = j * row + i;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // 标签要朝外（-Z）；法线整体朝里就翻面
            var normals = mesh.normals;
            Vector3 avg = Vector3.zero;
            for (int i = 0; i < normals.Length; i++) avg += normals[i];
            if (avg.z > 0f)
            {
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp;
                }
                mesh.triangles = t;
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        static Mesh Box(string name, Vector3 size)
        {
            var h = size * 0.5f;
            var v = new Vector3[]
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3( h.x, -h.y, -h.z),
                new Vector3( h.x, -h.y,  h.z), new Vector3(-h.x, -h.y,  h.z),
                new Vector3(-h.x,  h.y, -h.z), new Vector3( h.x,  h.y, -h.z),
                new Vector3( h.x,  h.y,  h.z), new Vector3(-h.x,  h.y,  h.z)
            };
            var t = new int[]
            {
                0,1,2, 0,2,3,        // bottom
                7,6,5, 7,5,4,        // top
                0,4,5, 0,5,1,        // -z
                2,6,7, 2,7,3,        // +z
                1,5,6, 1,6,2,        // +x
                3,7,4, 3,4,0         // -x
            };

            var mesh = new Mesh { name = name };
            mesh.vertices = v;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- 贴图 / 音效

        /// <summary>正面包装贴图：米白底 + 低饱和色带 + 猫头图案 + 几颗猫粮。</summary>
        static Texture2D MakeLabelTexture()
        {
            string path = TexDir + "/T_CatFoodLabel.png";

            const int size = 256;
            var px = new Color32[size * size];

            Color32 bg = new Color32(232, 223, 203, 255);
            Color32 band = new Color32(150, 152, 129, 255);
            Color32 ink = new Color32(74, 64, 54, 255);
            Color32 pale = new Color32(246, 240, 226, 255);
            Color32 kibble = new Color32(112, 78, 46, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    px[y * size + x] = bg;
                }
            }

            // 顶部色带 + 底部细条
            FillRect(px, size, 0, (int)(size * 0.80f), size, (int)(size * 0.20f), band);
            FillRect(px, size, 0, (int)(size * 0.10f), size, (int)(size * 0.035f), band);

            // 猫头：圆 + 两只耳朵 + 眼睛 + 鼻子
            int cx = size / 2, cy = (int)(size * 0.50f), r = (int)(size * 0.135f);
            FillCircle(px, size, cx, cy, r, ink);
            FillTriangle(px, size, cx - r, cy + (int)(r * 0.45f), cx - (int)(r * 0.95f), cy + (int)(r * 1.55f),
                         cx - (int)(r * 0.15f), cy + (int)(r * 0.85f), ink);
            FillTriangle(px, size, cx + r, cy + (int)(r * 0.45f), cx + (int)(r * 0.95f), cy + (int)(r * 1.55f),
                         cx + (int)(r * 0.15f), cy + (int)(r * 0.85f), ink);
            FillCircle(px, size, cx - (int)(r * 0.38f), cy + (int)(r * 0.12f), (int)(r * 0.14f), pale);
            FillCircle(px, size, cx + (int)(r * 0.38f), cy + (int)(r * 0.12f), (int)(r * 0.14f), pale);
            FillTriangle(px, size, cx - (int)(r * 0.11f), cy - (int)(r * 0.20f),
                         cx + (int)(r * 0.11f), cy - (int)(r * 0.20f),
                         cx, cy - (int)(r * 0.40f), pale);

            // 三颗猫粮
            FillCircle(px, size, (int)(size * 0.30f), (int)(size * 0.24f), 9, kibble);
            FillCircle(px, size, (int)(size * 0.40f), (int)(size * 0.20f), 8, kibble);
            FillCircle(px, size, (int)(size * 0.36f), (int)(size * 0.30f), 7, kibble);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.Apply();

            Directory.CreateDirectory(TexDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void FillRect(Color32[] px, int size, int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = y0; y < y0 + h && y < size; y++)
                for (int x = x0; x < x0 + w && x < size; x++)
                    if (x >= 0 && y >= 0) px[y * size + x] = c;
        }

        static void FillCircle(Color32[] px, int size, int cx, int cy, int r, Color32 c)
        {
            int r2 = r * r;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= size) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= size) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2) px[y * size + x] = c;
                }
            }
        }

        static void FillTriangle(Color32[] px, int size, int x1, int y1, int x2, int y2, int x3, int y3, Color32 c)
        {
            int minX = Mathf.Max(0, Mathf.Min(x1, Mathf.Min(x2, x3)));
            int maxX = Mathf.Min(size - 1, Mathf.Max(x1, Mathf.Max(x2, x3)));
            int minY = Mathf.Max(0, Mathf.Min(y1, Mathf.Min(y2, y3)));
            int maxY = Mathf.Min(size - 1, Mathf.Max(y1, Mathf.Max(y2, y3)));

            float d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
            if (Mathf.Abs(d) < 1e-5f) return;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float a = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / d;
                    float b = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / d;
                    float cc = 1f - a - b;
                    if (a >= 0f && b >= 0f && cc >= 0f) px[y * size + x] = c;
                }
            }
        }

        /// <summary>合成倒粮声：一串随机衰减的颗粒碰撞，很短、很轻。</summary>
        static AudioClip EnsurePourClip()
        {
            string path = FeedingAudioDir + "/kibble_pour.wav";
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            const int rate = 44100;
            const float dur = 1.1f;
            int count = Mathf.RoundToInt(rate * dur);

            var rng = new System.Random(4721);
            var noise = new float[count];
            for (int i = 0; i < count; i++) noise[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

            // 60 个随机落点，每个是一段 12ms 的衰减噪声
            var clicks = new List<int>();
            for (int i = 0; i < 60; i++) clicks.Add((int)(rng.NextDouble() * (count - rate * 0.02f)));

            var data = new float[count];
            for (int c = 0; c < clicks.Count; c++)
            {
                int start = clicks[c];
                float amp = 0.25f + (float)rng.NextDouble() * 0.55f;
                int len = (int)(rate * 0.012f);
                for (int i = 0; i < len && start + i < count; i++)
                {
                    float env = Mathf.Exp(-i / (len * 0.25f));
                    data[start + i] += noise[start + i] * env * amp;
                }
            }

            // 收尾淡出 + 归一化
            for (int i = 0; i < count; i++)
            {
                float fade = 1f - Mathf.Clamp01((i - count + rate * 0.15f) / (rate * 0.15f));
                data[i] *= fade * 0.5f;
            }

            Directory.CreateDirectory(FeedingAudioDir);
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + count * 2);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(rate);
                bw.Write(rate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(count * 2);
                for (int i = 0; i < count; i++)
                    bw.Write((short)(Mathf.Clamp(data[i], -1f, 1f) * 32000f));
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
