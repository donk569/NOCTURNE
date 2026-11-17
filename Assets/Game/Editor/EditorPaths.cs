namespace NOCTURNE.EditorTools
{
    /// <summary>
    /// 编辑器工具的**唯一路径来源**。业务/运行时脚本不该用这些常量（它们属于编辑器侧约定）。
    ///
    /// 为什么集中：审计发现 18 处字面量散落在 `CatLooksBuilder` / `CatFeedingPropsBuilder` /
    /// `BatchApply` / `AudioCatalogBuilder` 里。目录一改名，工具就会静默生成空物体或读不到文件，
    /// 而且报错信息只告诉你"没找到"，不告诉你在找什么路径。
    ///
    /// 改这里的常量时，配合 `AssetPathValidator` 跑一次就能确认全部仍然有效。
    /// </summary>
    public static class EditorPaths
    {
        // ---------------- 猫 ----------------
        public const string CatMeshDir = "Assets/Art/Cat/Meshes";
        public const string CatMaterialDir = "Assets/Art/Cat/Materials";
        public const string CatAudioDir = "Assets/Game/Cat/Audio";
        public const string CatModelFbx = "Assets/Art/Cat/Cat_All.fbx";
        public const string CatAnimatorController = "Assets/Art/Cat/AC_Cat.controller";
        public const string CatWalkWideAnim = "Assets/Art/Cat/AC_Cat_WalkWide.anim";

        // ---------------- 卧室 ----------------
        public const string BedroomRoot = "Assets/Art/Environment/Bedroom";
        public const string BedroomMeshDir = BedroomRoot + "/Meshes";
        public const string BedroomMaterialDir = BedroomRoot + "/Materials";
        public const string BedroomTextureDir = BedroomRoot + "/Textures";

        // ---------------- 音频 ----------------
        public const string OpeningAudioDir = "Assets/Game/Opening/Audio";
        public const string FeedingAudioDir = "Assets/Game/Feeding/Audio";
        public const string AudioCatalogDir = "Assets/Game/Core/Audio/Resources";

        public const string SfxCatAttention = OpeningAudioDir + "/cat_attention.mp3";
        public const string SfxCatHungry = OpeningAudioDir + "/cat_hungry.mp3";
        public const string SfxCatSweet = OpeningAudioDir + "/cat_sweet.mp3";

        // ---------------- 其它 ----------------
        public const string FeedingScriptDir = "Assets/Game/Feeding/Scripts";

        /// <summary>所有"必须存在"的目录，供 <see cref="AssetPathValidator"/> 遍历。</summary>
        public static readonly string[] RequiredDirs =
        {
            CatMeshDir, CatMaterialDir, CatAudioDir,
            BedroomMeshDir, BedroomMaterialDir, BedroomTextureDir,
            OpeningAudioDir, FeedingAudioDir, AudioCatalogDir, FeedingScriptDir
        };

        /// <summary>所有"必须存在"的文件。</summary>
        public static readonly string[] RequiredFiles =
        {
            CatModelFbx, CatAnimatorController, CatWalkWideAnim,
            SfxCatAttention, SfxCatHungry, SfxCatSweet
        };
    }
}
