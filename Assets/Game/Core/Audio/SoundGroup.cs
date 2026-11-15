namespace NOCTURNE.Core.Audio
{
    /// <summary>
    /// 混音分组。音量是乘算关系：最终音量 = clip 自身音量 × 分组音量 × Master。
    ///
    /// **只追加，不重排**（数值可能被存档/日志记录）。
    /// </summary>
    public enum SoundGroup
    {
        /// <summary>总音量。为 0 时一切静音，用于"静音全部"与暂停淡出。</summary>
        Master = 0,

        /// <summary>世界音效：门、抽屉、铃铛、脚步、倒粮。</summary>
        Sfx = 1,

        /// <summary>环境底噪：房间氛围、风声、循环音。</summary>
        Ambience = 2,

        /// <summary>角色语音：猫叫、鬼的低语。</summary>
        Voice = 3,

        /// <summary>界面音：按钮、提示。</summary>
        Ui = 4
    }
}
