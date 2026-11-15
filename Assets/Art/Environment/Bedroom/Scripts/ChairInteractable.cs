using System;
using UnityEngine;

namespace NOCTURNE
{
    /// <summary>
    /// 书桌椅子：在两个预设位置之间平滑移动，不走 Rigidbody。
    ///
    /// 短按 E = 推入 / 拉出；在"拉出"状态下长按 E 才坐下。
    /// 短按与长按严格互斥：长按一旦达成，松开不会再触发短按。
    ///
    /// 交互提示由 FPC 的准星 UI 显示，长按进度由 ChairHoldUI 显示。
    /// 本脚本不直接改玩家控制器的内部状态，只通过事件通知 PlayerSittingController。
    /// </summary>
    [DisallowMultipleComponent]
    public class ChairInteractable : MonoBehaviour, IInteractable
    {
        public enum ChairState { Outside, Inside, Moving }

        [Header("两个预设位置（场景里的空物体）")]
        public Transform outsidePosition;
        public Transform insidePosition;
        [Tooltip("空物体没摆时用这两个世界坐标兜底")]
        public Vector3 outsideFallback = new Vector3(-1.26f, 0f, 0.10f);
        public Vector3 insideFallback = new Vector3(-1.72f, 0f, 0.10f);

        [Header("移动")]
        [Tooltip("移动速度（米/秒）")]
        public float moveSpeed = 0.75f;
        [Tooltip("到位后的旋转（度），一般不填")]
        public Vector3 targetEuler = Vector3.zero;
        [Tooltip("跟着椅子一起动的子物体（碰撞体等）")]
        public Transform[] extras;

        [Header("长按坐下")]
        public KeyCode holdKey = KeyCode.E;
        [Tooltip("长按多久算坐下")]
        public float holdDuration = 1.0f;
        [Tooltip("只有拉出状态才允许坐下")]
        public bool sitOnlyWhenOutside = true;
        [Tooltip("玩家坐下控制器，用于坐下期间屏蔽椅子推拉")]
        public PlayerSittingController sitting;

        [Header("交互距离")]
        [Tooltip("超过这个距离就不响应（准星本来也点不到，这里做个兜底）")]
        public float maxInteractDistance = 2.4f;
        [Tooltip("准星大致对准椅子的容差角度（度）。不依赖射线命中，避免低视角点不到椅子")]
        public float maxLookAngle = 65f;

        [Header("音效（可空）")]
        public AudioSource audioSource;
        public AudioClip moveClip;
        public AudioClip sitClip;

        [Header("状态")]
        [SerializeField] ChairState _state = ChairState.Outside;

        public ChairState State { get { return _state; } }
        public bool IsOutside { get { return _state == ChairState.Outside; } }
        public bool IsMoving { get { return _state == ChairState.Moving; } }

        /// <summary>长按进度 0..1，UI 读它画进度条。</summary>
        public float HoldProgress
        {
            get
            {
                if (holdDuration <= 0f) return _holding ? 1f : 0f;
                return Mathf.Clamp01(_holdTime / holdDuration);
            }
        }

        public bool IsHolding { get { return _holding; } }

        /// <summary>椅子状态变化（旧, 新）。</summary>
        public event Action<ChairState, ChairState> StateChanged;
        /// <summary>长按完成，请求玩家坐下。参数是椅子本身。</summary>
        public event Action<ChairInteractable> SitRequested;

        FirstPersonController _fpc;
        Vector3 _from, _to;
        Quaternion _fromRot, _toRot;
        float _moveT, _moveDur = 1f;
        Vector3[] _extraFrom;
        bool _moving;
        bool _targetInside;

        bool _holding;
        float _holdTime;
        bool _holdConsumed;     // 长按已经完成，松开时不能再算短按
        bool _pressed;          // 这一轮 E 已经按下（用于区分短按/长按）

        void Awake()
        {
            _fpc = FindObjectOfType<FirstPersonController>();
            if (sitting == null) sitting = FindObjectOfType<PlayerSittingController>();
            CacheExtras();
        }

        void Start()
        {
            // 初始摆到 outside（或保持场景里的位置）
            var target = outsidePosition != null ? outsidePosition.position : outsideFallback;
            transform.position = new Vector3(target.x, transform.position.y, target.z);
            CacheExtras();
        }

        void CacheExtras()
        {
            if (extras == null) return;
            _extraFrom = new Vector3[extras.Length];
            for (int i = 0; i < extras.Length; i++)
                if (extras[i] != null) _extraFrom[i] = extras[i].position;
        }

        // ---------------------------------------------------------------- 输入

        void Update()
        {
            if (_moving) { TickMove(); return; }

            // 坐着或坐/起切换期间完全不响应椅子推拉，避免按 E 起身时把椅子也推走
            if (sitting != null && (sitting.IsSitting || sitting.IsTransitioning)) { ClearHold(); return; }
            if (_fpc == null || !_fpc.enabled) { ClearHold(); return; }

            if (Input.GetKeyDown(holdKey)) Press();
            if (!_pressed) return;

            if (Input.GetKey(holdKey)) TickHold(Time.deltaTime);
            else Release();
        }

        // ---------------------------------------------------------------- 输入状态机（外部也可驱动）

        /// <summary>按下的那一帧调用。玩家不在交互范围内则忽略。</summary>
        public void Press()
        {
            if (_moving) return;
            if (!IsPlayerNear()) { ClearHold(); return; }
            _pressed = true;
            _holdTime = 0f;
            _holdConsumed = false;
        }

        /// <summary>按住期间每帧调用，累计长按进度。</summary>
        public void TickHold(float dt)
        {
            if (!_pressed) return;
            if (!CanSit) return;                       // 推入状态不能坐下，也不显示进度

            _holdTime += dt;
            _holding = _holdTime > 0.06f;
            if (_holdTime < holdDuration) return;

            _holding = false;
            _holdConsumed = true;
            _pressed = false;
            _holdTime = holdDuration;
            RequestSit();
        }

        /// <summary>松开时调用：没达成长按就是短按，触发推入/拉出。</summary>
        public void Release()
        {
            if (!_pressed) return;

            _pressed = false;
            _holding = false;
            bool wasShort = !_holdConsumed;
            _holdConsumed = false;
            _holdTime = 0f;

            if (wasShort && IsPlayerNear()) ToggleMove();
        }

        void ClearHold()
        {
            _pressed = false;
            _holding = false;
            _holdTime = 0f;
        }

        /// <summary>
        /// 玩家是否在有效交互范围内并大致朝向椅子。
        /// 不用「准星射线命中」判定：本作相机只有 1/3 人高，平视时射线会从椅背上方掠过，
        /// 导致明明站在椅子前面却按 E 没反应。
        /// </summary>
        public bool IsPlayerNear()
        {
            if (_fpc == null) return false;
            Transform cam = _fpc.cameraRig != null ? _fpc.cameraRig : _fpc.transform;
            Vector3 seat = transform.position + Vector3.up * 0.45f;
            Vector3 to = seat - cam.position;
            float dist = to.magnitude;
            if (dist > maxInteractDistance) return false;
            if (dist < 0.25f) return true;          // 贴着椅子就不看角度了
            return Vector3.Angle(cam.forward, to) <= maxLookAngle;
        }

        bool CanSit
        {
            get
            {
                if (sitOnlyWhenOutside && _state != ChairState.Outside) return false;
                return _state == ChairState.Outside;
            }
        }

        void RequestSit()
        {
            if (audioSource != null && sitClip != null) audioSource.PlayOneShot(sitClip);
            if (SitRequested != null) SitRequested(this);
            // 直接调用序列化引用：C# event 的订阅在场景里不会保存，不能只靠事件
            if (sitting == null) sitting = FindObjectOfType<PlayerSittingController>();
            if (sitting != null) sitting.Sit();
        }

        // ---------------------------------------------------------------- IInteractable

        public string Prompt
        {
            get
            {
                if (_state == ChairState.Moving) return "椅子";
                if (_state == ChairState.Inside) return "拉出椅子";
                return "推入椅子";
            }
        }

        public bool CanInteract { get { return !_moving; } }

        /// <summary>
        /// FPC 在按下 E 的瞬间会调用这里。
        /// 短按/长按完全由本脚本的 Update 判定，这里只做兜底：如果 FPC 没读到
        /// CurrentTarget（例如射线被挡住），仍然能推拉椅子。
        /// </summary>
        public void Interact()
        {
            // 交给 Update 的长按/短按判定，避免同一帧双触发
        }

        // ---------------------------------------------------------------- 移动

        public void ToggleMove()
        {
            if (_moving) return;
            // 拉出状态 → 推入；推入状态 → 拉出
            MoveTo(_state != ChairState.Inside);
        }

        public void MoveTo(bool inside)
        {
            if (_moving) return;

            Vector3 targetPos = inside
                ? (insidePosition != null ? insidePosition.position : insideFallback)
                : (outsidePosition != null ? outsidePosition.position : outsideFallback);

            _from = transform.position;
            _to = new Vector3(targetPos.x, transform.position.y, targetPos.z);
            _fromRot = transform.rotation;
            _toRot = Quaternion.Euler(targetEuler);

            float dist = Vector3.Distance(_from, _to);
            _moveDur = moveSpeed > 0.001f ? Mathf.Max(0.12f, dist / moveSpeed) : 0.12f;
            _moveT = 0f;
            _moving = true;
            _targetInside = inside;

            CacheExtras();
            SetState(ChairState.Moving);

            if (audioSource != null && moveClip != null) audioSource.PlayOneShot(moveClip);
        }

        void TickMove()
        {
            _moveT += Time.deltaTime;
            float k = Mathf.Clamp01(_moveT / _moveDur);
            float e = k * k * (3f - 2f * k);

            Vector3 p = Vector3.Lerp(_from, _to, e);
            transform.position = p;
            transform.rotation = Quaternion.Slerp(_fromRot, _toRot, e);

            // extras 跟随位移（保持相对偏移）
            if (extras != null && _extraFrom != null)
            {
                Vector3 delta = p - _from;
                for (int i = 0; i < extras.Length; i++)
                    if (extras[i] != null) extras[i].position = _extraFrom[i] + delta;
            }

            if (k >= 1f)
            {
                _moving = false;
                SetState(_targetInside ? ChairState.Inside : ChairState.Outside);
            }
        }

        void SetState(ChairState s)
        {
            if (_state == s) return;
            var old = _state;
            _state = s;
            if (StateChanged != null) StateChanged(old, s);
        }

        /// <summary>玩家坐下后直接标记为使用中（不移动椅子）。</summary>
        public void ForceState(ChairState s)
        {
            _state = s;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Vector3 o = outsidePosition != null ? outsidePosition.position : outsideFallback;
            Vector3 i = insidePosition != null ? insidePosition.position : insideFallback;
            Gizmos.DrawWireSphere(o, 0.12f);
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(i, 0.12f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(o, i);
        }
    }
}
