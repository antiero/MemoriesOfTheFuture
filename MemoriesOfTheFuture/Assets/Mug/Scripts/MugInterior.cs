using Memories.Audio;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// "Look inside the mug": when the visitor looks down into the real mug, something happens at the
    /// opening (<see cref="look"/>) and a low drone rises from the mug. Look away and it closes.
    ///  - ParticleRing (the default): a ring of glowing motes gathers on the rim and swirls slowly
    ///    upward. The real inside of the mug stays visible.
    ///  - Tunnel: the inside opens into a bottomless tunnel fading to black (on the headset this can
    ///    read as a flat black disc), with the client's rune optionally glowing far below.
    ///  - None: only the drone.
    ///
    /// Built at runtime as children of the proxy mug (so it follows the tracked mug and hides when
    /// tracking is lost):
    ///  - Occluder: the proxy mesh, depth only (Mug/MugOccluder). Virtual things behind the
    ///    real mug's walls are hidden; passthrough shows through.
    ///  - Portal: a disc across the opening at the rim that marks the stencil (Mug/MugPortalMask).
    ///  - Well: a tunnel from just wider than the opening to <see cref="wellBottomRadius"/>, drawn only
    ///    through the portal and ignoring depth, so it goes "through" the real floor (Mug/MugWell).
    ///    It turns to run away from the viewer, so you always look straight down it at the rune.
    ///  - Rune: the client's rune as a glowing disc at the bottom of the well (Mug/MugRuneGlow).
    ///  - Ring: the particle ring at the rim (Mug/DreamEmber), when <see cref="look"/> is ParticleRing.
    ///    Portal, Well and Rune are only built for the Tunnel look; the occluder always is.
    ///
    /// Looking in: the head is within <see cref="maxDistance"/>, the gaze points at the opening
    /// (within <see cref="gazeAngle"/>), and the head is above the opening, i.e. the line from the
    /// head to the rim is within <see cref="intoAngle"/> of the mug's down axis. Held for
    /// <see cref="dwell"/>, the well fades in; otherwise it fades out. Works for a held, tilted mug
    /// too, since it uses the mug's own axis. Logs are prefixed [MugInterior].
    /// </summary>
    public class MugInterior : MonoBehaviour
    {
        public enum InteriorLook { ParticleRing, Tunnel, None }

        [Tooltip("What appears at the opening when you look in. Tunnel is the original dark well; ParticleRing replaces it with glowing motes on the rim; None leaves only the drone.")]
        [SerializeField] InteriorLook look = InteriorLook.ParticleRing;
        [SerializeField] CupDetector detector;
        [Tooltip("The proxy mug: everything is built under it.")]
        [SerializeField] Transform proxy;
        [Tooltip("The proxy's mesh (MugMesh), copied for the occluder.")]
        [SerializeField] MeshFilter proxyMesh;
        [SerializeField] Material occluderMaterial;
        [SerializeField] Material portalMaterial;
        [SerializeField] Material wellMaterial;
        [SerializeField] Material runeMaterial;
        [Tooltip("Particle material for the ring (Materials/DreamEmber.mat). Empty: borrows the dream's ember material.")]
        [SerializeField] Material ringMaterial;
        [Tooltip("Draw the glowing rune at the bottom of the tunnel. Off: the tunnel fades into darkness with nothing at the bottom.")]
        [SerializeField] bool showRune = false;
        [Tooltip("Low drone from inside the mug while it's open. Its file, level and pitch live in the Sound Slot (Memories > Sound Design).")]
        [SerializeField] SoundSlot droneSlot;

        [Header("Shape (m, mug space: base centre, +Y up)")]
        [Tooltip("Inner radius of the real mug's opening (measured from the model: 37.3 mm).")]
        [SerializeField] float openingRadius = 0.0365f;
        [Tooltip("A tunnel: looking down it, the rings on its walls converge on the rune, which reads as depth.")]
        [SerializeField] float wellDepth = 0.6f;
        [SerializeField] float wellBottomRadius = 0.09f;
        [Tooltip("The shaft's throat is this much wider than the opening, so it fills the opening seen at a slant.")]
        [SerializeField] float throatScale = 1.3f;
        [Tooltip("How quickly the shaft turns to follow the viewer's line of sight (1/s).")]
        [SerializeField] float shaftFollow = 8f;

        [Header("Particle ring")]
        [Tooltip("Motes per second with the ring fully open.")]
        [SerializeField] float ringRate = 180f;
        [Tooltip("How fast the motes rise off the rim (m/s).")]
        [SerializeField] float ringRise = 0.015f;
        [Tooltip("How fast the ring swirls around the mug's axis (rad/s).")]
        [SerializeField] float ringSwirl = 0.8f;
        [SerializeField] Color ringColour = new Color(1f, 0.82f, 0.45f, 0.9f);
        [SerializeField] Color ringColour2 = new Color(1f, 0.55f, 0.25f, 0.7f);

        [Header("Looking in")]
        [SerializeField] float maxDistance = 0.9f;
        [SerializeField] float gazeAngle = 25f;
        [SerializeField] float intoAngle = 50f;
        [SerializeField] float dwell = 0.4f;
        [SerializeField] float openTime = 1.5f;
        [SerializeField] float closeTime = 0.6f;
        [Tooltip("Debug: keep the well open regardless of gaze.")]
        [SerializeField] bool forceOpen;

        public bool LookingIn { get; private set; }
        public float Reveal { get; private set; }
        public bool ForceOpen { get => forceOpen; set => forceOpen = value; }
        /// <summary>Keep it open regardless of gaze (set by the dream while it runs).</summary>
        public bool HoldOpen { get; set; }
        /// <summary>Angle between the gaze and the opening, and distance to it (degrees, m).</summary>
        public float GazeAngle => _gaze;
        public float Distance => _dist;
        public Vector3 RimCentre => proxy != null ? proxy.position + proxy.up * RimHeight : Vector3.zero;
        public string StatusLine =>
            $"inside: {(LookingIn ? "LOOKING IN" : "-")}  open {Reveal:0.00}  gaze {_gaze:0}°  above {_above:0}°  dist {_dist:0.00} m{(forceOpen ? "  FORCED" : "")}";

        Material _well, _rune;
        ParticleSystem _ring;
        Transform _shaft;
        SlotVoice _drone;
        float _lookSince = -1f;
        float _gaze, _above, _dist;
        bool _wasOpen;

        float RimHeight => detector != null ? detector.MugHeight : 0.095f;

        void Start()
        {
            if (proxy == null) { Debug.LogWarning("[MugInterior] No proxy assigned"); enabled = false; return; }

            bool tunnel = look == InteriorLook.Tunnel;
            if (proxyMesh != null && occluderMaterial != null)
            {
                var occ = new GameObject("Occluder");
                occ.transform.SetParent(proxyMesh.transform.parent, false);
                occ.transform.localPosition = proxyMesh.transform.localPosition;
                occ.transform.localRotation = proxyMesh.transform.localRotation;
                occ.transform.localScale = proxyMesh.transform.localScale;
                occ.AddComponent<MeshFilter>().sharedMesh = proxyMesh.sharedMesh;
                occ.AddComponent<MeshRenderer>().sharedMaterial = occluderMaterial;
            }

            var interior = new GameObject("Interior").transform;
            interior.SetParent(proxy, false);
            interior.localPosition = new Vector3(0f, RimHeight, 0f);   // origin at the rim centre

            if (tunnel) BuildTunnel(interior);
            else if (look == InteriorLook.ParticleRing) BuildRing(interior);

            _drone = SlotVoice.Create(droneSlot, interior, "Drone", true);
            SetReveal(0f);
            Debug.Log(tunnel
                ? $"[MugInterior] Ready (tunnel): opening r {openingRadius * 1000f:0} mm, well {wellDepth:0.0} m deep flaring to r {wellBottomRadius:0.00} m"
                : $"[MugInterior] Ready ({look}): opening r {openingRadius * 1000f:0} mm");
        }

        void BuildTunnel(Transform interior)
        {
            Part("Portal", interior, Disc(openingRadius, 48), portalMaterial);

            // The shaft behind the opening turns to face away from the viewer, so you always look
            // straight down it at the rune (a straight well only shows its bottom from directly above).
            _shaft = new GameObject("Shaft").transform;
            _shaft.SetParent(interior, false);
            _well = new Material(wellMaterial);
            Part("Well", _shaft, Cone(openingRadius * throatScale, wellBottomRadius, wellDepth, 48, 24), _well);
            if (showRune && runeMaterial != null)
            {
                _rune = new Material(runeMaterial);
                var rune = Part("Rune", _shaft, Disc(wellBottomRadius * 0.95f, 48), _rune);
                rune.localPosition = new Vector3(0f, -wellDepth, 0f);
            }
        }

        /// <summary>Glowing motes born on the rim, swirling around the mug's axis as they rise and fade.
        /// Simulated in the mug's space so the ring stays on the rim of a held, moving mug.</summary>
        void BuildRing(Transform interior)
        {
            if (ringMaterial == null)
            {
                var dream = FindAnyObjectByType<DreamState>();
                if (dream != null) ringMaterial = dream.EmberMaterial;
            }
            if (ringMaterial == null) { Debug.LogWarning("[MugInterior] No ring material assigned: no particle ring"); return; }
            var go = new GameObject("Ring");
            go.transform.SetParent(interior, false);
            go.transform.localPosition = new Vector3(0f, 0.002f, 0f);   // just above the lip
            _ring = go.AddComponent<ParticleSystem>();
            _ring.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ring.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.007f);
            main.startColor = new ParticleSystem.MinMaxGradient(ringColour, ringColour2);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 600;

            var em = _ring.emission;
            em.rateOverTime = 0f;

            // A circle's edge, laid flat in the rim plane (the circle shape is in XY; turn it into XZ).
            var shape = _ring.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = openingRadius;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var vel = _ring.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = 0f; vel.z = 0f;
            vel.y = new ParticleSystem.MinMaxCurve(ringRise * 0.6f, ringRise * 1.4f);
            vel.orbitalX = 0f; vel.orbitalZ = 0f;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(ringSwirl * 0.7f, ringSwirl * 1.3f);
            vel.radial = new ParticleSystem.MinMaxCurve(0f, 0.004f);   // the ring slowly widens as it rises

            var noise = _ring.noise;
            noise.enabled = true;
            noise.strength = 0.004f;
            noise.frequency = 1.5f;

            var col = _ring.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var size = _ring.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = ringMaterial;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }

        Transform Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void Update()
        {
            LookingIn = proxy.gameObject.activeInHierarchy && IsLookingIn();
            if (LookingIn) { if (_lookSince < 0f) _lookSince = Time.time; }
            else _lookSince = -1f;

            bool open = forceOpen || HoldOpen || (_lookSince >= 0f && Time.time - _lookSince > dwell);
            if (open != _wasOpen) Debug.Log(open ? $"[MugInterior] Opening (gaze {_gaze:0}°, above {_above:0}°, {_dist:0.00} m)" : "[MugInterior] Closing");
            _wasOpen = open;
            float speed = open ? 1f / openTime : 1f / closeTime;
            SetReveal(Mathf.MoveTowards(Reveal, open ? 1f : 0f, Time.deltaTime * speed));
            if (_shaft != null) AimShaft(Reveal <= 0f);
        }

        /// <summary>Turn the shaft so it runs away from the viewer through the opening.</summary>
        void AimShaft(bool snap)
        {
            var cam = Camera.main;
            if (_shaft == null || cam == null) return;
            var dir = _shaft.position - cam.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            var target = Quaternion.FromToRotation(Vector3.down, dir.normalized);
            _shaft.rotation = snap ? target : Quaternion.Slerp(_shaft.rotation, target, 1f - Mathf.Exp(-shaftFollow * Time.deltaTime));
        }

        bool IsLookingIn()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var head = cam.transform;
            var up = proxy.up;
            var rim = proxy.position + up * RimHeight;
            var toRim = rim - head.position;
            _dist = toRim.magnitude;
            _gaze = Vector3.Angle(head.forward, toRim);
            _above = Vector3.Angle(toRim, -up);
            return _dist < maxDistance && _gaze < gazeAngle && _above < intoAngle;
        }

        void SetReveal(float r)
        {
            Reveal = r;
            float eased = r * r * (3f - 2f * r);
            if (_well != null) _well.SetFloat("_Reveal", eased);
            if (_rune != null) _rune.SetFloat("_Reveal", Mathf.Clamp01(eased * 1.6f - 0.6f));   // the rune lights up last
            if (_ring != null)
            {
                var em = _ring.emission;
                em.rateOverTime = ringRate * eased;
                // Keep it playing while motes are alive, so the last ones fade out rather than vanish.
                if (eased > 0f && !_ring.isPlaying) _ring.Play();
                else if (eased <= 0f && _ring.isPlaying && _ring.particleCount == 0) _ring.Stop();
            }
            if (_drone != null)
            {
                _drone.Level = eased;
                _drone.Tick();
                if (eased > 0f && !_drone.Source.isPlaying) _drone.Play();
                else if (eased <= 0f && _drone.Source.isPlaying) _drone.Stop();
            }
        }

        // ------------------------------------------------------------ meshes (origin at the rim, +Y up)

        static Mesh Disc(float radius, int segments)
        {
            var v = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var t = new int[segments * 3];
            v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % segments; t[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = "Disc", vertices = v, uv = uv, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Tunnel from the rim (radius r0) down to depth (radius r1), with a closed floor so
        /// the rune glows against black rather than the real mug's floor.</summary>
        static Mesh Cone(float r0, float r1, float depth, int segments, int rings)
        {
            int wallVerts = (segments + 1) * (rings + 1);
            var v = new Vector3[wallVerts + 1];
            var t = new int[segments * rings * 6 + segments * 3];
            for (int j = 0; j <= rings; j++)
            {
                float k = j / (float)rings;
                float r = Mathf.Lerp(r0, r1, k);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    v[j * (segments + 1) + i] = new Vector3(Mathf.Cos(a) * r, -depth * k, Mathf.Sin(a) * r);
                }
            }
            int n = 0;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < segments; i++)
                {
                    int a = j * (segments + 1) + i, b = a + segments + 1;
                    t[n++] = a; t[n++] = b; t[n++] = a + 1;
                    t[n++] = a + 1; t[n++] = b; t[n++] = b + 1;
                }
            // Floor cap: a fan from the bottom centre over the last ring.
            v[wallVerts] = new Vector3(0f, -depth, 0f);
            int last = rings * (segments + 1);
            for (int i = 0; i < segments; i++) { t[n++] = wallVerts; t[n++] = last + i; t[n++] = last + i + 1; }
            var m = new Mesh { name = "Well", vertices = v, triangles = t };
            m.RecalculateBounds();
            return m;
        }
    }
}
