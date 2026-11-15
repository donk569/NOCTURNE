namespace NOCTURNE.Core.Flow
{
    /// <summary>
    /// 游戏总状态。
    ///
    /// **铁律：只许在末尾追加，永不重排、永不删除。**
    /// 数值已被 <see cref="GameStateManager._state"/> 序列化进场景（当前值 0），
    /// 重排会让老场景读到错误的状态。同理适用于 <c>CatMood</c>、<c>FeedingPhase</c>、
    /// <c>CatFoodBowlState</c>、<c>CatFoodBagState</c>。
    /// </summary>
    public enum GameState
    {
        /// <summary>开场演出：全黑、禁止一切输入，时间/需求系统不推进。</summary>
        Opening = 0,

        /// <summary>躺卧观察：只允许视角旋转，不能移动。</summary>
        Lying = 1,

        /// <summary>起床过渡：禁止移动与重复触发。</summary>
        StandingUp = 2,

        /// <summary>正常游玩：完整控制，时间与需求系统推进。</summary>
        Playing = 3,

        /// <summary>睡眠中：时间加速推进，玩家输入锁死。</summary>
        Sleeping = 4,

        /// <summary>通用过场（开场之外的新演出都用它）。</summary>
        Cutscene = 5,

        /// <summary>暂停：Time.timeScale = 0。</summary>
        Paused = 6,

        /// <summary>结局：输入锁死，等待回到菜单或读档。</summary>
        Ended = 7,

        /// <summary>读档 / 加载中（预留）。</summary>
        Loading = 8
    }
}
