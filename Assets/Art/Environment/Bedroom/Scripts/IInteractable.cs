namespace NOCTURNE
{
    /// <summary>
    /// 可交互物。第一人称控制器用视线射线找到它，按 E 触发。
    /// 实现类只需要提供 <see cref="Prompt"/> 文案和 <see cref="Interact"/> 行为。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>准星旁显示的提示文案，例如「开门」。</summary>
        string Prompt { get; }

        /// <summary>是否还能交互（上锁的门可以返回 false）。</summary>
        bool CanInteract { get; }

        /// <summary>按下交互键时调用。</summary>
        void Interact();
    }
}
