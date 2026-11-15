using System;
using UnityEngine;

namespace NOCTURNE.Core.Flow
{
    /// <summary>
    /// 全局状态中枢。全项目唯一的游戏状态真值。
    ///
    /// 所有"要不要 tick / 能不能操作"的判断都必须走这里的派生谓词，
    /// 不要各自去 `_state == GameState.Xxx` 拼条件——那样每加一个状态就要改一圈。
    ///
    /// 迁移说明（A08）：本类原属 <c>NOCTURNE.Opening</c>，现迁至 <c>NOCTURNE.Core.Flow</c>。
    /// 因为它是 MonoBehaviour，Unity 靠 .meta 的 GUID 绑定脚本实例，
    /// 所以移动文件不会断开场景引用（已实测场景组件仍在、_state 仍为 0）。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        [SerializeField]
        [Tooltip("起始状态。开场由 OpeningSequenceController 接管，保持 Opening 即可")]
        GameState _state = GameState.Opening;

        public GameState State { get { return _state; } }

        // ---------------- 派生谓词（所有系统的唯一查询入口） ----------------

        /// <summary>玩家能动、能交互。等价于"正常游玩"。</summary>
        public bool IsPlayerControlled { get { return _state == GameState.Playing; } }

        /// <summary>时间 / 需求 / 鬼 AI 是否推进。睡眠时也推进。</summary>
        public bool IsSimulationRunning
        {
            get { return _state == GameState.Playing || _state == GameState.Sleeping; }
        }

        /// <summary>游戏内时钟是否在走。睡眠时走（按 sleepTimeScale 加速）。</summary>
        public bool IsTimeAdvancing
        {
            get { return _state == GameState.Playing || _state == GameState.Sleeping; }
        }

        /// <summary>实体 AI（猫 + 鬼）是否运行。过场里仍保留表现，所以 Cutscene 也算。</summary>
        public bool IsEntityAiRunning
        {
            get { return _state == GameState.Playing || _state == GameState.Cutscene; }
        }

        /// <summary>是否处于弹出式 UI（暂停 / 结局）：玩家输入与实体 AI 都停。</summary>
        public bool IsUiModal
        {
            get { return _state == GameState.Paused || _state == GameState.Ended; }
        }

        /// <summary>开场（黑屏 + 独白）阶段。此时连视角都不能转。</summary>
        public bool IsOpening { get { return _state == GameState.Opening; } }

        /// <summary>(旧状态, 新状态)</summary>
        public event Action<GameState, GameState> StateChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetState(GameState next)
        {
            if (_state == next) return;
            var prev = _state;
            _state = next;
            if (StateChanged != null) StateChanged(prev, next);
        }

        /// <summary>供 Inspector 里的按钮/外部脚本强制切换状态（不走 Transition 校验）。</summary>
        public void ForceState(GameState next)
        {
            _state = next;
        }
    }
}
