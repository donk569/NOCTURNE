using System;
using System.Collections;
using UnityEngine;

namespace NOCTURNE
{
    /// <summary>
    /// 坐下 / 起身。
    ///
    /// 坐下时接管相机（FPC 关闭），相机平滑移动到预设坐姿锚点；
    /// 允许有限度的转视角，禁止移动。起身后把朝向交还 FPC。
    ///
    /// CharacterController 在坐下时降低高度并重新定位，避免卡进桌子或椅子。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSittingController : MonoBehaviour
    {
        [Header("引用")]
        public FirstPersonController fpc;
        public CharacterController controller;
        public Transform cameraRig;
        [Tooltip("坐姿相机目标（场景里的空物体，朝向决定坐下后面向）")]
        public Transform sitAnchor;

        [Header("坐下镜头")]
        public float transitionDuration = 0.85f;
        public float sitLookSensitivity = 1.6f;
        [Tooltip("坐下后左右可转范围（相对坐下朝向）")]
        public float sitYawRange = 75f;
        public float sitPitchMin = -40f;
        public float sitPitchMax = 25f;

        [Header("碰撞体")]
        [Tooltip("坐下时的 CharacterController 高度")]
        public float sitColliderHeight = 0.95f;
        [Tooltip("坐下时碰撞体中心高度（一般是高度的一半）")]
        public float sitColliderCenterY = 0.48f;

        [Header("起身")]
        public KeyCode standUpKey = KeyCode.E;
        public float standEyeHeight = 1.6f;

        public bool IsSitting { get; private set; }
        public bool IsTransitioning { get; private set; }

        /// <summary>坐下完成。</summary>
        public event Action SitStarted;
        /// <summary>起身完成（已交还 FPC 控制）。</summary>
        public event Action StandFinished;

        float _yaw, _pitch, _baseYaw;
        bool _cursorLocked;

        void Awake()
        {
            if (fpc == null) fpc = GetComponent<FirstPersonController>();
            if (controller == null) controller = GetComponent<CharacterController>();
            if (cameraRig == null && fpc != null) cameraRig = fpc.cameraRig;
        }

        void Update()
        {
            if (!IsSitting || IsTransitioning) return;

            HandleCursor();
            if (_cursorLocked) HandleLook();

            if (Input.GetKeyDown(standUpKey) && _cursorLocked) RequestStandUp();
        }

        /// <summary>请求起身（按键、外部剧情、自动化测试都走这里）。</summary>
        public void RequestStandUp()
        {
            if (!IsSitting || IsTransitioning) return;
            StartCoroutine(StandRoutine());
        }

        void HandleLook()
        {
            float mx = Input.GetAxisRaw("Mouse X") * sitLookSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * sitLookSensitivity;

            _yaw += mx;
            _pitch = Mathf.Clamp(_pitch - my, sitPitchMin, sitPitchMax);

            float delta = Mathf.Clamp(Mathf.DeltaAngle(_baseYaw, _yaw), -sitYawRange, sitYawRange);
            _yaw = _baseYaw + delta;

            if (cameraRig != null)
                cameraRig.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
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

        /// <summary>坐下。sitAnchor 为空则用当前相机前方 0.6m 兜底。</summary>
        public void Sit()
        {
            if (IsSitting || IsTransitioning) return;
            StartCoroutine(SitRoutine());
        }

        IEnumerator SitRoutine()
        {
            IsTransitioning = true;

            if (fpc != null) fpc.enabled = false;
            LockCursor(true);

            // 碰撞体收矮，位置不变（避免起身时穿桌）
            if (controller != null)
            {
                controller.height = sitColliderHeight;
                controller.center = new Vector3(0f, sitColliderCenterY, 0f);
            }

            Vector3 fromPos = cameraRig != null ? cameraRig.position : transform.position;
            Quaternion fromRot = cameraRig != null ? cameraRig.rotation : transform.rotation;

            Vector3 toPos = sitAnchor != null ? sitAnchor.position
                                             : transform.position + transform.forward * 0.55f + Vector3.up * 1.15f;
            Quaternion toRot = sitAnchor != null ? sitAnchor.rotation : transform.rotation;

            Vector3 toEuler = toRot.eulerAngles;
            _baseYaw = toEuler.y;
            _yaw = _baseYaw;
            _pitch = toEuler.x > 180f ? toEuler.x - 360f : toEuler.x;

            float t = 0f;
            float dur = Mathf.Max(0.05f, transitionDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = k * k * (3f - 2f * k);
                if (cameraRig != null)
                {
                    cameraRig.position = Vector3.Lerp(fromPos, toPos, e);
                    cameraRig.rotation = Quaternion.Slerp(fromRot, Quaternion.Euler(_pitch, _yaw, 0f), e);
                }
                yield return null;
            }

            if (cameraRig != null)
            {
                cameraRig.position = toPos;
                cameraRig.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            IsSitting = true;
            IsTransitioning = false;
            if (SitStarted != null) SitStarted();
        }

        IEnumerator StandRoutine()
        {
            IsTransitioning = true;

            Vector3 fromPos = cameraRig != null ? cameraRig.position : transform.position;
            Quaternion fromRot = cameraRig != null ? cameraRig.rotation : transform.rotation;
            Vector3 toPos = transform.position + Vector3.up * standEyeHeight;
            Quaternion toRot = Quaternion.Euler(0f, _yaw, 0f);

            float t = 0f;
            float dur = Mathf.Max(0.05f, transitionDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = k * k * (3f - 2f * k);
                if (cameraRig != null)
                {
                    cameraRig.position = Vector3.Lerp(fromPos, toPos, e);
                    cameraRig.rotation = Quaternion.Slerp(fromRot, toRot, e);
                }
                yield return null;
            }

            if (controller != null)
            {
                controller.height = 1.72f;
                controller.center = new Vector3(0f, 0.86f, 0f);
            }

            if (cameraRig != null)
            {
                cameraRig.localPosition = new Vector3(0f, standEyeHeight, 0f);
                cameraRig.localRotation = Quaternion.identity;
            }

            if (fpc != null)
            {
                fpc.SetLook(_yaw, 0f);
                yield return null;
                fpc.enabled = true;
            }

            IsSitting = false;
            IsTransitioning = false;
            if (StandFinished != null) StandFinished();
        }

        /// <summary>紧急复位（重新开始游戏时用）。</summary>
        public void ResetSitting()
        {
            StopAllCoroutines();
            IsSitting = false;
            IsTransitioning = false;
            if (controller != null)
            {
                controller.height = 1.72f;
                controller.center = new Vector3(0f, 0.86f, 0f);
            }
        }
    }
}
