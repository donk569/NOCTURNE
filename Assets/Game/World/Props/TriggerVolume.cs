using System;
using UnityEngine;
using UnityEngine.Events;

namespace NOCTURNE
{
    /// <summary>
    /// 通用触发区：把 OnTriggerEnter/Exit 转成事件，供其他脚本订阅。
    /// 挂在带 BoxCollider(Is Trigger) 的对象上即可。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class TriggerVolume : MonoBehaviour
    {
        [Header("过滤")]
        [Tooltip("只响应这个 Tag 的碰撞体。留空表示不过滤")]
        public string requireTag = "Player";

        [Tooltip("只在满足条件时触发一次（用于一次性事件）")]
        public bool oneShot = false;

        [Header("UnityEvent（可在 Inspector 里拖引用）")]
        public UnityEvent onEnter;
        public UnityEvent onExit;

        /// <summary>进入，参数为触发者。</summary>
        public event Action<Collider> Entered;
        /// <summary>离开，参数为触发者。</summary>
        public event Action<Collider> Exited;

        /// <summary>当前是否有人/物在里面。</summary>
        public bool IsOccupied { get; private set; }

        int _insideCount;
        bool _fired;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!Matches(other)) return;
            if (oneShot && _fired) return;

            _insideCount++;
            IsOccupied = true;

            if (Entered != null) Entered(other);
            if (onEnter != null) onEnter.Invoke();
            _fired = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (!Matches(other)) return;

            _insideCount = Mathf.Max(0, _insideCount - 1);
            if (_insideCount == 0) IsOccupied = false;

            if (Exited != null) Exited(other);
            if (onExit != null) onExit.Invoke();
        }

        bool Matches(Collider other)
        {
            if (other == null) return false;
            if (string.IsNullOrEmpty(requireTag)) return true;
            return other.CompareTag(requireTag);
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.25f);
            var m = transform.localToWorldMatrix;
            Gizmos.matrix = m;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
