namespace NOCTURNE.Core.Audio
{
    /// <summary>
    /// 全部可播放音效的稳定 ID。<see cref="AudioCatalog"/>（ScriptableObject）负责
    /// 把 ID 映射到实际 <c>AudioClip</c>，代码里**永远不要**直接引用 clip 资源。
    ///
    /// **只追加，不重排**：数值会写进存档的"已听过/已触发"标记，重排会让老档错位。
    /// 新增音效时：先在这里加一项 → 再在 AudioCatalog 里填 clip。
    /// </summary>
    public enum SfxId
    {
        None = 0,

        // ---------------- 猫 ----------------
        CatBell = 1,
        CatMeowAttention = 2,
        CatMeowHungry = 3,
        CatMeowSweet = 4,

        // ---------------- 世界 / 道具 ----------------
        DoorOpen = 10,
        DoorClose = 11,
        DrawerSlide = 12,
        KibblePour = 13,
        BlanketRustle = 14,

        // ---------------- 环境 ----------------
        RoomTone = 20,
        BreathSlow = 21,

        // ---------------- 预留：鬼（Phase C 填充） ----------------
        GhostWhisper = 30,
        GhostStep = 31,
        GhostBreath = 32,
        GhostScream = 33,

        // ---------------- 预留：界面 / 玩家（Phase B/E） ----------------
        UiClick = 40,
        UiHover = 41,
        Footstep = 42,
        HeartBeat = 43
    }
}
