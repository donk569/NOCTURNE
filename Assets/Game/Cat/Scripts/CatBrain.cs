namespace NOCTURNE.Cat
{
    /// <summary>
    /// 黑猫的决策层：纯 C#，不依赖 UnityEngine，可直接单元测试。
    /// 输入每帧的 <see cref="CatSenses"/>，输出当前 <see cref="CatMood"/>。
    ///
    /// 两层结构
    /// - 底层：按玩家距离在 Idle / Alert / Flee(Walk) / Sleep 之间切换；
    /// - 上层：一次性动作（Sit / Eat / Drink / Pounce）通过 <see cref="PlayOneShot"/>
    ///   抢占一段时间，期间距离再近也不会被打断，播完自动回到距离驱动。
    ///
    /// 阈值都带迟滞，避免玩家站在边界上来回抖动。
    /// </summary>
    public sealed class CatBrain
    {
        /// <summary>小于这个距离立刻逃跑（米）。</summary>
        public float fleeRadius = 2.2f;

        /// <summary>进入警觉距离（米）。</summary>
        public float alertRadius = 5.0f;

        /// <summary>迟滞宽度（米）：退出某个状态需要的额外距离。</summary>
        public float hysteresis = 0.7f;

        /// <summary>逃跑最短持续时间（秒）。</summary>
        public float fleeMinTime = 1.6f;

        /// <summary>静置多久睡着（秒）。</summary>
        public float sleepAfter = 20f;

        /// <summary>被玩家正对时，判定半径的放大倍数。</summary>
        public float watchedRadiusScale = 1.35f;

        /// <summary>是否允许睡着。</summary>
        public bool canSleep = true;

        /// <summary>当前状态。</summary>
        public CatMood Mood { get; private set; }

        /// <summary>当前状态已持续时间（秒）。</summary>
        public float TimeInMood { get; private set; }

        /// <summary>一次性动作剩余时间（秒），>0 表示正被锁定。</summary>
        public float LockRemaining { get { return _lockTimer; } }

        /// <summary>是否正在播放一次性动作。</summary>
        public bool IsLocked { get { return _lockTimer > 0f; } }

        float _lockTimer;
        float _idleTime;

        public CatBrain()
        {
            Reset();
        }

        /// <summary>回到 Idle 并清空所有计时。</summary>
        public void Reset()
        {
            Mood = CatMood.Idle;
            TimeInMood = 0f;
            _idleTime = 0f;
            _lockTimer = 0f;
        }

        /// <summary>
        /// 播放一次性动作（Sit / Eat / Drink / Pounce），duration 秒内不受距离影响。
        /// 循环类状态不接受抢占。
        /// </summary>
        public void PlayOneShot(CatMood mood, float duration)
        {
            if (duration <= 0f) return;
            Mood = mood;
            TimeInMood = 0f;
            _lockTimer = duration;
            _idleTime = 0f;
        }

        /// <summary>外部强制切换（例如被玩家吓到）。</summary>
        public void ForceMood(CatMood mood)
        {
            if (mood == Mood && !IsLocked) return;
            Mood = mood;
            TimeInMood = 0f;
            _lockTimer = 0f;
            _idleTime = 0f;
        }

        /// <summary>推进一帧决策。</summary>
        public CatMood Tick(CatSenses senses, float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            TimeInMood += deltaTime;

            if (_lockTimer > 0f)
            {
                _lockTimer -= deltaTime;
                if (_lockTimer > 0f) return Mood;
                _lockTimer = 0f;
                // 一次性动作播完，交还给距离驱动
                SetMood(CatMood.Idle);
                return Mood;
            }

            float scale = senses.playerLookingAtMe ? watchedRadiusScale : 1f;
            float flee = fleeRadius * scale;
            float alert = alertRadius * scale;
            float d = senses.playerDistance;

            CatMood next = Mood;

            switch (Mood)
            {
                case CatMood.Walk:
                    // Walk 用于逃跑/游走，靠近就升级
                    if (d <= flee) next = CatMood.Walk;
                    else if (d <= alert) next = CatMood.Alert;
                    else if (TimeInMood >= fleeMinTime) next = CatMood.Idle;
                    break;

                case CatMood.Alert:
                    if (d <= flee) next = CatMood.Walk;
                    else if (d > alert + hysteresis) next = CatMood.Idle;
                    break;

                case CatMood.Sleep:
                    if (d <= flee) next = CatMood.Walk;
                    else if (d <= alert) next = CatMood.Alert;
                    break;

                default: // Idle
                    if (d <= flee) next = CatMood.Walk;
                    else if (d <= alert) next = CatMood.Alert;
                    else
                    {
                        _idleTime += deltaTime;
                        if (canSleep && _idleTime >= sleepAfter) next = CatMood.Sleep;
                    }
                    break;
            }

            if (next != Mood) SetMood(next);
            return Mood;
        }

        void SetMood(CatMood mood)
        {
            Mood = mood;
            TimeInMood = 0f;
            if (mood != CatMood.Idle) _idleTime = 0f;
        }
    }
}
