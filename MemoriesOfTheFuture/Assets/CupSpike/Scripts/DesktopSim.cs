using System.Collections;
using Memories.Audio;
using PictureChanges;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CupSpike
{
    /// <summary>
    /// Desktop debug mode (scene 03-CupDesktop, macOS app): walk a stand-in room with both
    /// experiences live, the mug on the table and the painting on the east wall, and try their
    /// sound design with a mouse and keyboard, no headset. The scream, look-inside, dream and
    /// painting code is the same as on the headset; this only stands in for what the headset provides:
    ///  - the mug's pose (fixed on a table, fed to CupDetector.Simulate)
    ///  - the painting's pose (force-locked on the wall instead of found by the camera)
    ///  - the head (mouse look, WASD to walk; E leans over the mug and back)
    ///  - a hand (Up / Down or the scroll wheel move a fingertip towards the mug and away)
    ///  - the real room (a simple stand-in room, tinted through RoomLook instead of passthrough)
    ///
    /// On-screen guide: what each experience is doing, the controls, and (Tab) a panel with the
    /// Sound panel (every Sound Slot, live) and timings. Sound files in ~/MemoriesOfTheFuture/Sounds
    /// replace a slot's files (see SoundPanel); L reloads them.
    /// </summary>
    public class DesktopSim : MonoBehaviour
    {
        [SerializeField] CupDetector detector;
        [SerializeField] MugScream mugScream;
        [SerializeField] MugInterior interior;
        [SerializeField] DreamState dream;
        [SerializeField] Camera cam;
        [SerializeField] Material handMaterial;

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

        enum Tab { Sound, Timings }

        float _yaw, _pitch;
        Vector3 _body;          // standing head position
        float _lean;            // 0 = standing at _body, 1 = over the mug
        bool _leanTarget;
        Coroutine _aim;
        float _reach;           // 0 = withdrawn, 1 = touching the mug
        bool _hud = true, _panel;
        Tab _tab;
        Vector2 _timingScroll;
        Transform _hand;
        SoundPanel _sound;
        string _bedFiles = "";
        GUIStyle _title, _body2, _small, _phase, _paintPhase;

        void Start()
        {
            if (cam == null) cam = Camera.main;
            _body = headRest;
            cam.transform.position = headRest;
            Face(Rim);
            BuildHand();
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
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // Cursor: click to look around, Esc to free it. Always free while the Tab panel is open.
            if (kb.tabKey.wasPressedThisFrame) _panel = !_panel;
            if (kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (_panel) Cursor.lockState = CursorLockMode.None;
            else if (mouse.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
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
            _reach = Mathf.Clamp01(_reach + move * reachSpeed * Time.deltaTime + (_panel ? 0f : mouse.scroll.ReadValue().y * 0.0006f));
            var tipPos = Vector3.Lerp(handRest, TouchPoint, _reach);
            _hand.SetPositionAndRotation(tipPos, Quaternion.LookRotation(TouchPoint - handRest));
            if (mugScream != null) mugScream.SimulatedFingertip = tipPos;

            if (detector != null) detector.Simulate(new Pose(mugBase, Quaternion.Euler(0f, 180f, 0f)), false);

            // Shortcuts.
            if (kb.digit1Key.wasPressedThisFrame) Teleport(headRest, Rim);
            if (kb.digit2Key.wasPressedThisFrame) Teleport(PaintingSpot, paintingCentre);
            if (kb.hKey.wasPressedThisFrame) _hud = !_hud;
            if (kb.zKey.wasPressedThisFrame && mugScream != null) mugScream.ShowZone = !mugScream.ShowZone;
            if (kb.lKey.wasPressedThisFrame) _sound.ReloadFiles();
            if (dream != null)
            {
                if (kb.fKey.wasPressedThisFrame) { if (dream.Dreaming) dream.WakeNow(); else dream.ForceDream = true; }
                if (kb.mKey.wasPressedThisFrame)
                    dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream;
            }
            if (kb.spaceKey.wasPressedThisFrame && mugScream != null) mugScream.Scream("space bar");
            if (painting != null)
            {
                if (kb.pKey.wasPressedThisFrame) painting.Replay();
                if (kb.tKey.wasPressedThisFrame) painting.CycleMode();
            }
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

        string MugPhase()
        {
            if (dream != null)
            {
                if (dream.Waking) return "Waking up...";
                if (dream.Depth >= 1f) return "Deep in the dream. Look away to wake";
                if (dream.Depth > 0f) return $"The dream is spreading ({dream.Depth * 100f:0}%). Keep looking at the mug";
            }
            if (mugScream != null)
            {
                if (mugScream.InSilence) return "Touched: silence...";
                if (mugScream.ScreamPlaying) return "THE MUG SCREAMS";
            }
            if (interior != null && interior.Reveal > 0.05f) return "Looking inside: the tunnel is open. Keep staring to dream";
            if (mugScream != null && mugScream.Approach > 0f) return $"Hand approaching: the mug breathes ({mugScream.Approach * 100f:0}%)";
            if (mugScream != null && !mugScream.Armed) return "Pull the hand back to re-arm the scream";
            return "Idle. Reach for the mug, or lean over it";
        }

        string PaintingPhase()
        {
            if (painting == null) return "";
            if (paintingBoard != null && !paintingBoard.IsLocked) return "Painting: not placed";
            if (!painting.Revealed) return "Painting: revealing...";
            var mode = PictureCanvas.ModeName(painting.Mode);
            var st = painting.Stages;
            if (painting.Mode == PictureCanvas.TriggerMode.UnseenSwap && st != null)
            {
                int last = st.StageCount - 1;
                string state = st.Stage >= last ? "final stage: P to replay"
                             : st.InView ? (st.Armed ? "armed: look away to swap" : $"in view, being seen {st.SeenProgress * 100f:0}%")
                             : st.Armed ? $"out of view: swapping {st.UnseenProgress * 100f:0}%" : "out of view: look at it first";
                return $"Painting ({mode}): stage {st.Stage}/{last}, {state}";
            }
            if (painting.HasPlayed) return $"Painting ({mode}): transforming{(painting.IsPlaying ? "" : ", finished")}. P to replay";
            return $"Painting ({mode}): waiting. {(painting.Mode == PictureCanvas.TriggerMode.Proximity ? $"Walk within {painting.TriggerDistance:0.0} m and face it" : "Look at it for a while")}";
        }

        void OnGUI()
        {
            float s = Mathf.Max(1f, Screen.height / 800f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s, h = Screen.height / s;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                _phase = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
                _phase.normal.textColor = new Color(1f, 0.55f, 0.4f);
                _paintPhase = new GUIStyle(_phase) { fontSize = 14 };
                _paintPhase.normal.textColor = new Color(0.55f, 0.8f, 1f);
                _body2 = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
                _small.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            }

            if (_panel) DrawPanel(w, h);

            if (!_hud)
            {
                GUI.Label(new Rect(10, h - 22, 300, 20), "H: show the guide", _small);
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 360, h - 24), GUI.skin.box);
            GUILayout.Label("Memories of the Future: desktop room", _title);
            GUILayout.Label(MugPhase(), _phase);
            var pp = PaintingPhase();
            if (pp != "") GUILayout.Label(pp, _paintPhase);
            GUILayout.Space(6);
            GUILayout.Label(
                "<b>Mouse</b>  look around (click to start, Esc frees the cursor)\n" +
                "<b>W A S D</b>  walk\n" +
                "<b>1</b>  go to the mug    <b>2</b>  go to the painting\n" +
                "<b>Tab</b>  Sound panel and timings    <b>L</b>  reload sound files\n" +
                "<b>H</b>  hide this guide", _body2);
            GUILayout.Space(4);
            GUILayout.Label(
                "<b>The mug</b>\n" +
                "<b>Up / Down</b> or scroll  move the hand towards the mug / away\n" +
                "<b>E</b>  lean over the mug / lean back\n" +
                "<b>F</b>  start the dream now / wake up    <b>M</b>  wake-up: scream or silent\n" +
                "<b>Space</b>  scream now    <b>Z</b>  show zones", _body2);
            GUILayout.Space(4);
            GUILayout.Label(
                "<b>The painting</b> (east wall)\n" +
                "<b>P</b>  replay    <b>T</b>  trigger: unseen swap / proximity / gaze", _body2);
            GUILayout.Space(6);
            GUILayout.Label(
                "<b>Things to try</b>\n" +
                "1. Reach slowly: the mug breathes. Pull back: it stops dead.\n" +
                "2. Reach all the way: silence, then the scream.\n" +
                "3. Press E and look into the mug: the tunnel opens; keep staring to dream.\n" +
                "4. Press 2, look at the painting, then turn away: it changes behind your back. " +
                "Walk close (W) between looks to let it reach the last stage and scream.", _body2);
            GUILayout.FlexibleSpace();
            if (mugScream != null) GUILayout.Label(mugScream.StatusLine, _small);
            if (interior != null) GUILayout.Label(interior.StatusLine, _small);
            if (dream != null) GUILayout.Label(dream.StatusLine, _small);
            GUILayout.EndArea();
        }

        void DrawPanel(float w, float h)
        {
            const float width = 430f;
            GUILayout.BeginArea(new Rect(w - width - 12, 12, width, h - 24), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == Tab.Sound, "Sound", GUI.skin.button)) _tab = Tab.Sound;
            if (GUILayout.Toggle(_tab == Tab.Timings, "Timings", GUI.skin.button)) _tab = Tab.Timings;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            if (_tab == Tab.Sound) _sound.Draw(_title, _body2, _small);
            else DrawTimings();
            GUILayout.EndArea();
        }

        void DrawTimings()
        {
            _timingScroll = GUILayout.BeginScrollView(_timingScroll);
            if (mugScream != null)
            {
                GUILayout.Label("The mug", _title);
                mugScream.ApproachStart = Slider("Breath starts at (cm from the mug)", mugScream.ApproachStart * 100f, 8f, 60f, "0") / 100f;
                mugScream.SilenceBeat = Slider("Silence before the scream (s)", mugScream.SilenceBeat, 0f, 2f, "0.00");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Touch distance: {mugScream.TouchZone * 100f:0} cm", _body2);
                if (GUILayout.Button("Switch", GUILayout.Width(70))) mugScream.CycleZone();
                GUILayout.EndHorizontal();
            }
            if (dream != null)
            {
                dream.StareBeforeDream = Slider("Stare before the dream starts (s)", dream.StareBeforeDream, 0.5f, 10f, "0.0");
                dream.DeepenTime = Slider("Time to go fully into the dream (s)", dream.DeepenTime, 2f, 30f, "0.0");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Wake-up: {(dream.Wake == DreamState.WakeMode.Scream ? "the mug screams" : "silent")}", _body2);
                if (GUILayout.Button("Switch", GUILayout.Width(70)))
                    dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream;
                GUILayout.EndHorizontal();
            }
            if (painting != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("The painting", _title);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Trigger: {PictureCanvas.ModeName(painting.Mode)}", _body2);
                if (GUILayout.Button("Switch (T)", GUILayout.Width(80))) painting.CycleMode();
                if (GUILayout.Button("Replay (P)", GUILayout.Width(80))) painting.Replay();
                GUILayout.EndHorizontal();
                painting.RevealDuration = Slider("Reveal when found (s)", painting.RevealDuration, 0.5f, 8f, "0.0");
                var st = painting.Stages;
                if (st != null)
                {
                    st.SeenDwell = Slider("Look at it this long before it may change (s)", st.SeenDwell, 0f, 5f, "0.00");
                    st.UnseenTime = Slider("Out of view this long to swap (s)", st.UnseenTime, 0.1f, 5f, "0.00");
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Timings aren't saved. Note the values you like.", _small);
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _body2, GUILayout.Width(300));
            GUILayout.Label(value.ToString(format), _body2, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(value, min, max);
        }
    }
}
