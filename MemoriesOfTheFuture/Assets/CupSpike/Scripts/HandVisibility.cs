using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// Hides the virtual hands and the interaction rig's pointers, so you only see your real hands
    /// in passthrough. Hand tracking keeps running (the detector, scream and hand menu all use the
    /// joint data, not the visuals).
    ///
    ///  - Hand meshes: <see cref="HandVisual.ForceOffVisibility"/> (the SDK's own switch, so it
    ///    doesn't turn them back on).
    ///  - Rays, reticles, poke limiters, grab and locomotion visuals: the GameObjects under the
    ///    interactors whose names end in "Visual" or "Visuals" are deactivated.
    /// The Hands button in the hand menu brings them back for debugging.
    /// </summary>
    public class HandVisibility : MonoBehaviour
    {
        [Tooltip("The interaction rig (OVRComprehensiveInteractionRig).")]
        [SerializeField] Transform rig;
        [SerializeField] bool visible = false;

        readonly List<HandVisual> _hands = new List<HandVisual>();
        readonly List<GameObject> _pointerVisuals = new List<GameObject>();
        readonly Dictionary<GameObject, bool> _wasActive = new Dictionary<GameObject, bool>();

        public bool Visible
        {
            get => visible;
            set { visible = value; Apply(); }
        }

        public void Toggle() => Visible = !Visible;

        void Start()
        {
            if (rig == null) { Debug.LogWarning("[HandVisibility] No rig assigned"); return; }
            rig.GetComponentsInChildren(true, _hands);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponentInParent<HandVisual>(true) != null) continue;   // handled by ForceOffVisibility
                if (!t.name.EndsWith("Visual") && !t.name.EndsWith("Visuals")) continue;
                if (!IsUnderInteractor(t)) continue;
                _pointerVisuals.Add(t.gameObject);
                _wasActive[t.gameObject] = t.gameObject.activeSelf;
            }
            Apply();
            Debug.Log($"[HandVisibility] {(visible ? "Showing" : "Hiding")} {_hands.Count} hand visuals and {_pointerVisuals.Count} pointer visuals");
        }

        static bool IsUnderInteractor(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name.Contains("Interactor")) return true;
            return false;
        }

        void Apply()
        {
            foreach (var h in _hands) if (h != null) h.ForceOffVisibility = !visible;
            foreach (var go in _pointerVisuals)
                if (go != null) go.SetActive(visible ? _wasActive[go] : false);
        }
    }
}
