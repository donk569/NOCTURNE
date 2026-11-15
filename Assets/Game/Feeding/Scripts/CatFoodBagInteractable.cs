using System.Collections;
using UnityEngine;
using NOCTURNE.Core.Flow;
using NOCTURNE.Interaction;
using NOCTURNE.Core.Audio;

namespace NOCTURNE.Feeding
{
    /// <summary>
    /// 茶几上的猫粮袋：拿起 / 手持跟随 / 倒粮 / 放回。
    ///
    /// 状态：OnTable → Held → Pouring → Held（→ OnTable）
    ///
    /// 为什么手持期间要关掉自己的 Collider：
    /// FPC 的交互射线从相机中心往前打，袋子正好挡在视线上；不关的话永远只能选中袋子，
    /// 倒粮（饭盆）/开门/椅子全都点不到。关掉后准星才能穿过去选中饭盆。
    ///
    /// 放回茶几的判定：手持时按 E，且当前准星没有选中任何可交互物 ——
    /// 看向饭盆时 E 会走"倒入猫粮"，看向门时 E 会开门，都不会误触发放下。
    /// </summary>
    [DisallowMultipleComponent]
    public class CatFoodBagInteractable : MonoBehaviour, IInteractable
    {
        [Header("引用")]
        [Tooltip("手持点（Player/CameraRig/HoldPoint）")]
        public Transform holdPoint;

        [Tooltip("桌面原位。留空则用 Awake 时场景里的位置")]
        public Transform tableAnchor;

        public AudioSource audioSource;
        public AudioClip pickupClip;
        public AudioClip putDownClip;
        public AudioClip pourClip;

        [Header("手持")]
        [Tooltip("跟随手持点的平滑速度，越大越贴合手部（不拖影）")]
        public float followSharpness = 16f;

        [Tooltip("手持时的额外本地旋转（模型正面朝向调整用）")]
        public Vector3 holdLocalEuler = Vector3.zero;

        [Header("倒粮")]
        [Tooltip("倒粮时袋子倾角（度）。90 = 袋口完全朝下")]
        public float tiltAngle = 86f;

        [Tooltip("倾倒到位所需时间（秒）")]
        public float tiltTime = 0.55f;

        [Tooltip("倾倒时在手持位基础上的位置偏移（本地坐标，朝前下方送一点）")]
        public Vector3 pourLocalOffset = new Vector3(0f, -0.05f, 0.12f);

        [Tooltip("每颗猫粮颗粒的发射间隔（秒）")]
        public float emitInterval = 0.065f;

        [Tooltip("每次发射几颗")]
        public int emitPerTick = 3;

        public ParticleSystem pourFx;

        [Header("放回")]
        public float putBackTime = 0.42f;

        [Header("交互范围（茶几）")]
        [Tooltip("茶几上的指定放置区域。留空则用 Awake 时袋子的桌面原位作为范围中心")]
        public Transform placeArea;
        [Tooltip("拾取允许的最大水平距离（玩家根节点 → 放置区）。超出时不显示提示、按 E 也无效")]
        public float pickupRange = 1.8f;
        [Tooltip("放回允许的最大水平距离。超出时按 E 不会把袋子瞬移回桌面")]
        public float putBackRange = 1.6f;
        [Tooltip("按水平面算距离（忽略眼高与桌面高度差），避免抬头低头影响判定")]
        public bool useFlatDistance = true;

        [Header("交互")]
        public KeyCode dropKey = KeyCode.E;

        [Header("状态（只读）")]
        [SerializeField] CatFoodBagState _state = CatFoodBagState.OnTable;

        /// <summary>当前被拿在手上的袋子（全局只允许一个）。</summary>
        public static CatFoodBagInteractable Held { get; private set; }

        public CatFoodBagState State { get { return _state; } }

        Vector3 _tablePos;
        Vector3 _placePos;
        Quaternion _tableRot;
        Vector3 _tableScale;
        Collider _collider;

        bool _returning;
        float _tilt;
        float _tiltTarget;
        CatFoodBowl _pouringBowl;
        Coroutine _pourRoutine;
        FirstPersonController _fpc;

        // ---------------------------------------------------------------- 生命周期

        void Awake()
        {
            _collider = GetComponent<Collider>();

            if (tableAnchor != null)
            {
                _tablePos = tableAnchor.position;
                _tableRot = tableAnchor.rotation;
            }
            else
            {
                _tablePos = transform.position;
                _tableRot = transform.rotation;
            }
            _placePos = placeArea != null ? placeArea.position : _tablePos;
            _tableScale = transform.localScale;

            _fpc = FindObjectOfType<FirstPersonController>();
            SetState(CatFoodBagState.OnTable);
        }

        void OnDisable()
        {
            if (Held == this) Held = null;
            ControlLock.Release(ControlLock.Reason.Pouring, this);
        }

        void OnDestroy()
        {
            if (Held == this) Held = null;
        }

        void Update()
        {
            if (_state != CatFoodBagState.Held || _returning) return;

            // 放回不再是"任意位置按 E 就生效"的全局监听：
            // 必须真的站在茶几的指定放置区域旁边，且准星没有选中别的可交互物。
            if (ControlLock.Blocks(ControlLock.Reason.Pouring)) return;
            if (!PlayerInRange(putBackRange)) return;
            if (!Input.GetKeyDown(dropKey)) return;

            // 看向饭盆 → E 交给饭盆处理（倒入猫粮）
            // 看向别的可交互物 → 交给它（开门、推椅子）
            // 什么都没选中 → 这里把袋子放回茶几
            if (_fpc == null) _fpc = FindObjectOfType<FirstPersonController>();
            if (_fpc == null || _fpc.CurrentTarget == null) ReturnToTable();
        }

        /// <summary>玩家根节点到茶几放置区的距离是否在允许范围内。</summary>
        bool PlayerInRange(float range)
        {
            if (_fpc == null) _fpc = FindObjectOfType<FirstPersonController>();
            if (_fpc == null) return true;   // 没有玩家的调试场景不拦

            Vector3 a = _fpc.transform.position;
            Vector3 b = _placePos;
            if (useFlatDistance) { a.y = 0f; b.y = 0f; }
            return Vector3.Distance(a, b) <= range;
        }

        void OnGUI()
        {
            if (_state != CatFoodBagState.Held || _returning) return;
            if (ControlLock.Blocks(ControlLock.Reason.Pouring)) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (!PlayerInRange(putBackRange)) return;
            if (_fpc == null) _fpc = FindObjectOfType<FirstPersonController>();
            if (_fpc != null && _fpc.CurrentTarget != null) return;

            // FPC 只在 CurrentTarget != null 时画提示，这里正好补上"放回"这一条
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(0.95f, 0.9f, 0.78f, 0.9f);
            GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.5f + 26f, 240f, 28f),
                      "[E] 放回猫粮", style);
        }

        // ---------------------------------------------------------------- 手持跟随

        void LateUpdate()
        {
            if (_state == CatFoodBagState.OnTable || _returning) return;
            if (holdPoint == null) return;

            float k = 1f - Mathf.Exp(-Mathf.Max(1f, followSharpness) * Time.deltaTime);

            // 倾倒角度平滑推进
            float rate = tiltAngle / Mathf.Max(0.05f, tiltTime);
            _tilt = Mathf.MoveTowards(_tilt, _tiltTarget, rate * Time.deltaTime);

            Vector3 targetPos = holdPoint.position;
            Quaternion targetRot = holdPoint.rotation * Quaternion.Euler(holdLocalEuler);

            if (_tilt > 0.01f && _pouringBowl != null && _pouringBowl.pourAnchor != null)
            {
                // 倒粮时把袋子往前下方送，袋口对准盆口上方
                targetPos = holdPoint.TransformPoint(pourLocalOffset);
                Vector3 toBowl = _pouringBowl.pourAnchor.position - targetPos;
                if (toBowl.sqrMagnitude > 0.0004f)
                {
                    Quaternion face = Quaternion.LookRotation(toBowl.normalized, Vector3.up);
                    targetRot = Quaternion.Slerp(targetRot, face, 0.35f);
                }
            }

            targetRot *= Quaternion.Euler(_tilt, 0f, 0f);

            transform.position = Vector3.Lerp(transform.position, targetPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
        }

        // ---------------------------------------------------------------- IInteractable

        public string Prompt { get { return "拿起猫粮"; } }

        public bool CanInteract
        {
            get
            {
                // FPC 的射线负责"看着它"，这里负责"人在茶几旁边"。两者同时满足才有提示。
                return _state == CatFoodBagState.OnTable && Held == null && PlayerInRange(pickupRange);
            }
        }

        public void Interact()
        {
            if (!PlayerInRange(pickupRange)) return;
            PickUp();
        }

        // ---------------------------------------------------------------- 拿起 / 放回

        public void PickUp()
        {
            if (_state != CatFoodBagState.OnTable) return;
            if (Held != null && Held != this) return;    // 同时只能拿一袋

            Held = this;
            _returning = false;
            _tilt = 0f;
            _tiltTarget = 0f;

            // 关掉碰撞体：否则手持时准星永远只选中袋子（见类注释）
            if (_collider == null) _collider = GetComponent<Collider>();
            if (_collider != null) _collider.enabled = false;

            SetState(CatFoodBagState.Held);

            AudioService.PlayAt(transform, SfxId.BlanketRustle, SoundGroup.Sfx, 0.4f);
        }

        public void ReturnToTable() { ReturnToTable(false); }

        /// <summary>
        /// 放回茶几。force=true 只给剧情/自测直调用；玩家输入一律走距离守卫，
        /// 否则站在客厅另一头按 E 也会让袋子凭空飞回桌面。
        /// </summary>
        public void ReturnToTable(bool force)
        {
            if (_state == CatFoodBagState.OnTable || _returning) return;
            if (!force && !PlayerInRange(putBackRange)) return;
            StartCoroutine(ReturnRoutine());
        }

        IEnumerator ReturnRoutine()
        {
            _returning = true;
            _tiltTarget = 0f;
            _pouringBowl = null;

            Vector3 from = transform.position;
            Quaternion fromRot = transform.rotation;
            float t = 0f;
            float dur = Mathf.Max(0.08f, putBackTime);

            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                transform.position = Vector3.Lerp(from, _tablePos, k);
                transform.rotation = Quaternion.Slerp(fromRot, _tableRot, k);
                yield return null;
            }

            transform.position = _tablePos;
            transform.rotation = _tableRot;
            transform.localScale = _tableScale;

            if (_collider != null) _collider.enabled = true;
            if (Held == this) Held = null;

            _returning = false;
            _tilt = 0f;
            SetState(CatFoodBagState.OnTable);

            AudioService.PlayAt(transform, SfxId.BlanketRustle, SoundGroup.Sfx, 0.4f);
        }

        // ---------------------------------------------------------------- 长按倾倒（左键）

        /// <summary>当前倾斜到多少度（自测/UI 用）。</summary>
        public float TiltDegrees { get { return _tilt; } }

        /// <summary>
        /// 按住鼠标左键的瞬间调用：进入"预备倾倒"，袋子开始跟着长按进度倾斜。
        /// 注意这里故意不锁交互 —— 玩家随时可以松手取消。
        /// </summary>
        public bool BeginPourHold(CatFoodBowl bowl)
        {
            if (_state != CatFoodBagState.Held || bowl == null) return false;
            if (!bowl.CanAcceptFood) return false;
            if (_pourRoutine != null) return false;

            _pouringBowl = bowl;
            _tiltTarget = 0f;
            SetState(CatFoodBagState.Pouring);
            return true;
        }

        /// <summary>长按进度 0~1 直接映射到倾角：进度条走完 = 袋子刚好倒到位。</summary>
        public void SetPourHoldProgress(float k)
        {
            if (_state != CatFoodBagState.Pouring || _pourRoutine != null) return;
            _tiltTarget = Mathf.Clamp01(k) * tiltAngle;
        }

        /// <summary>松手但没到时间，或离开范围：回正，什么都不留下。</summary>
        public void CancelPourHold()
        {
            if (_state != CatFoodBagState.Pouring || _pourRoutine != null) return;

            _tiltTarget = 0f;
            _pouringBowl = null;
            SetState(CatFoodBagState.Held);
            ControlLock.Release(ControlLock.Reason.Pouring, this);
        }

        /// <summary>长按达标：此时袋子通常已经倾到位，直接开始撒粮。</summary>
        public void CommitPour(CatFoodBowl bowl)
        {
            if (_state != CatFoodBagState.Pouring || bowl == null) return;
            if (_pourRoutine != null) return;
            _pourRoutine = StartCoroutine(CommitRoutine(bowl));
        }

        IEnumerator CommitRoutine(CatFoodBowl bowl)
        {
            _pouringBowl = bowl;
            ControlLock.Acquire(ControlLock.Reason.Pouring, this);

            _tiltTarget = tiltAngle;

            // 长按阶段已经把袋子倾到位了，这里只补剩下的角度差，不再干等一个 tiltTime
            float rate = tiltAngle / Mathf.Max(0.05f, tiltTime);
            float need = (tiltAngle - _tilt) / Mathf.Max(1f, rate);
            if (need > 0.02f) yield return new WaitForSeconds(need);

            bowl.Fill();

            AudioService.PlayAt(transform, SfxId.KibblePour, SoundGroup.Sfx, 0.45f);

            float emitTime = Mathf.Max(0.2f, bowl.fillDuration);
            float t = 0f;
            while (t < emitTime)
            {
                if (pourFx != null) pourFx.Emit(Mathf.Max(1, emitPerTick));
                t += emitInterval;
                yield return new WaitForSeconds(emitInterval);
            }

            _tiltTarget = 0f;
            yield return new WaitForSeconds(tiltTime * 0.8f);

            _pouringBowl = null;
            SetState(CatFoodBagState.Held);

            ControlLock.Release(ControlLock.Reason.Pouring, this);
            _pourRoutine = null;
        }

        // ---------------------------------------------------------------- 倒粮（旧接口，剧情/自测直调）

        /// <summary>由饭盆（或剧情）调用：倾斜 → 撒颗粒 → 盆里长粮 → 回正。</summary>
        public void Pour(CatFoodBowl bowl)
        {
            if (_state != CatFoodBagState.Held || bowl == null) return;
            if (!bowl.CanAcceptFood) return;
            if (_pourRoutine != null) return;

            _pourRoutine = StartCoroutine(PourRoutine(bowl));
        }

        IEnumerator PourRoutine(CatFoodBowl bowl)
        {
            SetState(CatFoodBagState.Pouring);
            _pouringBowl = bowl;

            // 倒粮期间屏蔽其他交互（E 不响应门/椅子），移动保持可用
            ControlLock.Acquire(ControlLock.Reason.Pouring, this);

            _tiltTarget = tiltAngle;
            yield return new WaitForSeconds(tiltTime);

            // 袋口到位：盆里开始长粮，同时撒颗粒
            bowl.Fill();

            AudioService.PlayAt(transform, SfxId.KibblePour, SoundGroup.Sfx, 0.45f);

            float emitTime = Mathf.Max(0.2f, bowl.fillDuration);
            float t = 0f;
            while (t < emitTime)
            {
                if (pourFx != null) pourFx.Emit(Mathf.Max(1, emitPerTick));
                t += emitInterval;
                yield return new WaitForSeconds(emitInterval);
            }

            // 回正
            _tiltTarget = 0f;
            yield return new WaitForSeconds(tiltTime * 0.8f);

            _pouringBowl = null;
            SetState(CatFoodBagState.Held);

            ControlLock.Release(ControlLock.Reason.Pouring, this);
            _pourRoutine = null;
        }

        void SetState(CatFoodBagState next)
        {
            _state = next;
        }
    }
}
