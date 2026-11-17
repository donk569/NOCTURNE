using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 一键跑 EditMode 单测并把结果同时打到 Console 与文件。
    ///
    /// 为什么需要：`CatBrainTests`（210 行）从项目建立起就没被执行过。
    /// 没有执行过的测试等于不存在——它只证明"能编译"，不证明"行为正确"。
    /// </summary>
    public sealed class RunEditModeTests : ICallbacks
    {
        const string LogPath = "../.workbuddy/editmode_tests.log";

        [MenuItem("NOCTURNE/测试/跑 EditMode 单测", false, 70)]
        public static void Run()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new RunEditModeTests());

            var filter = new Filter { testMode = TestMode.EditMode };
            api.Execute(new ExecutionSettings(filter));
            Debug.Log("[Tests] 已提交 EditMode 测试任务，结果会写入 .workbuddy/editmode_tests.log");
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            Debug.Log("[Tests] EditMode 开始，共 " + testsToRun.TestCaseCount + " 个用例");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            string line = "passed=" + result.PassCount
                        + " failed=" + result.FailCount
                        + " skipped=" + result.SkipCount
                        + " inconclusive=" + result.InconclusiveCount;

            Debug.Log("[Tests] EditMode 结束：" + line);

            try
            {
                var full = Path.GetFullPath(Path.Combine(Application.dataPath, LogPath));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, line + "\n" + result.Name + "\n");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Tests] 写结果文件失败：" + e.Message);
            }
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.HasChildren) return;
            var status = result.TestStatus.ToString();
            if (status == "Failed")
                Debug.LogError("[Tests] FAIL " + result.Name + " -> " + result.Message);
        }
    }
}
