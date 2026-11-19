using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace CupSpike
{
    /// <summary>
    /// Desktop debug mode (scene 03-CupDesktop, macOS app): try the mug's interaction phases and
    /// sound design with a mouse and keyboard, no headset. The scream, look-inside and dream code is
    /// the same as on the headset; this only stands in for the things the headset provides:
    ///  - the mug's pose (fixed on a table, fed to CupDetector.Simulate)
    ///  - the head (mouse look; E leans over the mug and back)
    ///  - a hand (Up / Down or the scroll wheel move a fingertip towards the mug and away)
    ///  - the real room (a simple stand-in room, tinted through RoomLook instead of passthrough)
    ///
    /// On-screen guide: the current phase in plain words, the controls, and (Tab) a tuning panel with
    /// volumes and timings. Sound files dropped into ~/MugThatScreams/Sounds replace the built-in ones
    /// (scream, breath, drone, whisper, background; wav, mp3, ogg or aiff). L reloads them.
    /// </summary>
    public class DesktopSim : MonoBehaviour
    {
        [SerializeField] CupDetector detector;
        [SerializeField] MugScream mugScream;
        [SerializeField] MugInterior interior;
        [SerializeField] DreamState dream;
        [SerializeField] Camera cam;
        [SerializeField] Material handMaterial;

        [Header("Layout (m)")]
        [Tooltip("Centre of the mug's base, on the table.")]
        [SerializeField] Vector3 mugBase = new Vector3(0f, 0.75f, 0f);
        [SerializeField] Vector3 headRest = new Vector3(0f, 1.32f, -0.62f);
        [Tooltip("Head position when leaning over the mug.")]
        [SerializeField] Vector3 headLean = new Vector3(0f, 1.09f, -0.11f);
        [Tooltip("Fingertip when the hand is fully withdrawn.")]
        [SerializeField] Vector3 handRest = new Vector3(0.3f, 0.9f, -0.38f);

        [Header("Feel")]
        [SerializeField] float lookSensitivity = 0.08f;
        [SerializeField] float reachSpeed = 0.45f;
        [SerializeField] float leanTime = 0.6f;

        float _yaw, _pitch;
        float _lean;            // 0 = standing back, 1 = over the mug
        bool _leanTarget;
        float _reach;           // 0 = withdrawn, 1 = touching the mug
        bool _hud = true, _panel;
        Transform _hand;
        string _soundDir;
        readonly List<string> _loaded = new List<string>();
        string _soundNote = "";
        GUIStyle _title, _body, _small, _phase;

        static readonly string[] SoundNames = { "scream", "breath", "drone", "whisper", "background" };

        void Start()
        {
            if (cam == null) cam = Camera.main;
            cam.transform.position = headRest;
            FaceMug();
            BuildHand();
            if (mugScream != null) mugScream.ShowZone = false;

            _soundDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MugThatScreams", "Sounds");
            try
            {
                Directory.CreateDirectory(_soundDir);
                var readme = Path.Combine(_soundDir, "README.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "Drop sound files here to replace the built-in ones, then press L in the app (or restart it).\n\n" +
                        "File names (wav, mp3, ogg or aiff):\n" +
                        "  scream      the mug's scream when touched (or when you wake from the dream)\n" +
                        "  breath      loops from inside the mug while a hand approaches\n" +
                        "  drone       loops from inside the mug while you look into it\n" +
                        "  whisper     loops, circling you, during the dream\n" +
                        "  background  the room's background loop\n\n" +
                        "Example: scream.wav, breath.mp3. Remove a file and restart the app to get the built-in sound back.\n");
            }
            catch (Exception e) { _soundNote = "Couldn't create the sounds folder: " + e.Message; }
            StartCoroutine(LoadSounds());
            Debug.Log($"[DesktopSim] Desktop debug mode. Sounds folder: {_soundDir}");
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

        void FaceMug()
        {
            var dir = Rim - cam.transform.position;
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = -Mathf.Asin(dir.normalized.y) * Mathf.Rad2Deg;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // Cursor: click to look around, Esc (or Tab for the panel) to free it.
            if (kb.tabKey.wasPressedThisFrame) { _panel = !_panel; Cursor.lockState = _panel ? CursorLockMode.None : CursorLockMode.Locked; }
            if (kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (!_panel && mouse.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue();
                _yaw += d.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - d.y * lookSensitivity, -85f, 85f);
            }

            // Lean over the mug and back.
            if (kb.eKey.wasPressedThisFrame)
            {
                _leanTarget = !_leanTarget;
                StartCoroutine(AimAtMugAfterLean());
            }
            _lean = Mathf.MoveTowards(_lean, _leanTarget ? 1f : 0f, Time.deltaTime / leanTime);
            cam.transform.position = Vector3.Lerp(headRest, headLean, Mathf.SmoothStep(0f, 1f, _lean));
            cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Hand: reach in and out.
            float move = (kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed || kb.sKey.isPressed ? 1f : 0f);
            _reach = Mathf.Clamp01(_reach + move * reachSpeed * Time.deltaTime + mouse.scroll.ReadValue().y * 0.0006f);
            var tipPos = Vector3.Lerp(handRest, TouchPoint, _reach);
            _hand.SetPositionAndRotation(tipPos, Quaternion.LookRotation(TouchPoint - handRest));
            if (mugScream != null) mugScream.SimulatedFingertip = tipPos;

            if (detector != null) detector.Simulate(new Pose(mugBase, Quaternion.Euler(0f, 180f, 0f)), false);

            // Shortcuts.
            if (kb.hKey.wasPressedThisFrame) _hud = !_hud;
            if (kb.zKey.wasPressedThisFrame && mugScream != null) mugScream.ShowZone = !mugScream.ShowZone;
            if (kb.lKey.wasPressedThisFrame) StartCoroutine(LoadSounds());
            if (dream != null)
            {
                if (kb.dKey.wasPressedThisFrame) { if (dream.Dreaming) dream.WakeNow(); else dream.ForceDream = true; }
                if (kb.mKey.wasPressedThisFrame)
                    dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream;
            }
            if (kb.spaceKey.wasPressedThisFrame && mugScream != null) mugScream.Scream("space bar");
        }

        IEnumerator AimAtMugAfterLean()
        {
            // Turn the view to the mug as the head moves, so leaning in ends up looking into it.
            float t = 0f, y0 = _yaw, p0 = _pitch;
            var end = _leanTarget ? headLean : headRest;
            var dir = Rim - end;
            float y1 = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, p1 = -Mathf.Asin(dir.normalized.y) * Mathf.Rad2Deg;
            while (t < leanTime)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / leanTime);
                _yaw = Mathf.LerpAngle(y0, y1, k);
                _pitch = Mathf.Lerp(p0, p1, k);
                yield return null;
            }
        }

        // ------------------------------------------------------------ sounds

        IEnumerator LoadSounds()
        {
            _loaded.Clear();
            if (!Directory.Exists(_soundDir)) yield break;
            foreach (var name in SoundNames)
            {
                string path = null;
                foreach (var ext in new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif" })
                {
                    var p = Path.Combine(_soundDir, name + ext);
                    if (File.Exists(p)) { path = p; break; }
                }
                if (path == null) continue;
                var ext2 = Path.GetExtension(path).ToLowerInvariant();
                var type = ext2 == ".wav" ? AudioType.WAV : ext2 == ".mp3" ? AudioType.MPEG : ext2 == ".ogg" ? AudioType.OGGVORBIS : AudioType.AIFF;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { _loaded.Add($"{name}: FAILED ({req.error})"); continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null || clip.length <= 0f) { _loaded.Add($"{name}: FAILED (couldn't read the file)"); continue; }
                    clip.name = Path.GetFileName(path);
                    switch (name)
                    {
                        case "scream": mugScream?.SetScream(clip); break;
                        case "breath": mugScream?.SetBreath(clip); break;
                        case "drone": interior?.SetDrone(clip); break;
                        case "whisper": dream?.SetWhisper(clip); break;
                        case "background": mugScream?.SetBackground(clip); break;
                    }
                    _loaded.Add($"{name}: {clip.name} ({clip.length:0.0} s)");
                    Debug.Log($"[DesktopSim] Loaded {name} from {path} ({clip.length:0.0} s, {clip.channels} ch)");
                }
            }
            _soundNote = _loaded.Count == 0 ? "Using the built-in sounds" : "";
        }

        // ------------------------------------------------------------ on-screen guide

        string Phase()
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

        void OnGUI()
        {
            float s = Mathf.Max(1f, Screen.height / 800f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s, h = Screen.height / s;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                _phase = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
                _phase.normal.textColor = new Color(1f, 0.55f, 0.4f);
                _body = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
                _small.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            }

            if (!_hud)
            {
                GUI.Label(new Rect(10, h - 22, 300, 20), "H: show the guide", _small);
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 360, h - 24), GUI.skin.box);
            GUILayout.Label("The Mug That Screams: desktop debug", _title);
            GUILayout.Label(Phase(), _phase);
            GUILayout.Space(6);
            GUILayout.Label(
                "<b>Mouse</b>  look around (click to start, Esc to free the cursor)\n" +
                "<b>Up / Down</b> or scroll  move the hand towards the mug / away\n" +
                "<b>E</b>  lean over the mug / lean back\n" +
                "<b>D</b>  start the dream now / wake up\n" +
                "<b>M</b>  wake-up: scream or silent\n" +
                "<b>Space</b>  scream now\n" +
                "<b>Tab</b>  volumes and timings    <b>L</b>  reload sounds\n" +
                "<b>Z</b>  show zones    <b>H</b>  hide this guide", _body);
            GUILayout.Space(6);
            GUILayout.Label(
                "<b>Things to try</b>\n" +
                "1. Reach slowly: the mug breathes. Pull back: it stops dead.\n" +
                "2. Reach all the way: silence, then the scream.\n" +
                "3. Press E and look into the mug: the tunnel opens.\n" +
                "4. Keep staring about 2 s: the dream spreads from the cup.\n" +
                "5. Look away (move the mouse): you wake.", _body);
            GUILayout.FlexibleSpace();
            if (mugScream != null) GUILayout.Label(mugScream.StatusLine, _small);
            if (interior != null) GUILayout.Label(interior.StatusLine, _small);
            if (dream != null) GUILayout.Label(dream.StatusLine, _small);
            GUILayout.EndArea();

            if (_panel) DrawPanel(w, h);
        }

        void DrawPanel(float w, float h)
        {
            GUILayout.BeginArea(new Rect(w - 332, 12, 320, h - 24), GUI.skin.box);
            GUILayout.Label("Volumes and timings", _title);
            if (mugScream != null)
            {
                mugScream.ScreamVolume = Slider("Scream volume", mugScream.ScreamVolume, 0f, 1f, "0.00");
                mugScream.BreathVolume = Slider("Breath volume", mugScream.BreathVolume, 0f, 1f, "0.00");
                mugScream.BackgroundVolume = Slider("Background volume", mugScream.BackgroundVolume, 0f, 1f, "0.00");
            }
            if (interior != null) interior.DroneVolume = Slider("Drone volume (looking in)", interior.DroneVolume, 0f, 1f, "0.00");
            if (dream != null) dream.WhisperVolume = Slider("Whisper volume (dream)", dream.WhisperVolume, 0f, 1f, "0.00");
            GUILayout.Space(8);
            if (mugScream != null)
            {
                mugScream.ApproachStart = Slider("Breath starts at (cm from the mug)", mugScream.ApproachStart * 100f, 8f, 60f, "0") / 100f;
                mugScream.SilenceBeat = Slider("Silence before the scream (s)", mugScream.SilenceBeat, 0f, 2f, "0.00");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Touch distance: {mugScream.TouchZone * 100f:0} cm", _body);
                if (GUILayout.Button("Switch", GUILayout.Width(70))) mugScream.CycleZone();
                GUILayout.EndHorizontal();
            }
            if (dream != null)
            {
                dream.StareBeforeDream = Slider("Stare before the dream starts (s)", dream.StareBeforeDream, 0.5f, 10f, "0.0");
                dream.DeepenTime = Slider("Time to go fully into the dream (s)", dream.DeepenTime, 2f, 30f, "0.0");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Wake-up: {(dream.Wake == DreamState.WakeMode.Scream ? "the mug screams" : "silent")}", _body);
                if (GUILayout.Button("Switch", GUILayout.Width(70)))
                    dream.Wake = dream.Wake == DreamState.WakeMode.Scream ? DreamState.WakeMode.Silent : DreamState.WakeMode.Scream;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            GUILayout.Label("Your own sounds", _title);
            GUILayout.Label("Put scream, breath, drone, whisper or background (wav, mp3, ogg, aiff) in the sounds folder, then reload.", _body);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open sounds folder")) Application.OpenURL(new Uri(_soundDir).AbsoluteUri);
            if (GUILayout.Button("Reload (L)")) StartCoroutine(LoadSounds());
            GUILayout.EndHorizontal();
            foreach (var line in _loaded) GUILayout.Label(line, _small);
            if (_soundNote != "") GUILayout.Label(_soundNote, _small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Settings changed here aren't saved. Note the values you like.", _small);
            GUILayout.EndArea();
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _body, GUILayout.Width(200));
            GUILayout.Label(value.ToString(format), _body, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(value, min, max);
        }
    }
}
