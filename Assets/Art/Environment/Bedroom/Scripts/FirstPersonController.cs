using UnityEngine;

namespace NOCTURNE
{
    /// <summary>
    /// 第一人称探索控制器（恐怖游戏手感：慢速、有重量、轻微呼吸晃动）。
    /// 挂在一个带 CharacterController 的空物体（玩家脚下原点），
    /// 把相机放在子物体 cameraRig 上，eyeHeight 控制视点高度。
    ///
    /// 操作：WASD 移动 / Shift 快走 / Ctrl 蹲下 / 空格跳（默认关闭）
    ///       E 交互 / F 手电 / Esc 解锁鼠标 / 左键锁定鼠标
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("相机所在的子物体，负责俯仰和头部晃动")]
        public Transform cameraRig;

        [Header("移动速度 m/s")]
        public float walkSpeed = 1.75f;
        public float sprintSpeed = 3.15f;
        public float crouchSpeed = 0.95f;
        [Tooltip("加速/减速，越小越「重」")]
        public float acceleration = 9f;

        [Header("视角")]
        public float mouseSensitivity = 2.2f;
        public float pitchLimit = 82f;
        [Tooltip("开关：Y 轴反向")]
        public bool invertY = false;

        [Header("身高")]
        public float standHeight = 1.72f;
        public float crouchHeight = 1.10f;
        public float standEyeHeight = 1.60f;
        public float crouchEyeHeight = 1.02f;
        public float crouchSpeedLerp = 9f;

        [Header("跳跃")]
        public bool canJump = false;
        public float jumpHeight = 0.85f;
        [Tooltip("落地后多久才能再跳")]
        public float jumpCooldown = 0.15f;

        [Header("重力")]
        public float gravity = -18f;
        public float terminalVelocity = -28f;

        [Header("头部晃动")]
        public float bobAmplitude = 0.028f;
        public float bobFrequency = 8.6f;
        [Tooltip("静止时的呼吸幅度")]
        public float breatheAmplitude = 0.006f;
        public float breatheFrequency = 1.15f;
        [Tooltip("镜头侧倾（走路时轻微左右摆）")]
        public float swayRoll = 0.45f;

        [Header("脚步")]
        [Tooltip("走多远算一步（米）")]
        public float stepDistance = 0.72f;
        public AudioSource footstepSource;
        public AudioClip[] footstepClips;
        [Range(0f, 0.5f)] public float footstepVolume = 0.35f;

        [Header("手电")]
        public bool startWithFlashlight = true;
        public Light flashlight;
        public float flashlightRange = 9f;
        public float flashlightAngle = 46f;
        public float flashlightIntensity = 2.1f;

        [Header("交互")]
        public float interactDistance = 2.2f;
        public LayerMask interactMask = ~0;
        public KeyCode interactKey = KeyCode.E;
        public KeyCode flashlightKey = KeyCode.F;

        /// <summary>当前准星指向的可交互物，UI 可以读它显示提示。</summary>
        public IInteractable CurrentTarget { get; private set; }

        /// <summary>每次落步触发一次，参数是当时的水平速度。接音效/震动/事件用。</summary>
        public event System.Action<float> Footstep;

        CharacterController _cc;
        Vector3 _velocity;          // 水平速度 + 竖直速度打包
        float _yaw, _pitch;
        float _targetHeight, _targetEye;
        float _bobPhase, _breathePhase, _stepAccum, _jumpTimer;
        float _flashlightTarget, _flashlightCurrent;
        bool _cursorLocked;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (cameraRig == null && Camera.main != null) cameraRig = Camera.main.transform;

            _cc.height = standHeight;
            _cc.center = new Vector3(0f, standHeight * 0.5f, 0f);
            _cc.radius = 0.28f;
            _cc.stepOffset = 0.32f;
            _cc.slopeLimit = 46f;
            _cc.skinWidth = 0.02f;

            _targetHeight = standHeight;
            _targetEye = standEyeHeight;

            _yaw = transform.eulerAngles.y;
            _pitch = 0f;

            if (cameraRig != null)
            {
                var e = cameraRig.localEulerAngles;
                cameraRig.localEulerAngles = new Vector3(0f, 0f, e.z);
                cameraRig.localPosition = new Vector3(0f, standEyeHeight, 0f);
            }

            BuildFlashlightIfNeeded();
            _flashlightCurrent = startWithFlashlight ? 1f : 0f;
            _flashlightTarget = _flashlightCurrent;
            if (flashlight != null) flashlight.enabled = startWithFlashlight;

            LockCursor(true);
        }

        void Update()
        {
            HandleCursor();
            if (_cursorLocked)
            {
                Look();
                Move();
            }
            HandleCrouch();
            HandleFlashlight();
            HandleInteraction();
            HeadBob();
        }

        // ---------------------------------------------------------------- 视角

        void Look()
        {
            float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            if (invertY) my = -my;

            _yaw += mx;
            _pitch = Mathf.Clamp(_pitch - my, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraRig != null)
                cameraRig.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
            else if (Input.GetMouseButtonDown(0) && !_cursorLocked) LockCursor(true);
        }

        void LockCursor(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ---------------------------------------------------------------- 移动

        void Move()
        {
            float ix = Input.GetAxisRaw("Horizontal");
            float iz = Input.GetAxisRaw("Vertical");
            Vector3 wish = transform.right * ix + transform.forward * iz;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool crouching = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            bool sprinting = Input.GetKey(KeyCode.LeftShift) && !crouching;

            float speed = crouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);
            Vector3 target = wish * speed;

            // 空中不让改向太多，落地才全权控制
            float accel = _cc.isGrounded ? acceleration : acceleration * 0.35f;
            Vector3 horiz = new Vector3(_velocity.x, 0f, _velocity.z);
            horiz = Vector3.MoveTowards(horiz, target, accel * Time.deltaTime);

            // 竖直
            if (_cc.isGrounded)
            {
                if (_velocity.y < -1f) { _velocity.y = -1f; OnLand(); }
                _jumpTimer -= Time.deltaTime;
                if (canJump && _jumpTimer <= 0f && Input.GetKeyDown(KeyCode.Space))
                {
                    _velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
                    _jumpTimer = jumpCooldown;
                }
            }
            _velocity.y = Mathf.Max(_velocity.y + gravity * Time.deltaTime, terminalVelocity);

            _velocity.x = horiz.x;
            _velocity.z = horiz.z;

            var flags = _cc.Move(_velocity * Time.deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f) _velocity.y = 0f;

            AccumulateSteps(new Vector2(horiz.x, horiz.z).magnitude);
        }

        void OnLand()
        {
            // 落地的下沉感，一次就够
            _bobPhase = 0f;
        }

        void AccumulateSteps(float planarSpeed)
        {
            if (!_cc.isGrounded || planarSpeed < 0.15f) return;
            _stepAccum += planarSpeed * Time.deltaTime;
            if (_stepAccum < stepDistance) return;
            _stepAccum = 0f;

            Footstep?.Invoke(planarSpeed);

            if (footstepSource != null && footstepClips != null && footstepClips.Length > 0)
            {
                var clip = footstepClips[Random.Range(0, footstepClips.Length)];
                if (clip != null)
                {
                    footstepSource.pitch = Random.Range(0.92f, 1.08f);
                    footstepSource.PlayOneShot(clip, footstepVolume);
                }
            }
        }

        void HandleCrouch()
        {
            bool crouching = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            _targetHeight = crouching ? crouchHeight : standHeight;
            _targetEye = crouching ? crouchEyeHeight : standEyeHeight;

            float h = Mathf.Lerp(_cc.height, _targetHeight, crouchSpeedLerp * Time.deltaTime);
            if (Mathf.Abs(h - _cc.height) > 0.0005f)
            {
                _cc.height = h;
                _cc.center = new Vector3(0f, h * 0.5f, 0f);
            }
        }

        // ---------------------------------------------------------------- 头部晃动

        void HeadBob()
        {
            if (cameraRig == null) return;

            Vector3 horiz = new Vector3(_velocity.x, 0f, _velocity.z);
            float planarSpeed = horiz.magnitude;
            bool moving = _cc.isGrounded && planarSpeed > 0.15f;

            _breathePhase += Time.deltaTime * breatheFrequency * Mathf.PI * 2f;

            Vector3 offset;
            float roll;
            if (moving)
            {
                float rate = bobFrequency * Mathf.Clamp(planarSpeed / walkSpeed, 0.6f, 1.6f);
                _bobPhase += Time.deltaTime * rate;
                float y = Mathf.Sin(_bobPhase * Mathf.PI * 2f) * bobAmplitude;
                float x = Mathf.Cos(_bobPhase * Mathf.PI) * bobAmplitude * 0.45f;
                roll = Mathf.Cos(_bobPhase * Mathf.PI) * swayRoll;
                offset = new Vector3(x, y, 0f);
            }
            else
            {
                // 呼吸：竖直轻微起伏
                offset = new Vector3(0f, Mathf.Sin(_breathePhase) * breatheAmplitude, 0f);
                roll = 0f;
            }

            float eye = Mathf.Lerp(cameraRig.localPosition.y, _targetEye, crouchSpeedLerp * Time.deltaTime);
            cameraRig.localPosition = new Vector3(offset.x, eye + offset.y, 0f);
            cameraRig.localRotation = Quaternion.Euler(_pitch, 0f, roll);
        }

        // ---------------------------------------------------------------- 手电

        void BuildFlashlightIfNeeded()
        {
            if (flashlight != null) return;
            if (cameraRig == null) return;

            var go = new GameObject("Flashlight");
            go.transform.SetParent(cameraRig, false);
            go.transform.localPosition = new Vector3(0.12f, -0.10f, 0.06f);

            flashlight = go.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = flashlightRange;
            flashlight.spotAngle = flashlightAngle;
            flashlight.intensity = flashlightIntensity;
            flashlight.color = new Color(1f, 0.955f, 0.86f);
            flashlight.shadows = LightShadows.Soft;
            flashlight.shadowStrength = 0.82f;
            flashlight.shadowBias = 0.02f;
            flashlight.enabled = false;
        }

        void HandleFlashlight()
        {
            if (Input.GetKeyDown(flashlightKey))
            {
                _flashlightTarget = _flashlightTarget > 0.5f ? 0f : 1f;
                if (flashlight != null) flashlight.enabled = true;
            }

            if (flashlight == null) return;

            // 开关有一点延迟和抖动，比硬切更像真手电
            _flashlightCurrent = Mathf.MoveTowards(_flashlightCurrent, _flashlightTarget,
                6f * Time.deltaTime);
            float flick = 1f + Mathf.PerlinNoise(Time.time * 9f, 0.37f) * 0.10f - 0.05f;
            flashlight.intensity = flashlightIntensity * _flashlightCurrent * flick;

            if (_flashlightCurrent <= 0.001f && _flashlightTarget <= 0.001f)
                flashlight.enabled = false;
        }

        // ---------------------------------------------------------------- 交互

        void HandleInteraction()
        {
            CurrentTarget = null;

            if (cameraRig != null)
            {
                Ray ray = new Ray(cameraRig.position, cameraRig.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask,
                        QueryTriggerInteraction.Collide))
                {
                    var target = hit.collider.GetComponentInParent<IInteractable>();
                    if (target != null && target.CanInteract) CurrentTarget = target;
                }
            }

            if (CurrentTarget != null && Input.GetKeyDown(interactKey))
                CurrentTarget.Interact();
        }

        // ---------------------------------------------------------------- 调试 HUD

        void OnGUI()
        {
            if (!_cursorLocked) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(0.85f, 0.85f, 0.8f, 0.55f);

            // 准星
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 6f, cy - 12f, 12f, 24f), "+", style);

            if (CurrentTarget != null)
            {
                style.normal.textColor = new Color(0.95f, 0.9f, 0.78f, 0.9f);
                style.fontSize = 15;
                GUI.Label(new Rect(cx - 120f, cy + 26f, 240f, 28f), "[E] " + CurrentTarget.Prompt, style);
            }
        }
    }
}
