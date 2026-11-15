using System.Collections.Generic;
using UnityEngine;

namespace NOCTURNE.Core.Flow
{
    /// <summary>
    /// 跨系统的"控制权"登记表：谁在阻止什么，是引用计数而不是布尔量。
    ///
    /// 为什么需要它：项目里原来有三套互不相干的机制——`FeedingInteractionLock`
    /// （靠把 `interactDistance` 改成 0 实现）、`PlayerSittingController` 与
    /// `PlayerLyingController` 的 `fpc.enabled = false`。前者的行为是**用副作用做开关**，
    /// 一旦某次没还原，交互就永久坏掉且很难查。
    ///
    /// 用法铁律：
    /// - 谁 `Acquire` 谁 `Release`，成对出现；`OnDisable` 里调 <see cref="ReleaseAll"/> 兜底。
    /// - 查询方只问 <see cref="Blocks"/> / <see cref="BlocksAny"/>，不要去读别人身上的布尔字段。
    /// </summary>
    public static class ControlLock
    {
        /// <summary>谁在阻止什么。只追加，不重排（可能被日志/存档以字符串形式记录）。</summary>
        public enum Reason
        {
            Cutscene = 0,
            Sitting = 1,
            Lying = 2,
            Pouring = 3,
            Holding = 4,
            Sleeping = 5,
            Pause = 6,
            UiModal = 7
        }

        static readonly Dictionary<Reason, HashSet<object>> _holders =
            new Dictionary<Reason, HashSet<object>>();

        static readonly Dictionary<object, HashSet<Reason>> _byOwner =
            new Dictionary<object, HashSet<Reason>>();

        /// <summary>登记一条阻塞。同一 (reason, owner) 重复 Acquire 不会重复计数。</summary>
        public static void Acquire(Reason reason, object owner)
        {
            if (owner == null) return;

            HashSet<object> set;
            if (!_holders.TryGetValue(reason, out set))
            {
                set = new HashSet<object>();
                _holders[reason] = set;
            }
            if (!set.Add(owner)) return;   // 已经持有

            HashSet<Reason> owned;
            if (!_byOwner.TryGetValue(owner, out owned))
            {
                owned = new HashSet<Reason>();
                _byOwner[owner] = owned;
            }
            owned.Add(reason);
        }

        /// <summary>解除一条阻塞。必须与 Acquire 成对。</summary>
        public static void Release(Reason reason, object owner)
        {
            if (owner == null) return;

            HashSet<object> set;
            if (_holders.TryGetValue(reason, out set))
            {
                set.Remove(owner);
                if (set.Count == 0) _holders.Remove(reason);
            }

            HashSet<Reason> owned;
            if (_byOwner.TryGetValue(owner, out owned))
            {
                owned.Remove(reason);
                if (owned.Count == 0) _byOwner.Remove(owner);
            }
        }

        /// <summary>解除该 owner 持有的全部阻塞。用于 OnDisable / 异常中断兜底。</summary>
        public static void ReleaseAll(object owner)
        {
            if (owner == null) return;

            HashSet<Reason> owned;
            if (!_byOwner.TryGetValue(owner, out owned)) return;

            var copy = new List<Reason>(owned);
            for (int i = 0; i < copy.Count; i++) Release(copy[i], owner);
        }

        /// <summary>某一个理由是否正在阻塞。</summary>
        public static bool Blocks(Reason reason)
        {
            HashSet<object> set;
            return _holders.TryGetValue(reason, out set) && set.Count > 0;
        }

        /// <summary>是否有任何理由在阻塞（交互检测用这个：锁住时清空准星目标）。</summary>
        public static bool BlocksAny
        {
            get
            {
                foreach (var kv in _holders)
                    if (kv.Value.Count > 0) return true;
                return false;
            }
        }

        /// <summary>当前持有者数量（诊断/自测用）。</summary>
        public static int HolderCount(Reason reason)
        {
            HashSet<object> set;
            return _holders.TryGetValue(reason, out set) ? set.Count : 0;
        }

        /// <summary>清空全部登记。场景重载 / 退出 Play Mode 时调用。</summary>
        public static void Clear()
        {
            _holders.Clear();
            _byOwner.Clear();
        }

        // 静态状态跨场景不会自动重置（尤其在关闭域重载时），这里兜底清一次。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Clear();
        }
    }
}
