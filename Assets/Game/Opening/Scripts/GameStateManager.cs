using System;
using UnityEngine;

namespace NOCTURNE.Opening
{
    /// <summary>
    /// 游戏总状态。
    /// Opening   —— 开场演出：全黑、禁止一切输入，时间/需求系统不推进
    /// Lying     —— 躺卧观察：只允许视角旋转，不能移动
    /// StandingUp—— 起床过渡：禁止移动与重复触发
    /// Playing   —— 正常游戏：完整控制，时间与需求系统推进
    /// </summary>
    public enum GameState
    {
        Opening = 0,
        Lying = 1,
        StandingUp = 2,
        Playing = 3
    }

    /// <summary>
    /// 全局状态中枢。项目此前没有任何状态管理器，这里是唯一的一个。
    ///
    /// 时间推进、疲劳、理智、猫咪需求等系统应以 <see cref="IsGameplayActive"/>
    /// 作为总闸：为 false 时一律不 tick。
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

        /// <summary>只有正式游戏阶段为 true。所有时间/数值/需求系统都应该查这个。</summary>
        public bool IsGameplayActive { get { return _state == GameState.Playing; } }

        /// <summary>非 Playing 时玩家输入应被锁死。</summary>
        public bool IsInputLocked { get { return _state != GameState.Playing; } }

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

        /// <summary>供 Inspector 里的按钮/外部脚本强制回到开场。</summary>
        public void ForceState(GameState next)
        {
            _state = next;
        }
    }
}
