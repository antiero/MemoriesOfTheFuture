using System;
using System.Collections;
using Memories.Audio;
using PictureChanges;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CupSpike
{
    /// <summary>
    /// Desktop debug mode (scene 03-DesktopTesting, macOS app): walk a stand-in room with both
    /// experiences live, the mug on the table and the painting on the east wall, and try their
    /// sound design with a mouse and keyboard, no headset. The scream, look-inside, dream and
    /// painting code is the same as on the headset; this only stands in for what the headset provides:
    ///  - the mug's pose (fixed on a table, fed to CupDetector.Simulate)
    ///  - the painting's pose (force-locked on the wall instead of found by the camera)
    ///  - the head (mouse look, WASD to walk; E leans over the mug and back)
    ///  - a hand (Up / Down or the scroll wheel move a fingertip towards the mug and away; G picks the
    ///    mug up and puts it down, which starts and ends the dream)
    ///  - the real room (a simple stand-in room, tinted through RoomLook instead of passthrough)
    ///
    /// The on-screen guide, the welcome card and the Tab panel (Sound, Timings, Help) are drawn by
    /// DesktopUI from the state here. Sound files in ~/MemoriesOfTheFuture/Sounds replace a slot's
    /// files (see SoundPanel); L reloads them.
    ///
    /// XR is switched off at start: the project starts XR on Standalone (for the headset scenes),
    /// which on a Mac would take over the view.
    /// </summary>
    public class DesktopSim : MonoBehaviour
    {
        [SerializeField] CupDetector detector;
        [SerializeField] MugScream mugScream;
        [SerializeField] MugInterior interior;
        [SerializeField] DreamState dream;
        [SerializeField] Camera cam;
        [SerializeField] Material handMaterial;
        [Tooltip("The stand-in room. Its depth writes go off during the dream, like passthrough, which has no depth.")]
        [SerializeField] Transform room;

        [Header("Painting")]
        [SerializeField] PictureCanvas painting;
        [SerializeField] BoardPoseEstimator paintingBoard;
        [Tooltip("Centre of the painting's board on the wall.")]
        [SerializeField] Vector3 paintingCentre = new Vector3(2.99f, 1.45f, 1.2f);
        [Tooltip("Into the wall (the board's +Z).")]
        [SerializeField] Vector3 paintingInto = Vector3.right;
        [Tooltip("Where 2 puts you: this far in front of the painting (m).")]
        [SerializeField] float paintingViewDistance = 2.5f;

        [Header("Layout (m)")]
        [Tooltip("Centre of the mug's base, on the table.")]
        [SerializeField] Vector3 mugBase = new Vector3(0f, 0.75f, 0f);
        [Tooltip("Head position at the mug, and the eye height when walking.")]
        [SerializeField] Vector3 headRest = new Vector3(0f, 1.32f, -0.62f);
        [Tooltip("Head position when leaning over the mug.")]
        [SerializeField] Vector3 headLean = new Vector3(0f, 1.09f, -0.11f);
        [Tooltip("Fingertip when the hand is fully withdrawn (relative to the mug spot).")]
        [SerializeField] Vector3 handRest = new Vector3(0.3f, 0.9f, -0.38f);
        [Tooltip("You can walk this far from the room's centre (m), short of the walls.")]
        [SerializeField] float roomHalfSize = 2.75f;
        [Tooltip("The table, as a box on the floor you walk around: centre and half size (x, z).")]
        [SerializeField] Vector4 table = new Vector4(0f, 0.1f, 0.75f, 0.375f);

        [Header("Feel")]
        [SerializeField] float lookSensitivity = 0.08f;
        [SerializeField] float walkSpeed = 1.4f;
        [SerializeField] float reachSpeed = 0.45f;
        [SerializeField] float leanTime = 0.6f;
        [Tooltip("Where the held mug sits: right, up and forward of the eyes (m).")]
        [SerializeField] Vector3 heldOffset = new Vector3(0.08f, -0.2f, 0.36f);
        [SerializeField] float pickUpTime = 0.4f;

        float _yaw, _pitch;
        Vector3 _body;          // standing head position
        readonly System.Collections.Generic.List<Material> _roomMats = new System.Collections.Generic.List<Material>();
        bool _roomDepth = true;
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        float _lean;            // 0 = standing at _body, 1 = over the mug
        bool _leanTarget;
        Coroutine _aim;
        float _reach;           // 0 = withdrawn, 1 = touching the mug
        bool _holding;          // G: the mug is in the hand
        float _hold;            // 0 = on the table, 1 = in the hand
        Transform _hand;
        SoundPanel _sound;
        string _bedFiles = "";

        // ------------------------------------------------------------ read by DesktopUI

        public MugScream Mug => mugScream;
        public MugInterior Interior => interior;
        public DreamState Dream => dream;
        public bool Holding => _holding;
        public PictureCanvas Painting => painting;
        public SoundPanel Sound => _sound;
        /// <summary>The Tab panel is open (the mouse is free while it is).</summary>
        public bool PanelOpen { get; set; }
        /// <summary>The guide card with the keys is shown (H).</summary>
        public bool GuideVisible { get; set; } = true;
        /// <summary>The welcome card is up: nothing reacts to the mouse until it's closed (Enter or its button).</summary>
        public bool WelcomeOpen { get; set; } = true;
        public bool MouseLook => Cursor.lockState == CursorLockMode.Locked;
        /// <summary>Short messages for the screen ("Painting trigger: proximity").</summary>
        public event Action<string> Toast;

        void Say(string message) => Toast?.Invoke(message);

        void Awake() => StopXR();

        /// <summary>The project starts XR on Standalone for the headset scenes; on a Mac it only gets in the way.</summary>
        static void StopXR()
        {
            var xr = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            if (xr == null || xr.Manager == null || xr.Manager.activeLoader == null) return;
            Debug.Log($"[DesktopSim] Stopping XR ({xr.Manager.activeLoader.name}): desktop testing uses the screen, mouse and keyboard");
            xr.Manager.StopSubsystems();
            xr.Manager.DeinitializeLoader();
        }

        void Start()
        {
            if (cam == null) cam = Camera.main;
            _body = headRest;
            cam.transform.position = headRest;
            Face(Rim);
            BuildHand();
            // The echoes around the held mug warp a copy of the view here (no passthrough camera).
            var echoes = FindAnyObjectByType<CupEchoes>();
            if (echoes != null) echoes.UseScreen = true;
            if (room != null)
                foreach (var r in room.GetComponentsInChildren<Renderer>()) _roomMats.AddRange(r.materials);   // instances, so the assets stay as they are
            if (mugScream != null) mugScream.ShowZone = false;

            if (paintingBoard != null)
                paintingBoard.ForceLock(new Pose(paintingCentre, Quaternion.LookRotation(paintingInto.normalized, Vector3.up)));

            _sound = new SoundPanel(this);
            _sound.FilesReloaded += RebuildBedIfNeeded;
            _sound.Begin();
            Debug.Log($"[DesktopSim] Desktop debug mode. Sounds folder: {SoundMixFile.SoundsFolder}");
        }

        /// <summary>
        /// The bed is built once. A new file for a slot it already plays swaps in by itself (SlotVoice);
        /// rebuild only when a background file came or went, which can switch between the pair and stereo.
        /// </summary>
        void RebuildBedIfNeeded()
        {
            if (mugScream == null) return;
            var sig = "";
            foreach (var s in _sound.Slots)
                if (s.name.StartsWith("Background") && s.RuntimeOverride != null) sig += s.name + ";";
            if (sig == _bedFiles) return;
            _bedFiles = sig;
            mugScream.RebuildBed();
        }

        void BuildHand()
        {
            _hand = new GameObject("SimulatedHand").transform;
            _hand.SetParent(transform, false);
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(_hand, false);
            tip.transform.localScale = Vector3.one * 0.017f;
            var finger = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(finger.GetComponent<Collider>());
            finger.transform.SetParent(_hand, false);
            finger.transform.localScale = new Vector3(0.016f, 0.045f, 0.016f);
            finger.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            finger.transform.localPosition = new Vector3(0f, 0f, -0.045f);   // behind the tip (the hand points along +Z)
            foreach (var r in _hand.GetComponentsInChildren<Renderer>())
            {
                if (handMaterial != null) r.sharedMaterial = handMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        Vector3 Rim => mugBase + Vector3.up * (detector != null ? detector.MugHeight : 0.095f);
        Vector3 TouchPoint => mugBase + new Vector3(detector != null ? detector.MugRadius : 0.04f, 0.055f, -0.01f);
        Vector3 PaintingSpot
        {
            get
            {
                var p = paintingCentre - paintingInto.normalized * paintingViewDistance;
                p.y = headRest.y;
                return p;
            }
        }

        void Face(Vector3 target) => Face(target, cam.transform.position, out _yaw, out _pitch);

        static void Face(Vector3 target, Vector3 from, out float yaw, out float pitch)
        {
            var dir = target - from;
            yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(dir.normalized.y) * Mathf.Rad2Deg;
        }

        void Update()
        {
            bool roomDepth = dream == null || dream.Depth <= 0f;
            if (roomDepth != _roomDepth)
            {
                _roomDepth = roomDepth;
                foreach (var m in _roomMats) m.SetFloat(ZWriteId, roomDepth ? 1f : 0f);
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (WelcomeOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) WelcomeOpen = false;
                return;
            }

            // Cursor: click to look around, Esc to free it. Always free while the Tab panel is open.
            if (kb.tabKey.wasPressedThisFrame) PanelOpen = !PanelOpen;
            if (kb.escapeKey.wasPressedThisFrame) { if (PanelOpen) PanelOpen = false; Cursor.lockState = CursorLockMode.None; }
            if (PanelOpen) Cursor.lockState = CursorLockMode.None;
            else if (mouse.leftButton.wasPressedThisFrame && !(PointerOverUi != null && PointerOverUi(mouse.position.ReadValue())))
                Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue();
                _yaw += d.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - d.y * lookSensitivity, -85f, 85f);
            }

            Walk(kb);

            // E: go back to the mug and lean over it / lean back.
            if (kb.eKey.wasPressedThisFrame)
            {
                _leanTarget = !_leanTarget;
                if (!_leanTarget) _body = headRest;
                Aim(_leanTarget ? headLean : headRest, Rim, leanTime);
            }
            _lean = Mathf.MoveTowards(_lean, _leanTarget ? 1f : 0f, Time.deltaTime / leanTime);
            cam.transform.position = Vector3.Lerp(_body, headLean, Mathf.SmoothStep(0f, 1f, _lean));
            cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Hand: reach in and out (from beside wherever you stand at the mug).
            float move = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
            _reach = Mathf.Clamp01(_reach + move * reachSpeed * Time.deltaTime + (PanelOpen ? 0f : mouse.scroll.ReadValue().y * 0.0006f));
            var tipPos = Vector3.Lerp(handRest, TouchPoint, _reach);
            _hand.SetPositionAndRotation(tipPos, Quaternion.LookRotation(TouchPoint - handRest));
            if (mugScream != null) mugScream.SimulatedFingertip = tipPos;

            // G: pick the mug up / put it down. Held, it sits in front of you and turns with you.
            if (kb.gKey.wasPressedThisFrame) ToggleHold();
            _hold = Mathf.MoveTowards(_hold, _holding ? 1f : 0f, Time.deltaTime / pickUpTime);
            var onTable = new Pose(mugBase, Quaternion.Euler(0f, 180f, 0f));
            var yawRot = Quaternion.Euler(0f, _yaw, 0f);
            var inHand = new Pose(cam.transform.position + yawRot * heldOffset + Vector3.down * 0.0475f, yawRot * Quaternion.Euler(0f, 180f, 0f));
            float h = Mathf.SmoothStep(0f, 1f, _hold);
            if (detector != null)
                detector.Simulate(new Pose(Vector3.Lerp(onTable.position, inHand.position, h), Quaternion.Slerp(onTable.rotation, inHand.rotation, h)), _holding);

            // Shortcuts.
            if (kb.digit1Key.wasPressedThisFrame) GoToMug();
            if (kb.digit2Key.wasPressedThisFrame) GoToPainting();
            if (kb.hKey.wasPressedThisFrame) GuideVisible = !GuideVisible;
            if (kb.zKey.wasPressedThisFrame && mugScream != null)
            {
                mugScream.ShowZone = !mugScream.ShowZone;
                Say(mugScream.ShowZone ? "Touch zones shown" : "Touch zones hidden");
            }
            if (kb.lKey.wasPressedThisFrame) ReloadSounds();
            if (dream != null)
            {
                if (kb.fKey.wasPressedThisFrame) ToggleDream();
                if (kb.mKey.wasPressedThisFrame) CycleWake();
            }
            if (kb.spaceKey.wasPressedThisFrame) ScreamNow();
            if (painting != null)
            {
                if (kb.pKey.wasPressedThisFrame) ReplayPainting();
                if (kb.vKey.wasPressedThisFrame) AnimatePainting();
                if (kb.tKey.wasPressedThisFrame) CyclePaintingMode();
            }
        }

        // ------------------------------------------------------------ actions (keys and the panel's buttons)

        /// <summary>Set by DesktopUI: is this screen point over a card or button? Clicks there don't grab the mouse.</summary>
        public Func<Vector2, bool> PointerOverUi;

        public void GoToMug() { Teleport(headRest, Rim); Say("At the mug"); }
        public void GoToPainting() { Teleport(PaintingSpot, paintingCentre); Say("In front of the painting"); }
        public void ReloadSounds() { _sound.ReloadFiles(); Say("Reloading the Sounds folder..."); }
        public void ScreamNow() { if (mugScream != null) mugScream.Scream("space bar"); }
        public void ToggleHold()
        {
            _holding = !_holding;
            if (_holding && Vector3.Distance(cam.transform.position, mugBase) > 1.5f) { _holding = false; Say("Go to the mug first (1)"); return; }
            Say(_holding ? "Holding the mug" : "Mug put down");
        }
        public void AnimatePainting() { if (painting == null) return; Say(painting.Animate() ? "Painting: animating from the start" : "The painting isn't placed yet"); }
        public void ToggleDream()
        {
            if (dream == null) return;
            if (dream.Active) { dream.WakeNow(); Say("Waking up"); }
            else { dream.ForceDream = true; Say("Dreaming"); }
        }
        public void CycleWake()
        {
            if (dream == null) return;
            dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream;
            Say(dream.Wake == DreamState.WakeMode.Scream ? "Wake-up: the mug screams" : "Wake-up: silent");
        }
        public void ReplayPainting() { if (painting == null) return; painting.Replay(); Say("Painting replayed: back to the first stage"); }
        public void CyclePaintingMode()
        {
            if (painting == null) return;
            painting.CycleMode();
            Say("Painting trigger: " + PictureCanvas.ModeName(painting.Mode));
        }

        void Walk(Keyboard kb)
        {
            var input = new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                                    (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            if (input == Vector2.zero) return;
            if (_leanTarget)
            {
                // Walking off cancels the lean: stand up (eased by _lean) from where you are.
                var p = cam.transform.position;
                _body = new Vector3(p.x, headRest.y, p.z);
                _leanTarget = false;
            }
            if (_aim != null) { StopCoroutine(_aim); _aim = null; }
            var yaw = Quaternion.Euler(0f, _yaw, 0f);
            var step = yaw * new Vector3(input.x, 0f, input.y).normalized * walkSpeed * Time.deltaTime;
            var next = _body + step;
            next.x = Mathf.Clamp(next.x, -roomHalfSize, roomHalfSize);
            next.z = Mathf.Clamp(next.z, -roomHalfSize, roomHalfSize);
            // Don't walk through the table: push out along the shallower side.
            float dx = next.x - table.x, dz = next.z - table.y, hx = table.z + 0.2f, hz = table.w + 0.2f;
            if (Mathf.Abs(dx) < hx && Mathf.Abs(dz) < hz)
            {
                if (hx - Mathf.Abs(dx) < hz - Mathf.Abs(dz)) next.x = table.x + Mathf.Sign(dx) * hx;
                else next.z = table.y + Mathf.Sign(dz) * hz;
            }
            next.y = headRest.y;
            _body = next;
        }

        void Teleport(Vector3 head, Vector3 lookAt)
        {
            _leanTarget = false;
            _lean = 0f;
            if (_aim != null) { StopCoroutine(_aim); _aim = null; }
            _body = head;
            cam.transform.position = head;
            Face(lookAt);
        }

        void Aim(Vector3 end, Vector3 target, float time)
        {
            if (_aim != null) StopCoroutine(_aim);
            _aim = StartCoroutine(AimRoutine(end, target, time));
        }

        IEnumerator AimRoutine(Vector3 end, Vector3 target, float time)
        {
            // Turn the view to the target as the head moves, so leaning in ends up looking into the mug.
            float t = 0f, y0 = _yaw, p0 = _pitch;
            Face(target, end, out var y1, out var p1);
            while (t < time)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / time);
                _yaw = Mathf.LerpAngle(y0, y1, k);
                _pitch = Mathf.Lerp(p0, p1, k);
                yield return null;
            }
            _aim = null;
        }

        // ------------------------------------------------------------ on-screen guide

        /// <summary>A line of the guide: what is happening, what to do next, and how far along (negative: no bar).</summary>
        public struct Phase
        {
            public string title, hint;
            public float progress;
            public Phase(string title, string hint, float progress = -1f) { this.title = title; this.hint = hint; this.progress = progress; }
        }

        /// <summary>What the mug is doing, in plain words.</summary>
        public Phase MugPhase()
        {
            if (dream != null)
            {
                if (dream.Waking) return new Phase("Waking up", "The dream lets go");
                if (dream.Depth >= 1f) return new Phase("Deep in the dream", "Put the mug down (G) to wake", 1f);
                if (dream.Depth > 0f) return new Phase("The dream is spreading", "Keep holding the mug. Put it down (G) to wake", dream.Depth);
                if (dream.InDread) return new Phase("Holding it: something's wrong", "Keep holding and the dream comes. Put it down (G) and it fades", dream.Dread);
            }
            if (mugScream != null)
            {
                if (mugScream.InSilence) return new Phase("Touched: silence...", "The scream comes next");
                if (mugScream.ScreamPlaying) return new Phase("The mug screams", "Pull the hand back (Down arrow) to re-arm it");
            }
            if (interior != null && interior.Reveal > 0.05f) return new Phase("Looking inside", "The tunnel is open. Pick the mug up (G) and hold it for the dream", interior.Reveal);
            if (mugScream != null && mugScream.Approach > 0f) return new Phase("A hand approaches: it breathes", "Keep reaching (Up arrow) to touch it, or pull back and it stops dead", mugScream.Approach);
            if (mugScream != null && !mugScream.Armed)
                return new Phase("Re-arming", !float.IsNaN(mugScream.NearestTip) && mugScream.NearestTip < mugScream.TouchZone + 0.06f
                    ? "Pull the hand back (Down arrow) to re-arm it" : "Ready again in a moment");
            return new Phase("Waiting", "Reach for it (Up arrow or scroll), lean over it (E), or pick it up (G)");
        }

        /// <summary>What the painting is doing, in plain words.</summary>
        public Phase PaintingPhase()
        {
            if (painting == null) return new Phase("", "");
            if (paintingBoard != null && !paintingBoard.IsLocked) return new Phase("Not placed", "");
            if (!painting.Revealed) return new Phase("Appearing...", "It washes in once it's found on the wall");
            var st = painting.Stages;
            if (painting.Mode == PictureCanvas.TriggerMode.UnseenSwap && st != null)
            {
                int last = st.StageCount - 1;
                if (st.Stage >= last) return new Phase("Final stage", "It screamed as it changed. Press P to start again");
                if (st.InView)
                    return st.Armed ? new Phase("Seen: it's waiting", "Turn away. It changes while you can't see it", 1f)
                                    : new Phase("Being looked at", "Keep it in view a moment", st.SeenProgress);
                if (st.Armed)
                    return new Phase("Out of view: changing", st.Stage + 1 >= last && !st.CameClose
                        ? "To reach the last stage, walk close to it (within 1.5 m) between looks" : "Look back to see what changed", st.UnseenProgress);
                return new Phase("Out of view", "Look at it first (2 puts you in front of it)");
            }
            if (painting.HasPlayed) return new Phase(painting.IsPlaying ? "Transforming" : "Transformed", "The soundtrack plays from the painting. P to replay");
            return painting.Mode == PictureCanvas.TriggerMode.Proximity
                ? new Phase("Waiting for you", $"Walk within {painting.TriggerDistance:0.0} m and face it")
                : new Phase("Waiting for you", "Look at it for a couple of seconds");
        }
    }
}
