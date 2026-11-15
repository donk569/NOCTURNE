using NUnit.Framework;
using NOCTURNE.Cat;

namespace NOCTURNE.Cat.Tests
{
    /// <summary>
    /// CatBrain 决策层单元测试。纯 C#，不加载场景，毫秒级。
    /// 覆盖：距离分档、迟滞、逃跑锁定、睡眠、一次性动作抢占与归还。
    /// </summary>
    public class CatBrainTests
    {
        static CatSenses S(float dist, bool looking = false)
        {
            return new CatSenses
            {
                playerDistance = dist,
                playerVisible = dist < 8f,
                playerLookingAtMe = looking
            };
        }

        CatBrain NewBrain()
        {
            var b = new CatBrain();
            b.alertRadius = 5f;
            b.fleeRadius = 2.2f;
            b.hysteresis = 0.7f;
            b.fleeMinTime = 1.6f;
            b.sleepAfter = 20f;
            b.watchedRadiusScale = 1.35f;
            b.canSleep = true;
            return b;
        }

        // ------------------------------------------------------------ 距离分档

        [Test]
        public void FarPlayer_StaysIdle()
        {
            var b = NewBrain();
            Assert.AreEqual(CatMood.Idle, b.Tick(S(10f), 0.1f));
        }

        [Test]
        public void WithinAlertRadius_BecomesAlert()
        {
            var b = NewBrain();
            Assert.AreEqual(CatMood.Alert, b.Tick(S(4f), 0.1f));
        }

        [Test]
        public void WithinFleeRadius_StartsWalkingAway()
        {
            var b = NewBrain();
            Assert.AreEqual(CatMood.Walk, b.Tick(S(2f), 0.1f));
        }

        // ------------------------------------------------------------ 迟滞

        [Test]
        public void Alert_HoldsUntilBeyondHysteresis()
        {
            var b = NewBrain();
            b.Tick(S(4f), 0.1f);
            Assert.AreEqual(CatMood.Alert, b.Mood);

            // 5.3 < alert(5) + hysteresis(0.7)，仍在警觉
            b.Tick(S(5.3f), 0.1f);
            Assert.AreEqual(CatMood.Alert, b.Mood, "刚越过 alertRadius 不应该立刻掉回 Idle");

            // 6.0 越过 5.7，回到 Idle
            b.Tick(S(6.0f), 0.1f);
            Assert.AreEqual(CatMood.Idle, b.Mood);
        }

        // ------------------------------------------------------------ 逃跑锁定

        [Test]
        public void Walk_LatchesForMinTime()
        {
            var b = NewBrain();
            Assert.AreEqual(CatMood.Walk, b.Tick(S(2f), 0.1f));

            // 玩家瞬间退到很远，但逃跑还没够 1.6s
            b.Tick(S(12f), 0.5f);
            Assert.AreEqual(CatMood.Walk, b.Mood, "逃跑应有最短持续时间");

            b.Tick(S(12f), 1.5f);
            Assert.AreEqual(CatMood.Idle, b.Mood, "超过最短时间且已拉开距离应回 Idle");
        }

        // ------------------------------------------------------------ 睡眠

        [Test]
        public void IdleLongEnough_FallsAsleep()
        {
            var b = NewBrain();
            for (int i = 0; i < 25; i++) b.Tick(S(12f), 1.0f);
            Assert.AreEqual(CatMood.Sleep, b.Mood);
        }

        [Test]
        public void Sleep_WakesWhenPlayerApproaches()
        {
            var b = NewBrain();
            b.ForceMood(CatMood.Sleep);
            Assert.AreEqual(CatMood.Alert, b.Tick(S(4f), 0.1f));
        }

        [Test]
        public void CanSleepFalse_NeverSleeps()
        {
            var b = NewBrain();
            b.canSleep = false;
            for (int i = 0; i < 40; i++) b.Tick(S(12f), 1.0f);
            Assert.AreEqual(CatMood.Idle, b.Mood);
        }

        // ------------------------------------------------------------ 一次性动作

        [Test]
        public void OneShot_LocksMoodIgnoringDistance()
        {
            var b = NewBrain();
            b.PlayOneShot(CatMood.Pounce, 2f);
            Assert.IsTrue(b.IsLocked);

            // 玩家贴脸也不该打断扑击
            for (int i = 0; i < 10; i++) b.Tick(S(0.3f), 0.1f);
            Assert.AreEqual(CatMood.Pounce, b.Mood, "一次性动作期间不应被距离打断");
        }

        [Test]
        public void OneShot_ReturnsToIdleWhenFinished()
        {
            var b = NewBrain();
            b.PlayOneShot(CatMood.Sit, 1f);
            for (int i = 0; i < 9; i++) b.Tick(CatSenses.Far, 0.1f);
            Assert.AreEqual(CatMood.Sit, b.Mood);

            b.Tick(CatSenses.Far, 0.2f);
            Assert.IsFalse(b.IsLocked);
            Assert.AreEqual(CatMood.Idle, b.Mood, "一次性动作播完应回到 Idle");
        }

        [Test]
        public void ForceMood_ClearsLock()
        {
            var b = NewBrain();
            b.PlayOneShot(CatMood.Sit, 5f);
            b.ForceMood(CatMood.Alert);
            Assert.IsFalse(b.IsLocked);
            Assert.AreEqual(CatMood.Alert, b.Mood);
        }

        // ------------------------------------------------------------ 被注视放大

        [Test]
        public void WatchedByPlayer_EnlargesFleeRadius()
        {
            var b = NewBrain();
            // 2.5 米：正常情况只是 Alert，但被正对时 flee=2.2*1.35=2.97
            Assert.AreEqual(CatMood.Alert, b.Tick(S(2.5f, false), 0.1f));

            var b2 = NewBrain();
            Assert.AreEqual(CatMood.Walk, b2.Tick(S(2.5f, true), 0.1f));
        }

        // ------------------------------------------------------------ 杂项

        [Test]
        public void Reset_RestoresInitialState()
        {
            var b = NewBrain();
            b.PlayOneShot(CatMood.Pounce, 3f);
            b.Reset();
            Assert.AreEqual(CatMood.Idle, b.Mood);
            Assert.IsFalse(b.IsLocked);
            Assert.AreEqual(0f, b.TimeInMood, 1e-5f);
        }

        [Test]
        public void ClipNames_MatchRequiredList()
        {
            string[] expect = new string[]
            {
                "Cat_Idle", "Cat_Walk", "Cat_Sit", "Cat_Sleep",
                "Cat_Eat", "Cat_Drink", "Cat_Alert", "Cat_Pounce"
            };
            for (int i = 0; i < expect.Length; i++)
            {
                Assert.AreEqual(expect[i], CatClips.Of((CatMood)i));
                Assert.AreEqual(expect[i], CatClips.BY_MOOD[i]);
            }
        }

        [Test]
        public void OnlySitAndPounceAreOneShot()
        {
            Assert.IsTrue(CatClips.IsOneShot(CatMood.Sit));
            Assert.IsTrue(CatClips.IsOneShot(CatMood.Pounce));
            Assert.IsFalse(CatClips.IsOneShot(CatMood.Idle));
            Assert.IsFalse(CatClips.IsOneShot(CatMood.Walk));
            Assert.IsFalse(CatClips.IsOneShot(CatMood.Sleep));
            Assert.IsFalse(CatClips.IsOneShot(CatMood.Eat));
            Assert.IsFalse(CatClips.IsOneShot(CatMood.Drink));
            Assert.IsFalse(CatClips.IsOneShot(CatMood.Alert));
        }
    }
}
