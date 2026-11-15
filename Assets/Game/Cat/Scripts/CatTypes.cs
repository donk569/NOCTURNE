using UnityEngine;

namespace NOCTURNE.Cat
{
    /// <summary>黑猫的行为状态，与 FBX 里的动画剪辑一一对应。</summary>
    public enum CatMood
    {
        Idle = 0,
        Walk = 1,
        Sit = 2,
        Sleep = 3,
        Eat = 4,
        Drink = 5,
        Alert = 6,
        Pounce = 7,
        /// <summary>小跑/奔跑：速度超过 runAnimSpeed 时替换 Walk，和普通慢走区分开。</summary>
        Run = 8
    }

    /// <summary>
    /// 感知快照。决策层只吃这个结构，不碰物理/动画，方便单元测试。
    /// </summary>
    public struct CatSenses
    {
        /// <summary>到玩家的水平距离（米）。没有玩家时传 float.PositiveInfinity。</summary>
        public float playerDistance;

        /// <summary>玩家是否在猫的视野范围内。</summary>
        public bool playerVisible;

        /// <summary>玩家是否正朝向我（被注视时判定半径会放大）。</summary>
        public bool playerLookingAtMe;

        public static CatSenses Far
        {
            get
            {
                return new CatSenses
                {
                    playerDistance = float.PositiveInfinity,
                    playerVisible = false,
                    playerLookingAtMe = false
                };
            }
        }
    }

    /// <summary>动画剪辑名，必须与 FBX 内 NLA 轨道名完全一致。</summary>
    public static class CatClips
    {
        public const string IDLE = "Cat_Idle";
        public const string WALK = "Cat_Walk";
        public const string SIT = "Cat_Sit";
        public const string SLEEP = "Cat_Sleep";
        public const string EAT = "Cat_Eat";
        public const string DRINK = "Cat_Drink";
        public const string ALERT = "Cat_Alert";
        public const string POUNCE = "Cat_Pounce";
        public const string RUN = "Cat_Run";

        /// <summary>按 <see cref="CatMood"/> 顺序排列的剪辑名。</summary>
        public static readonly string[] BY_MOOD = new string[]
        {
            IDLE, WALK, SIT, SLEEP, EAT, DRINK, ALERT, POUNCE, RUN
        };

        public static string Of(CatMood mood)
        {
            int i = (int)mood;
            return (i >= 0 && i < BY_MOOD.Length) ? BY_MOOD[i] : IDLE;
        }

        /// <summary>一次性动作（播完要回到 Idle），其余是循环。</summary>
        public static bool IsOneShot(CatMood mood)
        {
            return mood == CatMood.Sit || mood == CatMood.Pounce;
        }
    }

    /// <summary>骨架里需要程序化处理的骨头名（与 Blender 导出一致）。</summary>
    public static class CatBones
    {
        public const string ROOT = "root";
        public const string HEAD = "head";
        public const string NECK = "neck";
    }
}
