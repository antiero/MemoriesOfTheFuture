using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// The hypnotic dream: keep staring into the mug and the real room gives way to a dream that
    /// leaks out of the cup, until only the real mug is left. Look away (or after a time limit, or
    /// if you walk) and you wake with a jolt.
    ///
    /// One value, <see cref="Depth"/> (0..1), drives every layer. It starts after
    /// <see cref="stareBeforeDream"/> s of looking into the open mug and reaches 1 over
    /// <see cref="deepenTime"/> s while you keep watching the mug.
    ///  - Room: passthrough drains (colour, brightness), and a vignette closes in from the edges.
    ///  - Dream: a world-locked dome (CupSpike/DreamDome) spreads out from the mug's direction until
    ///    it fills the view: a dark void, faint twinkling specks and a slow spiral centred on the mug.
    ///    The mug's occluder cuts the real mug out of it, so the mug stays real throughout.
    ///  - Embers rise from the cup; copies of the rune drift around you; two whispers circle you.
    ///  - The background bed muffles; ghostly hand outlines appear, so you aren't disembodied.
    /// Waking: look away from the mug for <see cref="lookAwayToWake"/> s, reach
    /// <see cref="maxFullTime"/> s at full depth, or move more than <see cref="walkLimit"/> m.
    /// The dream snaps away, a beat of silence, then (by default) the mug screams.
    /// Comfort: the dream is world-locked and only moves slowly; nothing moves the viewer.
    /// Logs are prefixed [DreamState].
    /// </summary>
    public class DreamState : MonoBehaviour
    {
        public enum WakeMode { Scream, Silent }

        [SerializeField] CupDetector detector;
        [SerializeField] MugInterior interior;
        [SerializeField] MugScream mugScream;
        [SerializeField] HandVisibility hands;
        [SerializeField] OVRPassthroughLayer passthrough;
        [SerializeField] Material domeMaterial;
        [SerializeField] Material emberMaterial;
        [Tooltip("CupSpike/MugRuneGlow with Stencil Comp = Always and ZTest = LEqual.")]
        [SerializeField] Material runeFloatMaterial;
        [Tooltip("Whispers circling the viewer. Placeholder: the breath, pitched down.")]
        [SerializeField] AudioClip whisper;
        [SerializeField, Range(0f, 1f)] float whisperVolume = 0.6f;

        [Header("Timing (s)")]
        [SerializeField] float stareBeforeDream = 2f;
        [SerializeField] float deepenTime = 8f;
        [SerializeField] float maxFullTime = 20f;
        [SerializeField] float lookAwayToWake = 1f;
        [SerializeField] float wakeTime = 0.25f;
        [SerializeField] float wakeSilence = 0.6f;
        [SerializeField] float cooldown = 6f;

        [Header("Staying in")]
        [Tooltip("While dreaming, you stay in as long as your gaze is within this of the mug (degrees).")]
        [SerializeField] float stayGazeAngle = 35f;
        [Tooltip("Moving the head further than this from where the dream began wakes you (m).")]
        [SerializeField] float walkLimit = 0.4f;
        [SerializeField] WakeMode wakeMode = WakeMode.Scream;
        [Tooltip("Debug: dream without looking into the mug (and don't wake from looking away).")]
        [SerializeField] bool forceDream;

        public float Depth { get; private set; }
        public bool Dreaming => Depth > 0f || _waking;
        public bool ForceDream { get => forceDream; set => forceDream = value; }
        public WakeMode Wake { get => wakeMode; set => wakeMode = value; }
        public bool Waking => _waking;
        // Tuning (used by the desktop debug build's panel).
        public float StareBeforeDream { get => stareBeforeDream; set => stareBeforeDream = value; }
        public float DeepenTime { get => deepenTime; set => deepenTime = Mathf.Max(0.5f, value); }
        public float WhisperVolume { get => whisperVolume; set => whisperVolume = value; }
        public void SetWhisper(AudioClip clip)
        {
            whisper = clip;
            foreach (var w in _whispers) { bool playing = w.isPlaying; w.clip = clip; if (playing) w.Play(); }
        }
        public string StatusLine =>
            $"dream: {(_waking ? "WAKING" : Depth > 0f ? (Depth >= 1f ? "DEEP" : "dreaming") : _stare > 0f ? "staring" : "-")}" +
            $"  depth {Depth:0.00}  stare {_stare:0.0}s{(_awaySince >= 0f ? $"  away {Time.time - _awaySince:0.0}s" : "")}" +
            $"  wake: {wakeMode}{(forceDream ? "  FORCED" : "")}";

        float _stare;
        float _awaySince = -1f;
        float _fullSince = -1f;
        float _cooldownUntil;
        bool _waking;
        bool _colourActive;
        bool _handsWereVisible;
        bool _loggedHalf;
        Vector3 _startHead;

        Transform _root, _dome;
        Material _domeMat, _runeMat;
        ParticleSystem _embers;
        readonly List<Transform> _runes = new List<Transform>();
        readonly List<AudioSource> _whispers = new List<AudioSource>();

        static readonly int DepthId = Shader.PropertyToID("_Depth"), SpreadId = Shader.PropertyToID("_Spread"),
            VignetteId = Shader.PropertyToID("_Vignette"), MugDirId = Shader.PropertyToID("_MugDir"),
            HeadFwdId = Shader.PropertyToID("_HeadFwd"), RevealId = Shader.PropertyToID("_Reveal");

        void Start()
        {
            Build();
            Apply(0f, Camera.main);
            Debug.Log($"[DreamState] Ready: dream after {stareBeforeDream:0.0} s of staring, deep after {deepenTime:0} s more, wake: {wakeMode}");
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || interior == null || _waking) return;
            float dt = Time.deltaTime;

            if (Depth <= 0f)
            {
                bool canStart = Time.time > _cooldownUntil && detector != null && detector.Tracking &&
                                (mugScream == null || !mugScream.ScreamPlaying);
                bool lookingIn = interior.LookingIn && interior.Reveal > 0.95f;
                _stare = canStart && (lookingIn || forceDream) ? _stare + dt : Mathf.Max(0f, _stare - 2f * dt);
                if (_stare > stareBeforeDream) Begin(cam);
            }
            else
            {
                bool watching = forceDream || interior.GazeAngle < stayGazeAngle;
                if (watching) _awaySince = -1f;
                else if (_awaySince < 0f) _awaySince = Time.time;
                _stare += dt;

                float target = Mathf.Clamp01((_stare - stareBeforeDream) / deepenTime);
                Depth = Mathf.Max(0.001f, target);
                if (!_loggedHalf && Depth >= 0.5f) { _loggedHalf = true; Debug.Log("[DreamState] The dream is spreading (half way)"); }
                if (Depth >= 1f && _fullSince < 0f) { _fullSince = Time.time; Debug.Log("[DreamState] Fully in the dream"); }

                var moved = Vector3.ProjectOnPlane(cam.transform.position - _startHead, Vector3.up).magnitude;
                if (_awaySince >= 0f && Time.time - _awaySince > lookAwayToWake) StartCoroutine(WakeUp("looked away"));
                else if (_fullSince >= 0f && Time.time - _fullSince > maxFullTime) StartCoroutine(WakeUp($"time limit ({maxFullTime:0} s)"));
                else if (moved > walkLimit) StartCoroutine(WakeUp($"walked {moved:0.00} m"));
            }
            Apply(Depth, cam);
        }

        void Begin(Camera cam)
        {
            _startHead = cam.transform.position;
            _awaySince = -1f;
            _fullSince = -1f;
            _loggedHalf = false;
            Depth = 0.001f;
            if (interior != null) interior.HoldOpen = true;
            if (mugScream != null) mugScream.Suppressed = true;
            if (hands != null) _handsWereVisible = hands.Visible;
            _embers.Play();
            foreach (var w in _whispers) w.Play();
            Debug.Log($"[DreamState] Dream begins after {_stare:0.0} s of staring");
        }

        /// <summary>Wake now (e.g. from the hand menu).</summary>
        public void WakeNow() { if (Depth > 0f && !_waking) StartCoroutine(WakeUp("woken from the menu")); }

        IEnumerator WakeUp(string why)
        {
            _waking = true;
            Debug.Log($"[DreamState] Waking: {why} (depth {Depth:0.00}, {_stare - stareBeforeDream:0.0} s in the dream)");
            float from = Depth;
            for (float t = 0f; t < wakeTime; t += Time.deltaTime)
            {
                Depth = Mathf.Lerp(from, 0f, t / wakeTime);
                Apply(Depth, Camera.main);
                yield return null;
            }
            Depth = 0f;
            Apply(0f, Camera.main);
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var w in _whispers) w.Stop();
            if (interior != null) interior.HoldOpen = false;
            if (hands != null) hands.Visible = _handsWereVisible;

            // A beat of total silence, then the payoff.
            if (mugScream != null) mugScream.ExternalDuck = 0f;
            yield return new WaitForSeconds(wakeSilence);
            if (mugScream != null)
            {
                mugScream.ExternalDuck = 1f;
                mugScream.Suppressed = false;
                if (wakeMode == WakeMode.Scream) mugScream.Scream("woke from the dream");
            }
            _stare = 0f;
            _cooldownUntil = Time.time + cooldown;
            forceDream = false;
            _waking = false;
        }

        // ------------------------------------------------------------ layers

        void Apply(float d, Camera cam)
        {
            float e = Smooth(0f, 0.4f, d);

            // Room: passthrough drains as the dream starts.
            if (d > 0f) { RoomLook.Set(passthrough, -0.35f * e, 0.15f * e, -1f * e); _colourActive = true; }
            else if (_colourActive) { RoomLook.Clear(passthrough); _colourActive = false; }

            if (mugScream != null && !_waking) mugScream.ExternalDuck = 1f - 0.8f * Smooth(0.2f, 0.8f, d);

            bool on = d > 0f;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on || cam == null) return;

            var head = cam.transform;
            var mug = interior != null ? interior.RimCentre : head.position + head.forward;

            // Dream dome: centred on the head, spreading from the mug's direction.
            _dome.position = head.position;
            _domeMat.SetFloat(DepthId, d);
            // Slow at first, so the dream visibly creeps out of the cup over ~4 s before filling the view.
            _domeMat.SetFloat(SpreadId, 3.4f * Mathf.Pow(Mathf.InverseLerp(0.1f, 0.95f, d), 2.2f));
            _domeMat.SetFloat(VignetteId, 0.85f * Smooth(0f, 0.5f, d));
            _domeMat.SetVector(MugDirId, (mug - head.position).normalized);
            _domeMat.SetVector(HeadFwdId, head.forward);

            // Embers from the rim.
            _embers.transform.position = mug;
            var em = _embers.emission;
            em.rateOverTime = 25f * Smooth(0.1f, 0.6f, d);

            // Runes drifting around the viewer, facing them.
            _runeMat.SetFloat(RevealId, 0.8f * Smooth(0.5f, 0.9f, d));
            for (int i = 0; i < _runes.Count; i++)
            {
                float a = i * Mathf.PI * 2f / _runes.Count + Time.time * 0.05f * (i % 2 == 0 ? 1f : -0.7f);
                float r = 1.7f + 0.5f * Mathf.Sin(i * 1.7f);
                float y = -0.3f + 1.1f * ((i * 0.37f) % 1f) + 0.08f * Mathf.Sin(Time.time * 0.3f + i);
                var p = head.position + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                _runes[i].position = p;
                _runes[i].rotation = Quaternion.LookRotation(p - head.position, Vector3.up);
            }

            // Whispers circling.
            for (int i = 0; i < _whispers.Count; i++)
            {
                float a = Time.time * (i == 0 ? 0.35f : -0.25f) + i * Mathf.PI;
                _whispers[i].transform.position = head.position + new Vector3(Mathf.Cos(a) * 1.3f, 0.1f, Mathf.Sin(a) * 1.3f);
                _whispers[i].volume = whisperVolume * Smooth(0.35f, 0.9f, d);
            }

            // Ghostly hands once the room is mostly gone.
            if (hands != null && !_waking) hands.Visible = d > 0.6f || _handsWereVisible;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        // ------------------------------------------------------------ build

        void Build()
        {
            _root = new GameObject("Dream").transform;
            _root.SetParent(transform, false);

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dome.GetComponent<Collider>());
            dome.name = "DreamDome";
            _dome = dome.transform;
            _dome.SetParent(_root, false);
            _dome.localScale = Vector3.one * 12f;
            var dr = dome.GetComponent<Renderer>();
            _domeMat = new Material(domeMaterial);
            dr.sharedMaterial = _domeMat;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dr.receiveShadows = false;

            var eg = new GameObject("Embers");
            eg.transform.SetParent(_root, false);
            _embers = eg.AddComponent<ParticleSystem>();
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _embers.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.15f, 0.9f), new Color(1f, 0.2f, 0.05f, 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.01f;
            main.maxParticles = 300;
            var em = _embers.emission;
            em.rateOverTime = 0f;
            var shape = _embers.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.03f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var noise = _embers.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.4f;
            var col = _embers.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = eg.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = emberMaterial;
            pr.renderMode = ParticleSystemRenderMode.Billboard;

            _runeMat = new Material(runeFloatMaterial);
            for (int i = 0; i < 6; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "FloatingRune" + i;
                q.transform.SetParent(_root, false);
                q.transform.localScale = Vector3.one * (0.28f + 0.1f * (i % 3));
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = _runeMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _runes.Add(q.transform);
            }

            if (whisper != null)
                for (int i = 0; i < 2; i++)
                {
                    var wg = new GameObject("Whisper" + i);
                    wg.transform.SetParent(_root, false);
                    var a = wg.AddComponent<AudioSource>();
                    a.clip = whisper;
                    a.loop = true;
                    a.pitch = i == 0 ? 0.5f : 0.62f;
                    a.volume = 0f;
                    a.spatialize = true;
                    a.spatialBlend = 1f;
                    a.minDistance = 0.5f;
                    a.playOnAwake = false;
                    a.timeSamples = i * 37000;   // out of step with each other
                    wg.AddComponent<MetaXRAudioSource>();
                    _whispers.Add(a);
                }
            _root.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (_colourActive) RoomLook.Clear(passthrough);
        }
    }
}
