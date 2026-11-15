using UnityEngine;
using NOCTURNE.Core.Flow;

namespace NOCTURNE
{
    /// <summary>
    /// 玩家交互的唯一执行者：准星射线 → <see cref="IInteractable"/> → 提示绘制 → 按键触发。
    ///
    /// 从 <see cref="FirstPersonController"/> 拆出（A10），原因：FPC 已经同时管移动、视角、
    /// 蹲下、手电、头部起伏，交互再塞进去就要变成第二个 CatAgent。
    /// 拆出后 FPC 只保留一个转发属性 <c>CurrentTarget</c>，外部读取方零改动。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("留空则自动取本物体上的 FirstPersonController")]
        public FirstPersonController fpc;

        [Header("交互")]
        [Tooltip("准星射线长度（米）")]
        public float interactDistance = 2.2f;

        [Tooltip("射线检测层。默认全部层")]
        public LayerMask interactMask = ~0;

        public KeyCode interactKey = KeyCode.E;

        [Header("显示")]
        [Tooltip("是否绘制准星与交互提示")]
        public bool drawCrosshair = true;

        /// <summary>当前准星指向的可交互物。每帧由 <see cref="Scan"/> 刷新。</summary>
        public IInteractable CurrentTarget { get; private set; }

        Transform _fallbackOrigin;

        Transform Origin
        {
            get
            {
                if (fpc != null && fpc.cameraRig != null) return fpc.cameraRig;
                if (_fallbackOrigin == null && Camera.main != null) _fallbackOrigin = Camera.main.transform;
                return _fallbackOrigin;
            }
        }

        /// <summary>光标锁定时才响应交互（与 FPC 的锁定状态保持一致）。</summary>
        bool InputArmed
        {
            get { return fpc != null ? fpc.CursorLocked : Cursor.lockState == CursorLockMode.Locked; }
        }

        void Awake()
        {
            if (fpc == null) fpc = GetComponent<FirstPersonController>();
        }

        void Update()
        {
            Scan();
        }

        void Scan()
        {
            CurrentTarget = null;

            // 有任何系统登记了"控制权"（倒粮 / 手持 / 过场…）时，准星不再选中任何东西。
            // 这里查表而不是让别人改 interactDistance —— 后者一旦漏还原就会永久坏掉。
            if (ControlLock.BlocksAny) return;

            if (!InputArmed) return;

            var origin = Origin;
            if (origin == null) return;

            if (Physics.Raycast(new Ray(origin.position, origin.forward), out RaycastHit hit,
                    interactDistance, interactMask, QueryTriggerInteraction.Collide))
            {
                var target = hit.collider.GetComponentInParent<IInteractable>();
                if (target != null && target.CanInteract) CurrentTarget = target;
            }

            if (CurrentTarget != null && Input.GetKeyDown(interactKey))
                CurrentTarget.Interact();
        }

        // ---------------------------------------------------------------- 准星与提示

        void OnGUI()
        {
            if (!drawCrosshair || !InputArmed) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(0.85f, 0.85f, 0.8f, 0.55f);

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 6f, cy - 12f, 12f, 24f), "+", style);

            if (CurrentTarget != null)
            {
                style.normal.textColor = new Color(0.95f, 0.9f, 0.78f, 0.9f);
                style.fontSize = 15;
                // 绝大多数物体是 E 触发；猫碗走长按左键，自己给整句文案
                var custom = CurrentTarget as IInteractPrompt;
                string text = custom != null ? custom.FullPrompt : "[E] " + CurrentTarget.Prompt;
                GUI.Label(new Rect(cx - 120f, cy + 26f, 240f, 28f), text, style);
            }
        }
    }
}
