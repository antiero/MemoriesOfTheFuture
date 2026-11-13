using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PictureChanges
{
    /// <summary>
    /// How the visitor relates to the painting, as explicit states and events. Both states use
    /// hysteresis (different enter and exit thresholds) so they don't chatter at the boundary.
    ///
    ///   Near / Far      : head within nearEnter of the painting centre (exit at nearExit).
    ///   Facing / Away   : head direction within faceEnter degrees of the painting (exit at faceExit).
    ///   Triggered       : Near and Facing held for `dwell` seconds. Fires once until Rearm().
    ///
    /// Events: Approach, Leave, FaceToward, FaceAway, Triggered. Every event is logged
    /// ("[Presence] ...") and kept in a short history for the test-mode readout.
    /// Only active while a painting is placed (real lock or test-mode placement).
    /// </summary>
    public class ViewerPresence : MonoBehaviour
    {
        public enum Event { Approach, Leave, FaceToward, FaceAway, Triggered }

        [SerializeField] BoardPoseEstimator estimator;
        [SerializeField] Transform painting;
        [SerializeField] Transform head;

        [Header("Distance (m)")]
        [SerializeField] float nearEnter = 1.5f;
        [SerializeField] float nearExit = 1.7f;

        [Header("Facing (degrees between head forward and the painting)")]
        [SerializeField] float faceEnter = 25f;
        [SerializeField] float faceExit = 35f;

        [Header("Trigger")]
        [SerializeField] float dwell = 0.5f;

        public UnityEvent onApproach, onLeave, onFaceToward, onFaceAway, onTriggered;
        public event Action<Event> Changed;

        public bool Active { get; private set; }
        public bool IsNear { get; private set; }
        public bool IsFacing { get; private set; }
        public bool HasTriggered { get; private set; }
        public float Distance { get; private set; }
        public float Angle { get; private set; }
        public float DwellProgress => Mathf.Clamp01(_dwellTime / Mathf.Max(0.01f, dwell));
        public float NearEnter => nearEnter;

        public struct Entry { public float time; public Event evt; public float distance, angle; }
        readonly List<Entry> _history = new List<Entry>();
        public IReadOnlyList<Entry> History => _history;

        float _dwellTime;

        /// <summary>Allow the trigger to fire again (e.g. after Replay).</summary>
        public void Rearm()
        {
            HasTriggered = false;
            _dwellTime = 0f;
        }

        /// <summary>Forget all state (painting removed / rescanning).</summary>
        public void ResetState()
        {
            Active = IsNear = IsFacing = HasTriggered = false;
            _dwellTime = 0f;
        }

        void Update()
        {
            bool active = estimator != null && estimator.IsLocked && painting != null && painting.gameObject.activeInHierarchy && head != null;
            if (!active)
            {
                if (Active) ResetState();
                return;
            }
            Active = true;

            var toPainting = painting.position - head.position;
            Distance = toPainting.magnitude;
            Angle = Vector3.Angle(head.forward, toPainting);

            bool near = IsNear ? Distance < nearExit : Distance < nearEnter;
            if (near != IsNear) { IsNear = near; Raise(near ? Event.Approach : Event.Leave); }

            bool facing = IsFacing ? Angle < faceExit : Angle < faceEnter;
            if (facing != IsFacing) { IsFacing = facing; Raise(facing ? Event.FaceToward : Event.FaceAway); }

            _dwellTime = IsNear && IsFacing ? _dwellTime + Time.deltaTime : 0f;
            if (!HasTriggered && _dwellTime >= dwell)
            {
                HasTriggered = true;
                Raise(Event.Triggered);
            }
        }

        void Raise(Event e)
        {
            _history.Add(new Entry { time = Time.time, evt = e, distance = Distance, angle = Angle });
            if (_history.Count > 12) _history.RemoveAt(0);
            Debug.Log($"[Presence] {e} at {Distance:F2} m, {Angle:F0} deg");
            switch (e)
            {
                case Event.Approach: onApproach?.Invoke(); break;
                case Event.Leave: onLeave?.Invoke(); break;
                case Event.FaceToward: onFaceToward?.Invoke(); break;
                case Event.FaceAway: onFaceAway?.Invoke(); break;
                case Event.Triggered: onTriggered?.Invoke(); break;
            }
            Changed?.Invoke(e);
        }
    }
}
