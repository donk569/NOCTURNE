using UnityEngine;

namespace NOCTURNE.Nav
{
    /// <summary>
    /// 导航域对「门」的全部认知：门板挂在哪个 Transform 上、这扇门当前是否允许实体通过。
    ///
    /// 为什么要有这个接口：<c>NavGrid.Build</c> 烘焙时必须把可通行门的门板碰撞体从障碍里剔除，
    /// 否则门洞会被封死。但门的语义（谁能开、合页几何、开门动画、猫被门板扫到的规避）属于实体域，
    /// 不属于导航域。导航域只依赖这个两成员契约，实体域自己实现，双方都不必知道对方。
    ///
    /// 契约刻意做小：加成员前先问「烘焙真的需要吗」。导航域不许出现 <c>MonoBehaviour</c>/<c>GameObject</c>/<c>Find*</c>，
    /// 所以这里只有 <see cref="Transform"/> 与 <see cref="bool"/>。
    /// </summary>
    public interface IDoorPassage
    {
        /// <summary>这扇门当前是否可通行（设计师开关 + 物理尺寸合理性，由实现者决定）。</summary>
        bool Passable { get; }

        /// <summary>门板根 Transform（碰撞体挂在它下面）。为 <c>null</c> 时烘焙会跳过这扇门。</summary>
        Transform DoorTransform { get; }
    }
}
