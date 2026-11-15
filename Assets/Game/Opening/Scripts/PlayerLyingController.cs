using System;
using System.Collections;
using UnityEngine;

namespace NOCTURNE.Opening
{
    /// <summary>
    /// 躺卧状态。
    ///
    /// 项目原有的 FirstPersonController 只支持站立/蹲下，没有躺卧，
    /// 所以这里接管相机：直接按世界坐标摆放 cameraRig，并限制视角范围。
    /// 与 FPC 的关系是「互斥」——躺卧期间 FPC 被 disable，起床完成后交还。
    ///
    /// 起床路径分两段：先在床上坐起（抬升 + 俯仰归零），再把相机平滑移回玩家根节点上方的站立眼位。
    ///
    /// 【位置唯一真相】玩家根节点从场景摆位开始就固定在床边的合法地面位置，全程不移动。
    /// 躺卧期间只有 cameraRig 被临时摆到床上的躺卧锚点；起床只负责把 cameraRig 从
    /// 躺卧眼位平滑插值回站立眼位，绝不写 transform.position，也不做任何传送。
    /// CharacterController 全程保持启用（根节点本来就没和家具重叠），因此不存在
    /// 「禁用→重新启用时被 depenetration 弹出地图」的问题。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerLyingController : MonoBehaviour
    {
        [Header("引用")]
        public FirstPersonController fpc;
        [Tooltip("相机所在的子物体（Player/CameraRig）")]
        public Transform cameraRig;
        [Tooltip("玩家根上的 CharacterController")]
        public CharacterController controller;
        [Tooltip("场景里标记躺卧视角的空物体：位置=躺卧眼位，朝向=初始视线")]
        public Transform lyingAnchor;

        [Header("躺卧视角")]
        [Tooltip("鼠标灵敏度（与 FPC 独立，躺卧时手感可以更慢）")]
        public float lookSensitivity = 1.8f;
        [Tooltip("左右可转范围（相对躺卧锚点朝向，度）")]
        public float yawRange = 115f;
        [Tooltip("可仰视角度（度，负数=向上看）")]
        public float pitchMin = -55f;
        [Tooltip("可俯视角度（度）")]
        public float pitchMax = 38f;
        public bool invertY = false;

        [Header("呼吸起伏")]
        [Tooltip("默认关闭：本项目要求玩家相机完全无抖动，躺卧时只保留鼠标视角")]
        public bool breathe = false;
        [Tooltip("相机上下起伏幅度（米），要非常小。峰峰值 = 2 倍此值")]
        public float breathAmplitude = 0.006f;
        [Tooltip("呼吸频率（Hz），躺着比站着慢")]
        public float breathFrequency = 0.5f;
        [Tooltip("俯仰方向呼吸偏移（度），几乎察觉不到的点头")]
        public float breathPitchDegrees = 0.12f;
        [Tooltip("左右侧倾呼吸偏移（度）")]
        public float breathRollDegrees = 0.15f;
        [Tooltip("苏醒后多少秒内把呼吸完全衰减到 0（0 = 不衰减）")]
        public float breathSettleSeconds = 2.5f;

        [Header("起床")]
        public KeyCode getUpKey = KeyCode.E;
        [Tooltip("整个起床过渡时长")]
        public float getUpDuration = 2.4f;
        [Tooltip("其中「坐起来」占的比例")]
        [Range(0.2f, 0.75f)] public float sitUpFraction = 0.4f;
        [Tooltip("坐起时的眼高（相对玩家地面）")]
        public float sitUpEyeHeight = 1.02f;
        [Tooltip("站立眼高，应与 FPC 的 standEyeHeight 一致")]
        public float standEyeHeight = 1.6f;

        [Tooltip("【已废弃】旧版本用它在起床末尾直接写玩家根节点位置（会把玩家塞进床头柜并被 CharacterController 弹出地图）。" +
                 "现在根节点全程不动，站立位置就是场景里 Player 的摆位。保留字段只为兼容已有场景序列化数据。")]
        public Vector3 getUpStandOffset = new Vector3(-1.15f, 0f, -0.05f);

        /// <summary>眼高以 FPC 为唯一真相，避免两个脚本各写一套高度导致起身瞬间跳变。</summary>
        float EyeHeight { get { return fpc != null ? fpc.standEyeHeight : standEyeHeight; } }

        public bool IsLying { get { return _lying; } }
        public bool IsGettingUp { get { return _gettingUp; } }
        public bool CanGetUp { get { return _lying && _getUpAllowed && !_gettingUp; } }

        public event Action GetUpStarted;
        public event Action GetUpFinished;

        float _yaw, _pitch, _baseYaw, _breathPhase, _startPitch, _breathSettleT;
        bool _lying, _getUpAllowed, _gettingUp, _cursorLocked, _lookAllowed;
        Vector3 _anchorPos;

        /// <summary>呼吸权重：苏醒后按 breathSettleSeconds 线性衰减到 0，之后完全静止。</summary>
        float BreathWeight
        {
            get
            {
                if (!breathe) return 0f;
                if (breathSettleSeconds <= 0f) return 1f;
                return 1f - Mathf.Clamp01(_breathSettleT / breathSettleSeconds);
            }
        }

        void Awake()
        {
            if (fpc == null) fpc = GetComponent<FirstPersonController>();
            if (controller == null) controller = GetComponent<CharacterController>();
            if (cameraRig == null && fpc != null) cameraRig = fpc.cameraRig;
        }

        // ---------------------------------------------------------------- 进入躺卧

        /// <summary>进入躺卧：相机就位，暂时不接受输入（allowLook 由开场流程控制）。</summary>
        public void BeginLying(bool allowLookAtOnce = false)
        {
            if (lyingAnchor == null)
            {
                Debug.LogError("[PlayerLyingController] lyingAnchor 未指定，无法进入躺卧状态", this);
                return;
            }

            _lying = true;
            _gettingUp = false;
            _getUpAllowed = false;
            _lookAllowed = allowLookAtOnce;

            if (fpc != null) fpc.enabled = false;
            // CharacterController 保持启用：根节点始终停在床边合法地面位置，
            // 不需要禁用/重启用，也就不会有重新启用时的重叠弹飞。
            if (controller != null) controller.enabled = true;

            _anchorPos = lyingAnchor.position;
            Vector3 fwd = lyingAnchor.rotation * Vector3.forward;
            _pitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            _baseYaw = lyingAnchor.eulerAngles.y;
            _yaw = _baseYaw;
            _startPitch = _pitch;
            _breathPhase = 0f;
            _breathSettleT = 0f;

            ApplyCamera(0f);
            LockCursor(true);
        }

        /// <summary>允许玩家开始转动视角（睁眼之后）。同时开始呼吸衰减计时。</summary>
        public void EnableLook(bool enable)
        {
            _lookAllowed = enable;
            if (!enable) { _getUpAllowed = false; return; }
            _breathSettleT = 0f;
            _breathPhase = 0f;
        }

        /// <summary>允许按 E 起床（提示出现之后）。</summary>
        public void AllowGetUp(bool allow)
        {
            _getUpAllowed = allow;
        }

        // ---------------------------------------------------------------- 每帧

        void Update()
        {
            if (!_lying || _gettingUp) return;

            HandleCursor();
            if (_cursorLocked && _lookAllowed) HandleLook();

            if (CanGetUp && Input.GetKeyDown(getUpKey) && _cursorLocked)
                RequestGetUp();
        }

        /// <summary>
        /// 主动请求起床。按键、外部剧情、自动化测试都走这里。
        /// 未满足条件（还没允许 / 正在起床）时直接忽略，不会重复触发。
        /// </summary>
        public void RequestGetUp()
        {
            if (!CanGetUp || _gettingUp) return;
            StartCoroutine(GetUpRoutine());
        }

        void HandleLook()
        {
            float mx = Input.GetAxisRaw("Mouse X") * lookSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * lookSensitivity;
            if (invertY) my = -my;

            _yaw += mx;
            _pitch = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);

            float delta = Mathf.Clamp(Mathf.DeltaAngle(_baseYaw, _yaw), -yawRange, yawRange);
            _yaw = _baseYaw + delta;

            _breathPhase += Time.deltaTime * breathFrequency * Mathf.PI * 2f;
            if (breathSettleSeconds > 0f) _breathSettleT += Time.deltaTime;

            float w = BreathWeight;
            float y = Mathf.Sin(_breathPhase) * breathAmplitude * w;
            float pitch = Mathf.Cos(_breathPhase * 0.5f) * breathPitchDegrees * w;
            float roll = Mathf.Sin(_breathPhase * 0.71f) * breathRollDegrees * w;

            ApplyCamera(y, pitch, roll);
        }

        void ApplyCamera(float verticalOffset, float pitchOffset = 0f, float roll = 0f)
        {
            if (cameraRig == null) return;
            cameraRig.position = _anchorPos + Vector3.up * verticalOffset;
            cameraRig.rotation = Quaternion.Euler(_pitch + pitchOffset, _yaw, roll);
        }

        void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
            else if (Input.GetMouseButtonDown(0) && !_cursorLocked) LockCursor(true);
            if (Input.GetKeyDown(getUpKey) && !_cursorLocked) LockCursor(true);
        }

        void LockCursor(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ---------------------------------------------------------------- 起床

        IEnumerator GetUpRoutine()
        {
            if (_gettingUp) yield break;

            _gettingUp = true;
            _getUpAllowed = false;
            if (GetUpStarted != null) GetUpStarted();

            // 根节点是位置唯一真相，起床全程只读、绝不写：
            // 玩家最终就站在场景里摆好的床边地面位置，没有传送、没有跳帧。
            Vector3 rootPos = transform.position;
            float eye = EyeHeight;

            Vector3 p0 = cameraRig != null ? cameraRig.position : _anchorPos;
            Vector3 standPos = rootPos + Vector3.up * eye;

            // 第一段：在床上原地坐起（只抬眼高 + 俯仰归零，不横移）
            Vector3 sitPos = new Vector3(_anchorPos.x, rootPos.y + sitUpEyeHeight, _anchorPos.z);

            float yaw = _yaw;
            float phase1 = Mathf.Max(0.05f, getUpDuration * sitUpFraction);
            float phase2 = Mathf.Max(0.05f, getUpDuration - phase1);

            float t = 0f;
            while (t < phase1)
            {
                t += Time.deltaTime;
                float k = Smooth01(t / phase1);
                if (cameraRig != null)
                {
                    cameraRig.position = Vector3.Lerp(p0, sitPos, k);
                    cameraRig.rotation = Quaternion.Euler(Mathf.Lerp(_pitch, 0f, k), yaw, 0f);
                }
                yield return null;
            }

            t = 0f;
            while (t < phase2)
            {
                t += Time.deltaTime;
                float k = Smooth01(t / phase2);
                if (cameraRig != null)
                {
                    cameraRig.position = Vector3.Lerp(sitPos, standPos, k);
                    cameraRig.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
                yield return null;
            }

            // 落定：只把相机还原成 FPC 期望的 local 布局。
            // 根节点从没被写过，所以画面、碰撞体、CharacterController 都不会跳。
            if (cameraRig != null)
            {
                cameraRig.position = standPos;
                cameraRig.localPosition = new Vector3(0f, eye, 0f);
                cameraRig.localRotation = Quaternion.identity;
            }

            if (fpc != null)
            {
                fpc.SetLook(yaw, 0f);
                // 隔一帧再交还控制，避免起床的 E 键在同一帧被交互系统再次消费
                yield return null;
                fpc.enabled = true;
            }

            if (controller != null) controller.enabled = true;

            _gettingUp = false;
            _lying = false;
            _lookAllowed = false;

            LockCursor(true);
            if (GetUpFinished != null) GetUpFinished();
        }

        static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // ---------------------------------------------------------------- 复位

        /// <summary>重新开始开场时把状态清干净。</summary>
        public void ResetLying()
        {
            StopAllCoroutines();
            _lying = false;
            _gettingUp = false;
            _getUpAllowed = false;
            _lookAllowed = false;
            if (controller != null) controller.enabled = true;
        }
    }
}
