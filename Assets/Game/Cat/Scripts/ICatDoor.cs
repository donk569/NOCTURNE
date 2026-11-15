using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>
    /// 猫能"看懂"的门。
    ///
    /// 为什么要有这层接口：<c>NOCTURNE.Cat.asmdef</c> 的 references 是空的，
    /// 猫的程序集看不到 Assembly-CSharp 里的 HingedDoor / SlidingDoor，
    /// 直接引用会编译不过（程序集引用是单向的：Assembly-CSharp → NOCTURNE.Cat）。
    /// 所以把契约放在猫这一侧，门脚本反过来实现它即可。
    ///
    /// 这样猫不需要在场景里拖任何门引用，也不会和现有门系统重复。
    /// </summary>
    public interface ICatDoor
    {
        /// <summary>门板 Transform（合页/门扇原点，用来算等待点和门平面）。</summary>
        Transform DoorTransform { get; }

        /// <summary>这扇门是否允许猫自己开（阳台门、窗、柜门应返回 false）。</summary>
        bool CatCanOpen { get; }

        /// <summary>逻辑上的开关状态（注意：刚触发时门还在转，不代表已经能过）。</summary>
        bool IsOpen { get; }

        /// <summary>是否已经"开到位"，猫只在这个为 true 时才穿过门洞。</summary>
        bool IsFullyOpen { get; }

        /// <summary>开门进度 0..1，用于调试和超时判断。</summary>
        float OpenProgress { get; }

        /// <summary>请求开门/关门（复用门自己已有的插值逻辑）。</summary>
        void SetOpen(bool open);
    }
}
