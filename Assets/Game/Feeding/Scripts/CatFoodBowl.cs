using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NOCTURNE.Feeding
{
    /// <summary>
    /// 猫饭盆：管食物状态与视觉变化。
    ///
    /// 状态机（唯一真相）
    ///   Empty → Filled → Eating → EmptyAfterEating
    ///   · 初始一定是 Empty，Awake 里把食物视觉整体关掉（场景里可以直接摆成有粮的样子做美术参考）
    ///   · 只有 Filled 允许 <see cref="BeginEating"/>
    ///   · 吃到一半的状态仍是 Eating，逻辑上"有食物"，猫不会走开
    ///   · 吃完 → EmptyAfterEating，食物视觉完全隐藏，逻辑与画面同步
    ///
    /// 视觉渐变的做法：不生成/销毁对象。猫粮颗粒与粮堆都是场景里预先做好的，
    /// 这里只按 0~1 的"食物量"改激活数量与粮堆缩放。颗粒按离盆心距离升序排列，
    /// 所以变少时先消失的是外圈、后消失的是盆底中心，和真实进食顺序一致。
    ///
    /// 倒粮交互也挂在这里：玩家拿着猫粮袋看向饭盆时，本物体就是可交互目标。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatFoodBowl : MonoBehaviour, IInteractable, IInteractPrompt
    {
        [Header("食物视觉")]
        [Tooltip("食物根节点（CatFood）。空盆时整体隐藏")]
        public Transform foodVisualRoot;

        [Tooltip("粮堆（随食物量缩放）")]
        public Transform mound;

        [Tooltip("猫粮颗粒，按离盆心距离升序排列（Awake 里自动排序）")]
        public Transform[] kibbles = new Transform[0];

        [Tooltip("粮堆满量时的缩放，吃空时按比例缩到 20%")]
        public Vector3 moundFullScale = Vector3.one;

        [Tooltip("食物量低于这个值就把整个食物根节点隐藏")]
        public float hideThreshold = 0.02f;

        [Header("位置点")]
        [Tooltip("猫进食站位（盆前 0.3m 左右）")]
        public Transform eatingPoint;

        [Tooltip("接近点（可选，只为让路线更自然，不填也能寻路）")]
        public Transform approachPoint;

        [Tooltip("倒粮视觉参考点（盆口上方）")]
        public Transform pourAnchor;

        [Header("节奏")]
        [Tooltip("猫粮从无到满的时长（秒），与倒粮动作对齐")]
        public float fillDuration = 1.7f;

        [Tooltip("猫把一盆吃完的时长（秒）")]
        public float eatDuration = 10f;

        [Header("音效 / 特效")]
        public AudioSource audioSource;
        public AudioClip fillClip;

        [Header("倒粮（长按左键）")]
        [Tooltip("长按多久才真正倒粮（秒）。中途松手就取消")]
        public float pourHoldSeconds = 1.1f;

        [Tooltip("玩家离饭盆多近才允许倒粮（米）")]
        public float pourRange = 1.7f;

        [Tooltip("倒粮过程中显示进度条")]
        public bool showPourProgress = true;

        [Header("状态（只读）")]
        [SerializeField] CatFoodBowlState _state = CatFoodBowlState.Empty;

        public event Action<CatFoodBowlState, CatFoodBowlState> StateChanged;

        public CatFoodBowlState State { get { return _state; } }

        /// <summary>逻辑上是否有食物（Filled / Eating 都算）。</summary>
        public bool HasFood
        {
            get { return _state == CatFoodBowlState.Filled || _state == CatFoodBowlState.Eating; }
        }

        /// <summary>能否再倒粮进去。</summary>
        public bool CanAcceptFood
        {
            get { return _state == CatFoodBowlState.Empty || _state == CatFoodBowlState.EmptyAfterEating; }
        }

        /// <summary>0~1 的食物量，供调试与 UI 使用。</summary>
        public float FoodAmount { get; private set; }

        Coroutine _routine;
        Transform[] _sorted;
        int _lastVisible = -1;

        float _pourHold;
        bool _holding;
        FirstPersonController _fpc;
        bool _hintVisible;

        // ---------------------------------------------------------------- 生命周期

        void Awake()
        {
            if (foodVisualRoot == null)
            {
                if (mound != null) foodVisualRoot = mound.parent;
                else if (kibbles != null && kibbles.Length > 0) foodVisualRoot = kibbles[0].parent;
            }

            if (moundFullScale == Vector3.one && mound != null) moundFullScale = mound.localScale;

            BuildSortedKibbles();

            // 初始为空盆：食物视觉整体隐藏，盆底/盆壁保持可见
            ApplyAmount(0f);
            SetState(CatFoodBowlState.Empty, true);
        }

        void OnDestroy()
        {
            StateChanged = null;
        }

        /// <summary>按离食物根节点的水平距离升序排列，吃的时候先掉外圈的。</summary>
        void BuildSortedKibbles()
        {
            if (kibbles == null || kibbles.Length == 0)
            {
                _sorted = new Transform[0];
                return;
            }

            Vector3 center = foodVisualRoot != null ? foodVisualRoot.position : transform.position;

            var list = new List<Transform>();
            for (int i = 0; i < kibbles.Length; i++)
                if (kibbles[i] != null) list.Add(kibbles[i]);

            list.Sort(delegate (Transform a, Transform b)
            {
                float da = FlatSqr(a.position, center);
                float db = FlatSqr(b.position, center);
                return da.CompareTo(db);
            });

            _sorted = list.ToArray();
        }

        static float FlatSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        // ---------------------------------------------------------------- 对外：食物

        /// <summary>倒粮：食物从无到有。已在 Filled 或 Eating 时忽略。</summary>
        public void Fill()
        {
            if (!CanAcceptFood) return;

            StopRoutine();
            _routine = StartCoroutine(FillRoutine());
        }

        /// <summary>猫开始进食：返回 false 表示盆里没有食物。</summary>
        public bool BeginEating(float durationOverride = -1f)
        {
            if (_state != CatFoodBowlState.Filled) return false;

            StopRoutine();

            float dur = durationOverride > 0f ? durationOverride : eatDuration;
            _routine = StartCoroutine(EatRoutine(Mathf.Max(1f, dur)));
            return true;
        }

        /// <summary>直接清空（重新开始流程 / 调试用）。</summary>
        public void ResetBowl()
        {
            StopRoutine();
            ApplyAmount(0f);
            SetState(CatFoodBowlState.Empty, true);
        }

        void StopRoutine()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
        }

        IEnumerator FillRoutine()
        {
            if (audioSource != null && fillClip != null) audioSource.PlayOneShot(fillClip, 0.35f);

            float t = 0f;
            float dur = Mathf.Max(0.15f, fillDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                ApplyAmount(Mathf.Clamp01(t / dur));
                yield return null;
            }

            ApplyAmount(1f);
            _routine = null;

            // 逻辑到 Filled 的同时画面已经满了 —— 猫这时候才会出发
            SetState(CatFoodBowlState.Filled);
        }

        IEnumerator EatRoutine(float duration)
        {
            SetState(CatFoodBowlState.Eating);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                ApplyAmount(1f - Mathf.Clamp01(t / duration));
                yield return null;
            }

            ApplyAmount(0f);
            _routine = null;

            // 逻辑与画面同时归零
            SetState(CatFoodBowlState.EmptyAfterEating);
        }

        /// <summary>
        /// amount 0~1：粮堆缩放到 20%~100%，颗粒按比例显示前 N 颗。
        /// 只在可见数量变化时写 Transform，避免每帧几十次 SetActive。
        /// </summary>
        void ApplyAmount(float amount)
        {
            FoodAmount = Mathf.Clamp01(amount);

            bool any = FoodAmount > hideThreshold;
            if (foodVisualRoot != null && foodVisualRoot.gameObject.activeSelf != any)
                foodVisualRoot.gameObject.SetActive(any);

            if (mound != null)
            {
                float k = Mathf.Lerp(0.2f, 1f, FoodAmount);
                mound.localScale = new Vector3(moundFullScale.x * k,
                                               moundFullScale.y * Mathf.Lerp(0.15f, 1f, FoodAmount),
                                               moundFullScale.z * k);
            }

            if (_sorted == null) return;

            int visible = Mathf.Clamp(Mathf.RoundToInt(FoodAmount * _sorted.Length), 0, _sorted.Length);
            if (visible == _lastVisible) return;

            for (int i = 0; i < _sorted.Length; i++)
            {
                if (_sorted[i] == null) continue;
                bool on = i < visible;
                if (_sorted[i].gameObject.activeSelf != on) _sorted[i].gameObject.SetActive(on);
            }
            _lastVisible = visible;
        }

        void SetState(CatFoodBowlState next, bool force = false)
        {
            if (_state == next && !force) return;
            var old = _state;
            _state = next;
            if (StateChanged != null && (int)old >= 0) StateChanged(old, next);
        }

        // ---------------------------------------------------------------- 倒粮交互

        /// <summary>
        /// 拿着猫粮袋并看向饭盆时，本物体就是 FPC 的可交互目标。
        /// 空手时返回 false，这样准星不会提示"倒入猫粮"，同时也让猫粮袋能靠
        /// "没有可交互目标"这一条来判定"放回茶几"。
        /// </summary>
        public bool CanInteract
        {
            get
            {
                var bag = CatFoodBagInteractable.Held;
                return bag != null && bag.State == CatFoodBagState.Held && CanAcceptFood;
            }
        }

        public string Prompt { get { return "倒入猫粮"; } }

        /// <summary>准星提示走整句自定义文案（长按左键，不是 E）。</summary>
        public string FullPrompt
        {
            get { return _pourHold > 0f ? "倒粮中…" : "长按左键倒入猫粮"; }
        }

        /// <summary>
        /// E 在饭盆上不做任何事。
        ///
        /// 倒粮改成"长按鼠标左键"之后，这里必须保持可交互（CanInteract 为真），
        /// 否则 FPC 的准星目标会变成 null，猫粮袋会误以为玩家想把它放回茶几。
        /// </summary>
        public void Interact() { }

        // ---------------------------------------------------------------- 长按倒粮

        void Update()
        {
            TickPourHold(Input.GetMouseButton(0), Time.deltaTime);
        }

        /// <summary>
        /// 长按累积。抽出来是为了让自测能确定性地驱动它（编辑器里没有真实的鼠标按住）。
        /// 按下那一刻就让猫粮袋开始倾斜，倾角跟着进度走 —— 进度条走完，袋子刚好倒到位。
        /// </summary>
        void TickPourHold(bool pressed, float dt)
        {
            var bag = CatFoodBagInteractable.Held;

            bool ready = bag != null
                         && (bag.State == CatFoodBagState.Held || bag.State == CatFoodBagState.Pouring)
                         && CanAcceptFood
                         && PlayerInRange();

            _hintVisible = ready;

            if (!ready)
            {
                ReleaseHold(bag);
                return;
            }

            if (!pressed)
            {
                ReleaseHold(bag);   // 松手 = 取消（未达标则回正，什么都不留下）
                return;
            }

            if (!_holding)
            {
                _holding = bag.BeginPourHold(this);
                if (!_holding) return;
            }

            _pourHold += dt;
            float k = Mathf.Clamp01(_pourHold / Mathf.Max(0.1f, pourHoldSeconds));
            bag.SetPourHoldProgress(k);

            if (k >= 1f)
            {
                _pourHold = 0f;
                _holding = false;
                bag.CommitPour(this);
            }
        }

        void ReleaseHold(CatFoodBagInteractable bag)
        {
            if (_holding && bag != null) bag.CancelPourHold();
            _holding = false;
            _pourHold = 0f;
        }

        /// <summary>自测专用：模拟按住 seconds 秒。</summary>
        public void DebugHoldPour(float seconds)
        {
            float left = Mathf.Max(0f, seconds);
            while (left > 0f)
            {
                float step = Mathf.Min(0.1f, left);
                TickPourHold(true, step);
                left -= step;
            }
        }

        /// <summary>自测专用：模拟松手。</summary>
        public void DebugReleasePour()
        {
            TickPourHold(false, 0f);
        }

        /// <summary>当前长按进度 0~1（自测/UI 用）。</summary>
        public float PourHoldProgress
        {
            get { return Mathf.Clamp01(_pourHold / Mathf.Max(0.1f, pourHoldSeconds)); }
        }

        /// <summary>正在长按倒粮。</summary>
        public bool IsPourHolding { get { return _holding; } }

        bool PlayerInRange()
        {
            if (_fpc == null) _fpc = FindObjectOfType<FirstPersonController>();
            Transform eye = _fpc != null && _fpc.cameraRig != null
                ? _fpc.cameraRig
                : (Camera.main != null ? Camera.main.transform : null);
            if (eye == null) return true;
            return Vector3.Distance(eye.position, transform.position) <= pourRange;
        }

        void OnGUI()
        {
            if (!showPourProgress || !_hintVisible) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;

            // 文字提示统一由 FPC 那一行画（"[E] xxx" / "长按左键倒入猫粮"），
            // 这里只补一条进度条，并且放在提示文字下方，避免两行字叠在一起。
            float k = Mathf.Clamp01(_pourHold / Mathf.Max(0.1f, pourHoldSeconds));
            float w = 190f, h = 5f;
            float x = Screen.width * 0.5f - w * 0.5f;
            float y = Screen.height * 0.5f + 60f;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

            if (k > 0f)
            {
                GUI.color = new Color(1f, 0.82f, 0.45f, 0.95f);
                GUI.DrawTexture(new Rect(x, y, w * k, h), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
