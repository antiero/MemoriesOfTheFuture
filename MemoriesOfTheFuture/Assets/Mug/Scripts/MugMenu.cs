using PictureChanges.UI;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>The Cup scene's own hand-menu buttons (the scene row is added by SceneMenu).</summary>
    public class MugMenu : MonoBehaviour
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
        [SerializeField] CupEchoes echoes;
        [SerializeField] DreamSplatRoom splatRoom;
        [Tooltip("Renderers of the proxy mug, shown by the Proxy button. Hidden by default: the real mug is the " +
                 "star, and an invisible occluder (MugInterior) still hides virtual things behind it.")]
        [SerializeField] Renderer[] proxyRenderers;
        [SerializeField] bool proxyVisible = false;

        // Artists and visitors see Inside, Dream and Wake (plus the scene row). Everything else is for
        // tuning the detector and is a developer button: tick Developer Buttons on the HandMenu to show it.
        void Awake()
        {
            ApplyProxy();
            // Debug: the panel, the world markers and the touch-zone rings, together. Off at launch.
            menu.AddButton("Debug", () =>
            {
                debugView.Toggle();
                if (mugScream != null) mugScream.ShowZone = debugView.Visible;
            }, () => debugView.Visible, developer: true);
            menu.AddButton("Proxy", ToggleProxy, () => proxyVisible, developer: true);
            if (handVisibility != null) menu.AddButton("Hands", handVisibility.Toggle, () => handVisibility.Visible, developer: true);
            menu.AddButton("Freeze", () => detector.Frozen = !detector.Frozen, () => detector.Frozen, developer: true);
            // Cycles Auto -> depth only -> size only; lit when not Auto. The debug panel shows which.
            menu.AddButton("Dist", () => detector.Mode = (CupDetector.DistanceMode)(((int)detector.Mode + 1) % 3),
                           () => detector.Mode != CupDetector.DistanceMode.Auto, developer: true);
            // Held-mug tilt: Damped -> Upright -> Rigid -> Damped (lit when not Damped; the panel shows which).
            menu.AddButton("Tilt", () => detector.TiltMode = (CupDetector.HeldTilt)(((int)detector.TiltMode + 2) % 3),
                           () => detector.TiltMode != CupDetector.HeldTilt.Damped, developer: true);
            if (mugScream != null)
            {
                menu.AddRowBreak();
                // Zone: touch at 3 cm (contact, default) <-> 10 cm (lit). Pin: lit while pinned / placing; pinch at the mug to place.
                menu.AddButton("Zone", mugScream.CycleZone, () => mugScream.TouchZone > 0.05f, developer: true);
                menu.AddButton("Pin", mugScream.TogglePin, () => mugScream.ZoneMode != MugScream.Mode.Tracked, developer: true);
                menu.AddButton("Miss", mugScream.LogMiss, developer: true);
                menu.AddButton("False", mugScream.LogFalse, developer: true);
            }
            if (interior != null)
            {
                // Inside: keep the well open without looking in (to check it). Rec: save camera frames while looking in.
                menu.AddButton("Inside", () => interior.ForceOpen = !interior.ForceOpen, () => interior.ForceOpen);
                if (runeCapture != null) menu.AddButton("Rec", runeCapture.Toggle, () => runeCapture.Recording, developer: true);
                // Rim: lock the held mug to the opening seen by the camera (lit = on, the default).
                if (rimTracker != null) menu.AddButton("Rim", () => rimTracker.Locking = !rimTracker.Locking, () => rimTracker.Locking, developer: true);
            }
            if (dream != null)
            {
                // Dream: start the dread and dream without holding the mug (press again to wake; putting a held mug down wakes too).
                // Wake: lit = silent wake-up, unlit = the mug screams when you wake.
                menu.AddButton("Dream", () => { if (dream.Active) dream.WakeNow(); else dream.ForceDream = true; },
                               () => dream.Active);
                menu.AddButton("Wake", () => dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream,
                               () => dream.Wake == DreamState.WakeMode.Silent);
            }
            // Echo: show the echoes around the mug at full strength, held or not (for tuning them).
            if (echoes != null) menu.AddButton("Echo", () => echoes.ForceOn = !echoes.ForceOn, () => echoes.ForceOn, developer: true);
            // Splat: the dream scan's quality, Full (default) -> Balanced -> Fast (lit when not Full; the debug panel shows fps).
            if (splatRoom != null) menu.AddButton("Splat", () => splatRoom.Quality = (DreamSplatRoom.SplatQuality)(((int)splatRoom.Quality + 1) % 3),
                                                  () => splatRoom.Quality != DreamSplatRoom.SplatQuality.Full, developer: true);
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
