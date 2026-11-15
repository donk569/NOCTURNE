namespace NOCTURNE.Feeding
{
    /// <summary>
    /// 首次喂食流程的阶段。只用于流程脚本内部编排与调试显示，
    /// 不参与决策（决策在各子系统的状态里）。
    /// </summary>
    public enum FeedingPhase
    {
        Opening = 0,
        GetUp = 1,
        ApproachPlayer = 2,
        Meow = 3,
        WaitingForFood = 4,
        RunToFood = 5,
        Eating = 6,
        ReturnToBed = 7,
        Sleeping = 8
    }

    /// <summary>饭盆食物状态：空 → 有粮 → 正在吃 → 吃完（视觉上仍是空盆）。</summary>
    public enum CatFoodBowlState
    {
        Empty = 0,
        Filled = 1,
        Eating = 2,
        EmptyAfterEating = 3
    }

    /// <summary>猫粮袋状态：桌面 → 手持 → 倒粮中 → 手持。</summary>
    public enum CatFoodBagState
    {
        OnTable = 0,
        Held = 1,
        Pouring = 2
    }
}
