using UnityEngine;

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

    /// <summary>
    /// 「能装食物的容器」——猫粮袋只需要知道这么多就能倒粮，
    /// 不需要认识 <c>CatFoodBowl</c> 这个具体类型。
    /// </summary>
    public interface IFoodContainer
    {
        /// <summary>当前是否还能接受食物（已经满/正在吃时为 false）。</summary>
        bool CanAcceptFood { get; }

        /// <summary>从倒入到装满的时长，袋子据此排布撒粮粒子。</summary>
        float FillDuration { get; }

        /// <summary>把容器填满。</summary>
        void Fill();

        /// <summary>倒粮时袋口要对准的位置（盆口上方）。</summary>
        Transform PourAnchor { get; }
    }

    /// <summary>
    /// 「玩家手上当前拿着什么袋子」。碗通过它查询，从而完全不认识
    /// <c>CatFoodBagInteractable</c> 这个具体类型。
    /// </summary>
    public static class FeedingHands
    {
        /// <summary>当前手持的袋子；空手为 null。由袋子在拿起/放下时维护。</summary>
        public static IFoodBag Held;
    }

    /// <summary>
    /// 「能被拿起来倒粮的袋子」——碗只需要知道这么多，
    /// 不需要认识 <c>CatFoodBagInteractable</c> 这个具体类型。
    /// </summary>
    public interface IFoodBag
    {
        bool IsHeld { get; }
        bool IsPouring { get; }

        /// <summary>开始长按倒粮。返回 false 表示条件不满足（不在手上/容器已满）。</summary>
        bool BeginPourHold(IFoodContainer container);

        /// <summary>长按进度 0~1，用于倾斜表现。</summary>
        void SetPourHoldProgress(float t);

        /// <summary>长按达标，正式倒粮。</summary>
        void CommitPour(IFoodContainer container);

        /// <summary>中途松手，取消倒粮并回正。</summary>
        void CancelPourHold();
    }
}
