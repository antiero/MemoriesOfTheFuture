using PictureChanges.UI;
using UnityEngine;

namespace CupSpike
{
    /// <summary>The Cup scene's own hand-menu buttons (the scene row is added by SceneMenu).</summary>
    public class CupSpikeMenu : MonoBehaviour
    {
        [SerializeField] HandMenu menu;
        [SerializeField] CupDetector detector;
        [SerializeField] CupDebugView debugView;
        [SerializeField] MugScream mugScream;
        [SerializeField] HandVisibility handVisibility;
        [SerializeField] MugInterior interior;
        [SerializeField] RuneCapture runeCapture;
        [SerializeField] DreamState dream;
        [SerializeField] RimTracker rimTracker;
        [Tooltip("Renderers of the proxy mug, shown by the Proxy button. Hidden by default: the real mug is the " +
                 "star, and an invisible occluder (MugInterior) still hides virtual things behind it.")]
        [SerializeField] Renderer[] proxyRenderers;
        [SerializeField] bool proxyVisible = false;

        void Awake()
        {
            ApplyProxy();
            // Debug: the panel, the world markers and the touch-zone rings, together. Off at launch.
            menu.AddButton("Debug", () =>
            {
                debugView.Toggle();
                if (mugScream != null) mugScream.ShowZone = debugView.Visible;
            }, () => debugView.Visible);
            menu.AddButton("Proxy", ToggleProxy, () => proxyVisible);
            if (handVisibility != null) menu.AddButton("Hands", handVisibility.Toggle, () => handVisibility.Visible);
            menu.AddButton("Freeze", () => detector.Frozen = !detector.Frozen, () => detector.Frozen);
            // Cycles Auto -> depth only -> size only; lit when not Auto. The debug panel shows which.
            menu.AddButton("Dist", () => detector.Mode = (CupDetector.DistanceMode)(((int)detector.Mode + 1) % 3),
                           () => detector.Mode != CupDetector.DistanceMode.Auto);
            // Held-mug tilt: Damped -> Upright -> Rigid -> Damped (lit when not Damped; the panel shows which).
            menu.AddButton("Tilt", () => detector.TiltMode = (CupDetector.HeldTilt)(((int)detector.TiltMode + 2) % 3),
                           () => detector.TiltMode != CupDetector.HeldTilt.Damped);
            if (mugScream != null)
            {
                menu.AddRowBreak();
                // Zone: touch at 3 cm (contact, default) <-> 10 cm (lit). Pin: lit while pinned / placing; pinch at the mug to place.
                menu.AddButton("Zone", mugScream.CycleZone, () => mugScream.TouchZone > 0.05f);
                menu.AddButton("Pin", mugScream.TogglePin, () => mugScream.ZoneMode != MugScream.Mode.Tracked);
                menu.AddButton("Miss", mugScream.LogMiss);
                menu.AddButton("False", mugScream.LogFalse);
            }
            if (interior != null)
            {
                // Inside: keep the well open without looking in (to check it). Rec: save camera frames while looking in.
                menu.AddButton("Inside", () => interior.ForceOpen = !interior.ForceOpen, () => interior.ForceOpen);
                if (runeCapture != null) menu.AddButton("Rec", runeCapture.Toggle, () => runeCapture.Recording);
                // Rim: lock the held mug to the opening seen by the camera (lit = on, the default).
                if (rimTracker != null) menu.AddButton("Rim", () => rimTracker.Locking = !rimTracker.Locking, () => rimTracker.Locking);
            }
            if (dream != null)
            {
                // Dream: start the dream without looking in (press again while dreaming to wake).
                // Wake: lit = silent wake-up, unlit = the mug screams when you wake.
                menu.AddButton("Dream", () => { if (dream.Dreaming) dream.WakeNow(); else dream.ForceDream = true; },
                               () => dream.Dreaming);
                menu.AddButton("Wake", () => dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream,
                               () => dream.Wake == DreamState.WakeMode.Silent);
            }
        }

        void ToggleProxy()
        {
            proxyVisible = !proxyVisible;
            ApplyProxy();
        }

        void ApplyProxy()
        {
            foreach (var r in proxyRenderers) if (r != null) r.enabled = proxyVisible;
        }
    }
}
