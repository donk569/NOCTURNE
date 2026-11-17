using System.Text;
using UnityEditor;
using UnityEngine;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 校验 <see cref="EditorPaths"/> 里的每一条路径是否真的存在。
    ///
    /// 为什么需要：这些路径被编辑器工具用来"生成/重建 prefab"。目录被改名或资源被删时，
    /// 工具不会崩，而是**静默生成一个缺材质的空物体**——那种问题要到运行时才暴露。
    /// 这个校验器把它提前到一次菜单点击。
    /// </summary>
    public static class AssetPathValidator
    {
        [MenuItem("NOCTURNE/校验/资源路径", false, 60)]
        public static void ValidateAll()
        {
            var sb = new StringBuilder();
            int bad = 0;

            sb.AppendLine("[Paths] 检查目录：" + EditorPaths.RequiredDirs.Length + " 个");
            foreach (var dir in EditorPaths.RequiredDirs)
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    sb.AppendLine("  ✗ 目录不存在: " + dir);
                    bad++;
                }
            }

            sb.AppendLine("[Paths] 检查文件：" + EditorPaths.RequiredFiles.Length + " 个");
            foreach (var file in EditorPaths.RequiredFiles)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(file);
                if (asset == null)
                {
                    sb.AppendLine("  ✗ 文件不存在: " + file);
                    bad++;
                }
            }

            if (bad == 0)
            {
                Debug.Log("[Paths] 全部通过：目录 " + EditorPaths.RequiredDirs.Length
                          + " 个、文件 " + EditorPaths.RequiredFiles.Length + " 个均存在。");
                return;
            }

            Debug.LogError("[Paths] 有 " + bad + " 条路径失效（完整路径见下），编辑器工具会因此生成残缺物体：\n" + sb);
        }
    }
}
