using System.Collections;
using System.Collections.Generic;
using Memories.Audio;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// The dream that comes from holding the mug. Pick it up and keep holding it: first a creeping
    /// dread (the room drains towards grey, its edges glow, echoes ripple out of the mug, whispers
    /// start), then the real room gives way to a dream that leaks out of the cup, until only the real
    /// mug is left. Put the mug down and you drift smoothly back out.
    ///
    /// Two values drive every layer:
    ///  - <see cref="Dread"/> (0..1) rises over <see cref="holdBeforeDream"/> s while the mug is held,
    ///    and sinks again (over <see cref="dreadFade"/> s) if it's put down before the dream begins.
    ///  - <see cref="Depth"/> (0..1) starts when Dread reaches 1 and rises over <see cref="deepenTime"/> s
    ///    while the mug stays held.
    /// Dread: the passthrough drains towards monochrome and darkens, with a slow heartbeat pulse that
    /// quickens; the real room's edges glow red (passthrough edge rendering); <see cref="CupEchoes"/>
    /// ripples the room around the mug and sends ghost copies of it outward; embers start to rise from
    /// the rim; whispers creep in; the background bed sinks.
    /// Dream: as before: a world-locked dome spreads out from the mug, embers, drifting runes, circling
    /// whispers, the scanned room (<see cref="DreamSplatRoom"/>), ghostly hands. The mug's occluder
    /// cuts the real mug out of it, so the mug stays real throughout.
    /// Waking: put the mug down (it must read as down for <see cref="releaseGrace"/> s, so a tracking
    /// blip doesn't count), press Dream on the hand menu, reach <see cref="maxFullTime"/> s at full
    /// depth, or walk more than <see cref="walkWakeDistance"/> m. Everything eases out together over
    /// <see cref="wakeFadeTime"/> s. With the Scream wake-up, a beat of silence then the mug screams.
    /// Comfort: the dream is world-locked and only moves slowly; nothing moves the viewer.
    /// Logs are prefixed [DreamState].
    /// </summary>
    public class DreamState : MonoBehaviour
    {
        public enum WakeMode { Scream, Silent }
        enum Phase { Idle, Dread, Dreaming, Waking }

        [SerializeField] CupDetector detector;
        [SerializeField] MugInterior interior;
        [SerializeField] MugScream mugScream;
        [SerializeField] HandVisibility hands;
        [SerializeField] OVRPassthroughLayer passthrough;
        [Tooltip("Ripples and ghost copies of the mug around it, while it's held.")]
        [SerializeField] CupEchoes echoes;
        [SerializeField] Material domeMaterial;
        [SerializeField] Material emberMaterial;
        [Tooltip("CupSpike/MugRuneGlow with Stencil Comp = Always and ZTest = LEqual.")]
        [SerializeField] Material runeFloatMaterial;
        [Tooltip("Two whispers circling the viewer. Their file, level and pitch live in the Sound Slot (Memories > Sound Design).")]
        [SerializeField] SoundSlot whisperSlot;
        [Tooltip("The second whisper plays at this pitch relative to the first, so they don't phase.")]
        [SerializeField] float secondWhisperPitch = 1.24f;

        [Header("Timing (s)")]
        [Tooltip("Holding the mug this long builds the dread up to the start of the dream.")]
        [SerializeField] float holdBeforeDream = 5f;
        [Tooltip("Put down before the dream starts, the dread sinks away over this.")]
        [SerializeField] float dreadFade = 1.5f;
        [SerializeField] float deepenTime = 8f;
        [SerializeField] float maxFullTime = 30f;
        [Tooltip("The mug must read as put down for this long before you wake (tracking blips don't count).")]
        [SerializeField] float releaseGrace = 0.5f;
        [Tooltip("Waking eases everything out over this.")]
        [SerializeField] float wakeFadeTime = 2.5f;
        [Tooltip("Scream wake-up only: silence between the fade and the scream.")]
        [SerializeField] float wakeSilence = 0.6f;
        [SerializeField] float cooldown = 4f;

        [Header("Dread")]
        [Tooltip("Strongest drain before the dream: brightness, contrast, saturation (each -1..1).")]
        [SerializeField] Vector3 dreadLook = new Vector3(-0.1f, 0.3f, -0.85f);
        [Tooltip("Glow on the real room's edges (passthrough edge rendering); alpha is its strongest.")]
        [SerializeField] Color edgeGlow = new Color(0.85f, 0.12f, 0.08f, 0.55f);
        [Tooltip("Heartbeat pulse rate as the dread starts and at its peak (beats per second).")]
        [SerializeField] Vector2 heartbeat = new Vector2(0.9f, 1.7f);

        [Header("Staying in")]
        [Tooltip("Moving the head further than this from where the dream began wakes you (m).")]
        [SerializeField] float walkWakeDistance = 1.2f;
        [SerializeField] WakeMode wakeMode = WakeMode.Scream;
        [Tooltip("Debug: dream without holding the mug (until woken from the menu).")]
        [SerializeField] bool forceDream;

        public float Depth { get; private set; }
        public float Dread { get; private set; }
        public bool Dreaming => _phase == Phase.Dreaming || _phase == Phase.Waking;
        /// <summary>Anything is showing: the dread, the dream or the wake-up.</summary>
        public bool Active => _phase != Phase.Idle;
        public bool ForceDream { get => forceDream; set => forceDream = value; }
        public WakeMode Wake { get => wakeMode; set => wakeMode = value; }
        public bool Waking => _phase == Phase.Waking;
        public bool InDread => _phase == Phase.Dread;
        // Tuning (used by the desktop debug build's panel).
        public float HoldBeforeDream { get => holdBeforeDream; set => holdBeforeDream = Mathf.Max(0.5f, value); }
        public float DeepenTime { get => deepenTime; set => deepenTime = Mathf.Max(0.5f, value); }
        public float WakeFadeTime { get => wakeFadeTime; set => wakeFadeTime = Mathf.Max(0.1f, value); }
        public string StatusLine =>
            $"dream: {_phase}{(_phase == Phase.Dreaming && Depth >= 1f ? " (DEEP)" : "")}  dread {Dread:0.00}  depth {Depth:0.00}" +
            $"  held {(Held ? "yes" : _releasedSince >= 0f ? $"down {Time.time - _releasedSince:0.0}s" : "no")}" +
            $"  wake: {wakeMode}{(forceDream ? "  FORCED" : "")}";

        Phase _phase;
        float _dreamTime;
        float _releasedSince = -1f;
        float _fullSince = -1f;
        float _cooldownUntil;
        float _beat;
        bool _colourActive, _edgesActive;
        bool _handsWereVisible;
        bool _loggedHalf;
        Vector3 _startHead;
        Coroutine _wake;

        Transform _root, _dome;
        Material _domeMat, _runeMat;
        ParticleSystem _embers;
        readonly List<Transform> _runes = new List<Transform>();
        readonly List<SlotVoice> _whispers = new List<SlotVoice>();

        static readonly int DepthId = Shader.PropertyToID("_Depth"), SpreadId = Shader.PropertyToID("_Spread"),
            VignetteId = Shader.PropertyToID("_Vignette"), MugDirId = Shader.PropertyToID("_MugDir"),
            HeadFwdId = Shader.PropertyToID("_HeadFwd"), RevealId = Shader.PropertyToID("_Reveal");

        bool Held => detector != null && detector.TrackingSource == CupDetector.Source.Hand;

        void Start()
        {
            Build();
            Apply(Camera.main);
            Debug.Log($"[DreamState] Ready: dread over {holdBeforeDream:0.0} s of holding the mug, deep after {deepenTime:0} s more, " +
                      $"put it down to wake ({wakeFadeTime:0.0} s fade), wake: {wakeMode}");
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float dt = Time.deltaTime;

            bool holding = Held || forceDream;
            if (holding) _releasedSince = -1f;
            else if (_releasedSince < 0f) _releasedSince = Time.time;
            bool putDown = !holding && Time.time - _releasedSince > releaseGrace;

            switch (_phase)
            {
                case Phase.Idle:
                    bool canStart = Time.time > _cooldownUntil && (mugScream == null || !mugScream.ScreamPlaying);
                    if (holding && canStart) BeginDread();
                    break;

                case Phase.Dread:
                    if (holding) Dread += dt / (forceDream ? Mathf.Min(1f, holdBeforeDream) : holdBeforeDream);
                    else if (putDown) Dread -= dt / dreadFade;
                    if (Dread >= 1f) { Dread = 1f; BeginDream(cam); }
                    else if (Dread <= 0f) { Dread = 0f; EndAll(); Debug.Log("[DreamState] Put down before the dream: the dread fades"); }
                    break;

                case Phase.Dreaming:
                    if (holding) _dreamTime += dt;
                    Depth = Mathf.Max(0.001f, Mathf.Clamp01(_dreamTime / deepenTime));
                    if (!_loggedHalf && Depth >= 0.5f) { _loggedHalf = true; Debug.Log("[DreamState] The dream is spreading (half way)"); }
                    if (Depth >= 1f && _fullSince < 0f) { _fullSince = Time.time; Debug.Log("[DreamState] Fully in the dream"); }

                    var moved = Vector3.ProjectOnPlane(cam.transform.position - _startHead, Vector3.up).magnitude;
                    if (putDown) StartWake("put the mug down");
                    else if (_fullSince >= 0f && Time.time - _fullSince > maxFullTime) StartWake($"time limit ({maxFullTime:0} s)");
                    else if (moved > walkWakeDistance) StartWake($"walked {moved:0.00} m");
                    break;
            }
            if (_phase != Phase.Waking) Apply(cam);
        }

        void BeginDread()
        {
            _phase = Phase.Dread;
            Dread = 0f;
            Depth = 0f;
            _beat = 0f;
            if (mugScream != null) mugScream.Suppressed = true;
            if (hands != null) _handsWereVisible = hands.Visible;
            // The whispers and embers live under the dream root: switch it on first, or Play() is refused
            // ("Can not play a disabled audio source") and the whispers stay silent.
            _root.gameObject.SetActive(true);
            _embers.Play();
            for (int i = 0; i < _whispers.Count; i++)
            {
                var w = _whispers[i];
                w.Level = 0f;
                if (!w.Play()) continue;
                if (i > 0 && w.Source.clip.samples > 0) w.Source.timeSamples = (i * 37000) % w.Source.clip.samples;   // out of step with each other
            }
            Debug.Log($"[DreamState] Mug picked up{(forceDream ? " (forced)" : "")}: the dread begins");
        }

        void BeginDream(Camera cam)
        {
            _phase = Phase.Dreaming;
            _startHead = cam.transform.position;
            _dreamTime = 0f;
            _fullSince = -1f;
            _loggedHalf = false;
            Depth = 0.001f;
            if (interior != null) interior.HoldOpen = true;
            Debug.Log("[DreamState] Dream begins");
        }

        /// <summary>Wake now (e.g. from the hand menu): from the dread or the dream.</summary>
        public void WakeNow()
        {
            if (_phase == Phase.Dread || _phase == Phase.Dreaming) StartWake("woken from the menu");
        }

        void StartWake(string why)
        {
            if (_phase == Phase.Waking) return;
            _phase = Phase.Waking;
            Debug.Log($"[DreamState] Waking: {why} (dread {Dread:0.00}, depth {Depth:0.00}, {_dreamTime:0.0} s in the dream)");
            _wake = StartCoroutine(WakeUp());
        }

        IEnumerator WakeUp()
        {
            float fromDepth = Depth, fromDread = Dread;
            bool scream = wakeMode == WakeMode.Scream && fromDepth > 0f;
            for (float t = 0f; t < wakeFadeTime; t += Time.deltaTime)
            {
                // Ease out: the dream lets go gently, faster at the end.
                float k = 1f - Mathf.SmoothStep(0f, 1f, t / wakeFadeTime);
                Depth = fromDepth > 0f ? Mathf.Max(0.001f, fromDepth * k) : 0f;
                Dread = fromDread * k;
                Apply(Camera.main);
                if (mugScream != null) mugScream.ExternalDuck = scream ? Mathf.Min(mugScream.ExternalDuck, k) : Mathf.Lerp(1f, mugScream.ExternalDuck, k);
                yield return null;
            }
            Depth = 0f;
            Dread = 0f;
            Apply(Camera.main);
            if (interior != null) interior.HoldOpen = false;

            if (scream && mugScream != null)
            {
                // A beat of total silence, then the payoff.
                mugScream.ExternalDuck = 0f;
                yield return new WaitForSeconds(wakeSilence);
                mugScream.ExternalDuck = 1f;
                mugScream.Suppressed = false;
                mugScream.Scream("woke from the dream");
            }
            EndAll();
            _cooldownUntil = Time.time + cooldown;
            _wake = null;
            Debug.Log("[DreamState] Awake");
        }

        /// <summary>Back to idle: everything off.</summary>
        void EndAll()
        {
            _phase = Phase.Idle;
            Depth = 0f;
            Dread = 0f;
            forceDream = false;
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var w in _whispers) w.Stop();
            if (interior != null) interior.HoldOpen = false;
            if (hands != null) hands.Visible = _handsWereVisible;
            if (mugScream != null) { mugScream.ExternalDuck = 1f; mugScream.Suppressed = false; }
            Apply(Camera.main);
        }

        // ------------------------------------------------------------ layers

        void Apply(Camera cam)
        {
            float d = Depth;
            float dr = Mathf.SmoothStep(0f, 1f, Dread);
            float e = Smooth(0f, 0.4f, d);

            // Heartbeat: a double thump that quickens as the dread builds, fading out into the dream.
            _beat += Time.deltaTime * Mathf.Lerp(heartbeat.x, heartbeat.y, dr);
            float b = Beat(_beat % 1f) * dr * (1f - e);

            // Room: drains towards grey while the mug is held, then all the way as the dream starts.
            bool coloured = dr > 0f || d > 0f;
            if (coloured)
            {
                float bright = Mathf.Min(dreadLook.x * dr - 0.08f * b, -0.35f * e);
                float contrast = Mathf.Lerp(dreadLook.y * dr, 0.15f, e);
                float sat = Mathf.Min(dreadLook.z * dr, -1f * e);
                RoomLook.Set(passthrough, bright, contrast, sat);
                _colourActive = true;
            }
            else if (_colourActive) { RoomLook.Clear(passthrough); _colourActive = false; }

            // The real room's edges glow with the pulse, then sink under the dream.
            float edge = edgeGlow.a * dr * (0.55f + 0.45f * b) * (1f - Smooth(0.3f, 0.8f, d));
            if (edge > 0.003f) { RoomLook.SetEdges(passthrough, new Color(edgeGlow.r, edgeGlow.g, edgeGlow.b, edge)); _edgesActive = true; }
            else if (_edgesActive) { RoomLook.ClearEdges(passthrough); _edgesActive = false; }

            // Echoes from the mug: build with the dread, pulse with it, settle lower in the dream.
            if (echoes != null) echoes.Intensity = Mathf.Max(dr * (0.8f + 0.2f * b) * (1f - 0.4f * e), 0f);

            if (mugScream != null && _phase != Phase.Waking)
                mugScream.ExternalDuck = 1f - Mathf.Max(0.5f * dr, 0.8f * Smooth(0.2f, 0.8f, d));

            bool on = _phase != Phase.Idle;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on || cam == null) return;
            bool dome = d > 0f;
            if (_dome.gameObject.activeSelf != dome) _dome.gameObject.SetActive(dome);

            var head = cam.transform;
            var mug = interior != null ? interior.RimCentre : head.position + head.forward;

            // Dream dome: centred on the head, spreading from the mug's direction.
            _dome.position = head.position;
            _domeMat.SetFloat(DepthId, d);
            // Slow at first, so the dream visibly creeps out of the cup over ~4 s before filling the view.
            _domeMat.SetFloat(SpreadId, 3.4f * Mathf.Pow(Mathf.InverseLerp(0.1f, 0.95f, d), 2.2f));
            _domeMat.SetFloat(VignetteId, Mathf.Max(0.85f * Smooth(0f, 0.5f, d), 0.35f * dr));
            _domeMat.SetVector(MugDirId, (mug - head.position).normalized);
            _domeMat.SetVector(HeadFwdId, head.forward);

            // Embers from the rim: a few as the dread builds, more in the dream.
            _embers.transform.position = mug;
            var em = _embers.emission;
            em.rateOverTime = Mathf.Max(6f * dr, 25f * Smooth(0.1f, 0.6f, d));

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

            // Whispers: creeping in with the dread, circling in the dream.
            for (int i = 0; i < _whispers.Count; i++)
            {
                float a = Time.time * (i == 0 ? 0.35f : -0.25f) + i * Mathf.PI;
                _whispers[i].Source.transform.position = head.position + new Vector3(Mathf.Cos(a) * 1.3f, 0.1f, Mathf.Sin(a) * 1.3f);
                _whispers[i].Level = Mathf.Max(0.3f * dr * dr, Smooth(0.35f, 0.9f, d));
                _whispers[i].Tick();
            }

            // Ghostly hands once the room is mostly gone.
            if (hands != null && _phase != Phase.Waking) hands.Visible = d > 0.6f || _handsWereVisible;
        }

        /// <summary>A heartbeat over one cycle (0..1): a thump, then a softer second one.</summary>
        static float Beat(float x)
        {
            float first = Mathf.Exp(-x * 14f);
            float y = x - 0.22f;
            float second = y > 0f ? 0.6f * Mathf.Exp(-y * 16f) : 0f;
            return Mathf.Clamp01(first + second);
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

            for (int i = 0; i < 2; i++)
            {
                var w = SlotVoice.Create(whisperSlot, _root, "Whisper" + i, true);
                w.PitchScale = i == 0 ? 1f : secondWhisperPitch;
                _whispers.Add(w);
            }
            _root.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (_colourActive) RoomLook.Clear(passthrough);
            if (_edgesActive) RoomLook.ClearEdges(passthrough);
            _colourActive = _edgesActive = false;
        }
    }
}
