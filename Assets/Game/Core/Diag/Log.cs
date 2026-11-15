using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace NOCTURNE.Core.Diag
{
    /// <summary>
    /// 全项目唯一的日志入口。新增代码**禁止**直接调用 <see cref="Debug.Log"/>，
    /// 一律走这里，方便以后一键关掉、也保证输出格式统一为 <c>[域] 内容</c>。
    ///
    /// 级别约定（详见 DEVELOPMENT_GUIDE §12.2）：
    /// - Info：状态机关键转移、一次性里程碑
    /// - Warn：配置异常但能继续
    /// - Error：数据不一致、必须修的 bug
    ///
    /// 出包剥离：<see cref="Info"/> 用 [Conditional("UNITY_EDITOR")]，玩家包里连
    /// 参数求值都会消失，不会产生字符串拼接的 GC。
    /// </summary>
    public static class Log
    {
        /// <summary>里程碑级信息。仅编辑器构建保留。</summary>
        [Conditional("UNITY_EDITOR")]
        public static void Info(string tag, string msg)
        {
            Debug.Log("[" + tag + "] " + msg);
        }

        /// <summary>可继续的异常配置。</summary>
        public static void Warn(string tag, string msg, Object ctx = null)
        {
            Debug.LogWarning("[" + tag + "] " + msg, ctx);
        }

        /// <summary>数据不一致 / 必须修的 bug。</summary>
        public static void Error(string tag, string msg, Object ctx = null)
        {
            Debug.LogError("[" + tag + "] " + msg, ctx);
        }
    }
}
