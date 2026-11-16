using System;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;

namespace CupSpike
{
    /// <summary>
    /// The mug that screams (spike plan, Block 1): reach for the mug and it screams from exactly
    /// where it sits.
    ///
    ///  - Trigger zone: a shell around the mug's body. It fires when either index fingertip comes
    ///    within <see cref="ZoneSize"/> of the mug's surface. Zone presets: 10 cm ("almost
    ///    touching") and 3 cm ("contact"), switched with the Zone button.
    ///  - The zone follows the tracked mug. When tracking is lost it stays at the last known
    ///    pose (a mug on a table doesn't move by itself), and after a put-down it stays where the
    ///    hand let go.
    ///  - Fallback (Pin): pinch with either hand to pin the zone at the pinch point, for a demo that
    ///    works without detection. Pin again to go back to tracking.
    ///  - No scream while the mug is held (that's Block 4). Re-arms once the scream has finished,
    ///    the cooldown has passed and every fingertip is back outside the zone (plus a margin).
    ///  - The scream is spatialised (Meta XR Audio) and placed at the mug. A looping background bed
    ///    (not spatialised) fades in at start and ducks while the scream plays.
    ///  - Test counts for the "almost touching or contact?" question: each scream is logged; Miss
    ///    and False buttons log reaches that didn't fire or fires that weren't reaches.
    ///    All logs are prefixed [MugScream].
    /// </summary>
    public class MugScream : MonoBehaviour
    {
        public enum Mode { Tracked, Placing, Pinned }

        [SerializeField] CupDetector detector;
        [SerializeField] Hand leftHand;
        [SerializeField] Hand rightHand;
        [Header("Sound")]
        [Tooltip("Played from inside the mug (spatialised, mono). Restarts rather than overlapping.")]
        [SerializeField] AudioClip scream;
        [SerializeField, Range(0f, 1f)] float volume = 1f;
        [Tooltip("Looping background bed (not spatialised). Fades in when the scene starts.")]
        [SerializeField] AudioClip background;
        [Tooltip("The bed is louder than the scream (mean -13.7 dB vs -18.9 dB), so it sits low.")]
        [SerializeField, Range(0f, 1f)] float backgroundVolume = 0.35f;
        [SerializeField] float backgroundFadeIn = 3f;
        [Tooltip("Background level while the scream plays, as a fraction of its normal level.")]
        [SerializeField, Range(0f, 1f)] float duckTo = 0.25f;
        [SerializeField] float duckSpeed = 4f;

        [Header("Zone")]
        [Tooltip("Zone sizes the Zone button cycles through: distance from the mug's surface (m).")]
        [SerializeField] float[] zonePresets = { 0.10f, 0.03f };
        [Tooltip("Every fingertip must be this much further out than the zone to re-arm (m).")]
        [SerializeField] float rearmMargin = 0.05f;
        [SerializeField] float cooldown = 2f;

        [Header("Debug")]
        [SerializeField] Material unlitMaterial;
        [SerializeField] bool showZone = true;

        public UnityEvent onScream = new UnityEvent();

        // ------------------------------------------------------------ state, read by the debug view and menu

        public Mode ZoneMode { get; private set; } = Mode.Tracked;
        public float ZoneSize => zonePresets[_preset];
        public bool HasZone { get; private set; }
        public bool Armed { get; private set; } = true;
        /// <summary>Closest index fingertip to the mug surface (m), or NaN.</summary>
        public float NearestTip { get; private set; } = float.NaN;
        public int Screams { get; private set; }
        public int Misses { get; private set; }
        public int FalseFires { get; private set; }
        public bool ShowZone { get => showZone; set => showZone = value; }

        public string StatusLine =>
            $"scream: {(HasZone ? (Armed ? "armed" : "re-arming") : "no mug yet")}  zone {ZoneSize * 100f:0} cm ({ZoneMode})  " +
            $"tip {(float.IsNaN(NearestTip) ? "-" : $"{NearestTip * 100f:0} cm")}  " +
            $"screams {Screams}  miss {Misses}  false {FalseFires}";

        int _preset;
        Pose _zone;              // mug base pose the zone is around
        float _lastScream = -99f;
        AudioSource _audio, _bed;
        float _bedLevel;          // 0..1 fade-in progress
        float _duck = 1f;
        LineRenderer _ringTop, _ringBottom;
        bool _wasPinching;

        void Start()
        {
            var go = new GameObject("ScreamSource");
            go.transform.SetParent(transform, false);
            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialize = true;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Logarithmic;
            _audio.minDistance = 0.3f;
            _audio.maxDistance = 15f;
            go.AddComponent<MetaXRAudioSource>();
            _audio.clip = scream;
            _audio.volume = volume;

            if (background != null)
            {
                var bed = new GameObject("BackgroundBed");
                bed.transform.SetParent(transform, false);
                _bed = bed.AddComponent<AudioSource>();
                _bed.clip = background;
                _bed.loop = true;
                _bed.spatialBlend = 0f;
                _bed.volume = 0f;
                _bed.playOnAwake = false;
                _bed.Play();
            }

            _ringTop = Ring("ZoneTop");
            _ringBottom = Ring("ZoneBottom");
            Debug.Log($"[MugScream] Ready: zone {ZoneSize * 100f:0} cm, clip '{(scream ? scream.name : "NONE")}'");
        }

        // ------------------------------------------------------------ menu actions

        public void CycleZone()
        {
            _preset = (_preset + 1) % zonePresets.Length;
            Debug.Log($"[MugScream] Zone now {ZoneSize * 100f:0} cm  (so far: screams {Screams}, miss {Misses}, false {FalseFires})");
        }

        /// <summary>Tracked -> Placing (pinch to pin) -> back to Tracked.</summary>
        public void TogglePin()
        {
            ZoneMode = ZoneMode == Mode.Tracked ? Mode.Placing : Mode.Tracked;
            _wasPinching = true;   // ignore a pinch already in progress
            Debug.Log(ZoneMode == Mode.Placing ? "[MugScream] Pin: pinch at the mug to place the zone" : "[MugScream] Zone follows the tracked mug again");
        }

        public void LogMiss() { Misses++; Debug.Log($"[MugScream] MISS logged (zone {ZoneSize * 100f:0} cm) total {Misses}"); }
        public void LogFalse() { FalseFires++; Debug.Log($"[MugScream] FALSE fire logged (zone {ZoneSize * 100f:0} cm) total {FalseFires}"); }

        /// <summary>Plays the scream now (also used by Replay / testing).</summary>
        public void Scream(string why)
        {
            _lastScream = Time.time;
            Armed = false;
            Screams++;
            _audio.transform.position = MugCentre(_zone);
            if (scream != null) { _audio.Stop(); _audio.Play(); }   // restart, never stack a 12 s scream
            Debug.Log($"[MugScream] SCREAM #{Screams}: {why}, zone {ZoneSize * 100f:0} cm, {ZoneMode}, source {detector?.TrackingSource}");
            onScream.Invoke();
        }

        // ------------------------------------------------------------ per frame

        void Update()
        {
            UpdateZonePose();
            bool held = detector != null && detector.TrackingSource == CupDetector.Source.Hand;

            NearestTip = float.NaN;
            string nearestHand = "";
            if (HasZone)
            {
                foreach (var (hand, name) in new[] { (leftHand, "L"), (rightHand, "R") })
                {
                    if (hand == null || !hand.IsTrackedDataValid || !hand.GetJointPose(HandJointId.HandIndexTip, out var tip)) continue;
                    float d = SurfaceDistance(tip.position);
                    if (float.IsNaN(NearestTip) || d < NearestTip) { NearestTip = d; nearestHand = name; }
                }
            }

            // Re-arm once the scream has finished, the cooldown has passed and every fingertip is clear.
            if (!Armed && !_audio.isPlaying && Time.time - _lastScream > cooldown &&
                (float.IsNaN(NearestTip) || NearestTip > ZoneSize + rearmMargin))
                Armed = true;

            if (_audio.isPlaying) _audio.transform.position = MugCentre(_zone);   // a scream in progress follows the mug
            UpdateBed();

            if (Armed && HasZone && !held && !float.IsNaN(NearestTip) && NearestTip < ZoneSize)
                Scream($"{nearestHand} index {NearestTip * 100f:0.0} cm from the mug");

            DrawZone(held);
        }

        /// <summary>Fade the background in, and duck it while the scream plays.</summary>
        void UpdateBed()
        {
            if (_bed == null) return;
            _bedLevel = backgroundFadeIn > 0f ? Mathf.MoveTowards(_bedLevel, 1f, Time.deltaTime / backgroundFadeIn) : 1f;
            _duck = Mathf.MoveTowards(_duck, _audio.isPlaying ? duckTo : 1f, Time.deltaTime * duckSpeed);
            _bed.volume = backgroundVolume * _bedLevel * _duck;
        }

        void UpdateZonePose()
        {
            if (ZoneMode == Mode.Placing)
            {
                foreach (var hand in new[] { leftHand, rightHand })
                {
                    if (hand == null || !hand.IsTrackedDataValid) continue;
                    bool pinching = hand.GetIndexFingerIsPinching();
                    if (pinching && !_wasPinching && hand.GetJointPose(HandJointId.HandIndexTip, out var tip) &&
                        hand.GetJointPose(HandJointId.HandThumbTip, out var thumb))
                    {
                        // Pinch at the side of the mug, roughly half-way up.
                        var p = (tip.position + thumb.position) * 0.5f;
                        float h = detector != null ? detector.MugHeight : 0.095f;
                        _zone = new Pose(p - Vector3.up * (h * 0.5f), Quaternion.identity);
                        HasZone = true;
                        ZoneMode = Mode.Pinned;
                        Armed = false;   // the pinching hand is inside the zone
                        _lastScream = Time.time;
                        Debug.Log($"[MugScream] Zone pinned at {p:F2}");
                    }
                    _wasPinching = pinching;
                    if (ZoneMode == Mode.Pinned) break;
                }
                return;
            }
            if (ZoneMode == Mode.Pinned || detector == null || !detector.Tracking) return;   // keep the last pose
            _zone = detector.CupPose;
            if (!HasZone) Debug.Log("[MugScream] First mug position: zone live");
            HasZone = true;
        }

        /// <summary>Distance from a point to the mug's body (a cylinder), 0 inside it.</summary>
        float SurfaceDistance(Vector3 world)
        {
            float r = detector != null ? detector.MugRadius : 0.04f, h = detector != null ? detector.MugHeight : 0.095f;
            var local = Quaternion.Inverse(_zone.rotation) * (world - _zone.position);
            float radial = Mathf.Max(0f, new Vector2(local.x, local.z).magnitude - r);
            float vertical = Mathf.Max(0f, Mathf.Abs(local.y - h * 0.5f) - h * 0.5f);
            return Mathf.Sqrt(radial * radial + vertical * vertical);
        }

        Vector3 MugCentre(Pose p)
        {
            float h = detector != null ? detector.MugHeight : 0.095f;
            return p.position + p.rotation * (Vector3.up * (h * 0.5f));
        }

        // ------------------------------------------------------------ zone drawing

        LineRenderer Ring(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.positionCount = 48;
            lr.widthMultiplier = 0.002f;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (unlitMaterial != null) lr.material = new Material(unlitMaterial);
            return lr;
        }

        void DrawZone(bool held)
        {
            bool show = showZone && HasZone;
            _ringTop.enabled = _ringBottom.enabled = show;
            if (!show) return;
            var colour = held ? new Color(0.5f, 0.5f, 0.5f) : Armed ? new Color(0.95f, 0.25f, 0.2f) :
                         Time.time - _lastScream < 0.5f ? Color.white : new Color(1f, 0.6f, 0.2f);
            float r = (detector != null ? detector.MugRadius : 0.04f) + ZoneSize;
            float h = detector != null ? detector.MugHeight : 0.095f;
            SetRing(_ringBottom, r, 0f, colour);
            SetRing(_ringTop, r, h, colour);
        }

        void SetRing(LineRenderer lr, float radius, float height, Color c)
        {
            if (lr.material != null) lr.material.SetColor("_BaseColor", c);
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / lr.positionCount;
                lr.SetPosition(i, _zone.position + _zone.rotation * new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            }
        }
    }
}
