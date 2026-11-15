#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using NOCTURNE.Cat;
using NOCTURNE.Feeding;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 批处理入口：编辑器开不起来（UPM 服务起不来 / 界面卡死）时，
    /// 用 -batchmode -executeMethod 把"改场景"的活干完并落盘。
    ///
    /// 命令行：
    ///   Unity.exe -batchmode -quit -projectPath &lt;项目&gt; ^
    ///     [-executeMethod NOCTURNE.EditorTools.BatchApply.Run] -logFile &lt;日志&gt;
    ///
    /// 做的事（幂等，可重复跑）：
    ///   1) 重建猫咪眼睛 + 项圈铃铛
    ///   2) 放大走路步幅（只做一次，重复跑会叠加，所以按 AC_Cat_WalkWide 是否已存在判断）
    ///   3) 饭盆进食点前移到碗边
    ///   4) 保存场景
    /// </summary>
    public static class BatchApply
    {
        const string ReportPath = "D:/unity/NOCTURNE/.workbuddy/batch_apply.txt";

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== BatchApply " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

            try
            {
                sb.AppendLine("[eyes] " + CatLooksBuilder.AddEyes(null));
            }
            catch (Exception e) { sb.AppendLine("[eyes] FAIL " + e.Message); }

            try
            {
                sb.AppendLine("[bell] " + CatLooksBuilder.AddBell(null));
            }
            catch (Exception e) { sb.AppendLine("[bell] FAIL " + e.Message); }

            // 步幅只放大一次：已经有 Wide 剪辑就不再叠加
            try
            {
                bool exists = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Art/Cat/AC_Cat_WalkWide.anim") != null;
                if (exists)
                    sb.AppendLine("[walk] 已存在 AC_Cat_WalkWide.anim，跳过（避免重复放大）");
                else
                    sb.AppendLine("[walk] " + CatLooksBuilder.BoostWalkStride(null));
            }
            catch (Exception e) { sb.AppendLine("[walk] FAIL " + e.Message); }

            // 进食点：让嘴真正到碗上
            try
            {
                var bowl = UnityEngine.Object.FindObjectOfType<CatFoodBowl>();
                if (bowl != null && bowl.eatingPoint != null)
                {
                    Vector3 old = bowl.eatingPoint.position;
                    Vector3 want = bowl.transform.position;
                    // 从碗心朝"猫来的方向"退 0.20m（碗半径约 0.075，猫头前伸约 0.17）
                    Vector3 dir = new Vector3(old.x - want.x, 0f, old.z - want.z);
                    if (dir.sqrMagnitude < 1e-4f) dir = new Vector3(1f, 0f, 0f);
                    dir.Normalize();
                    Vector3 pos = new Vector3(want.x + dir.x * 0.20f, 0f, want.z + dir.z * 0.20f);
                    bowl.eatingPoint.position = pos;
                    EditorUtility.SetDirty(bowl.eatingPoint);
                    sb.AppendLine(string.Format("[eat] eatingPoint {0} → {1}", V(old), V(pos)));
                }
                else sb.AppendLine("[eat] 找不到饭盆或进食点");
            }
            catch (Exception e) { sb.AppendLine("[eat] FAIL " + e.Message); }

            // 落盘
            try
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                sb.AppendLine("[save] " + scene.name + " -> " + ok);
            }
            catch (Exception e) { sb.AppendLine("[save] FAIL " + e.Message); }

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log(sb.ToString());
        }

        static string V(Vector3 v)
        {
            return string.Format("({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
        }
    }
}
#endif
