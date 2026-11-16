using System.Collections;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;

namespace CupSpike
{
    /// <summary>
    /// The mug that screams when touched, built as an escalation rather than a single trigger:
    ///
    ///  1. Approach (fingertip from <see cref="approachStart"/> down to the touch zone): a breath
    ///     from inside the mug, louder and slightly higher as the hand nears, while the background
    ///     bed drains away.
    ///  2. Pull away before touching: the breath cuts out dead.
    ///  3. Touch (fingertip within <see cref="TouchZone"/> of the mug's surface, 3 cm by default):
    ///     a beat of total silence, then the scream (and <see cref="onScream"/>, e.g. RoomDrain).
    ///
    /// Distance is from either index fingertip to the mug's body (a cylinder). The zone follows the
    /// tracked mug, stays at the last known pose when tracking drops, and stays where the hand let
    /// go after a put-down. Pin (fallback): pinch at the mug to fix the zone with no detection.
    /// Nothing happens while the mug is held. Re-arms once the scream has finished, the cooldown
    /// has passed and every fingertip is back outside the touch zone plus a margin.
    ///
    /// Test counts for "almost touching or contact?": the Zone button switches the touch distance
    /// (3 cm / 10 cm); Miss and False log reaches that didn't fire and fires without a reach.
    /// All logs are prefixed [MugScream].
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
        [Tooltip("Looping breath from inside the mug while a hand approaches (spatialised, mono).")]
        [SerializeField] AudioClip breath;
        [SerializeField, Range(0f, 1f)] float breathVolume = 0.9f;
        [SerializeField] Vector2 breathPitch = new Vector2(0.9f, 1.15f);
        [Tooltip("Looping background bed, stereo, head-locked. Used only if the left/right pair below isn't set.")]
        [SerializeField] AudioClip background;
        [Tooltip("The bed's left and right channels as mono clips. Played from two fixed points in the room " +
                 "(world-locked, spatialised), so the bed stays put when you turn your head.")]
        [SerializeField] AudioClip backgroundLeft;
        [SerializeField] AudioClip backgroundRight;
        [Tooltip("The two bed emitters sit this far left and right of where you start, at head height (m).")]
        [SerializeField] float bedSpread = 3f;
        [Tooltip("...and this far in front (m).")]
        [SerializeField] float bedForward = 1f;
        [Tooltip("The bed is louder than the scream (mean -13.7 dB vs -18.9 dB), so it sits low.")]
        [SerializeField, Range(0f, 1f)] float backgroundVolume = 0.35f;
        [SerializeField] float backgroundFadeIn = 3f;
        [Tooltip("Background level while the scream plays, as a fraction of its normal level.")]
        [SerializeField, Range(0f, 1f)] float duckTo = 0.25f;
        [Tooltip("How much of the background the approach drains away at its closest (0..1).")]
        [SerializeField, Range(0f, 1f)] float approachDrain = 0.85f;
        [SerializeField] float duckSpeed = 4f;

        [Header("Zones (distance from the mug's surface, m)")]
        [Tooltip("The breath starts when a fingertip comes this close.")]
        [SerializeField] float approachStart = 0.30f;
        [Tooltip("Touch distances the Zone button cycles through. First = default (contact).")]
        [SerializeField] float[] zonePresets = { 0.03f, 0.10f };
        [Tooltip("Total silence between touch and scream (s).")]
        [SerializeField] float silenceBeat = 0.35f;
        [Tooltip("Every fingertip must be this much further out than the touch zone to re-arm (m).")]
        [SerializeField] float rearmMargin = 0.05f;
        [SerializeField] float cooldown = 2f;

        [Header("Debug")]
        [SerializeField] Material unlitMaterial;
        [SerializeField] bool showZone = true;

        public UnityEvent onScream = new UnityEvent();

        // ------------------------------------------------------------ state, read by the debug view and menu

        public Mode ZoneMode { get; private set; } = Mode.Tracked;
        public float TouchZone => zonePresets[_preset];
        public bool HasZone { get; private set; }
        public bool Armed { get; private set; } = true;
        /// <summary>Closest index fingertip to the mug surface (m), or NaN.</summary>
        public float NearestTip { get; private set; } = float.NaN;
        /// <summary>0 = hand at approachStart or further, 1 = at the touch zone.</summary>
        public float Approach { get; private set; }
        public int Screams { get; private set; }
        public int Misses { get; private set; }
        public int FalseFires { get; private set; }
        public bool ShowZone { get => showZone; set => showZone = value; }

        public string StatusLine =>
            $"scream: {(HasZone ? (_inBeat ? "SILENCE" : Armed ? (Approach > 0f ? $"breathing {Approach:0.00}" : "armed") : "re-arming") : "no mug yet")}" +
            $"  touch {TouchZone * 100f:0} cm ({ZoneMode})  tip {(float.IsNaN(NearestTip) ? "-" : $"{NearestTip * 100f:0} cm")}  " +
            $"screams {Screams}  miss {Misses}  false {FalseFires}";

        int _preset;
        Pose _zone;              // mug base pose the zones are around
        float _lastScream = -99f;
        AudioSource _audio, _breath;
        readonly System.Collections.Generic.List<AudioSource> _beds = new System.Collections.Generic.List<AudioSource>();
        float _bedLevel;          // 0..1 fade-in progress
        float _duck = 1f;
        bool _inBeat;
        bool _breathing;
        float _breathPeak;
        LineRenderer _ringTop, _ringBottom, _approachRing;
        bool _wasPinching;

        void Start()
        {
            _audio = MugSource("ScreamSource", scream, false);
            _audio.volume = volume;
            _breath = MugSource("BreathSource", breath, true);
            _breath.volume = 0f;

            if (backgroundLeft != null && backgroundRight != null)
            {
                // World-locked pair, started together so the channels stay in sync.
                var left = BedSource("BackgroundBedLeft", backgroundLeft, true);
                var right = BedSource("BackgroundBedRight", backgroundRight, true);
                double start = AudioSettings.dspTime + 0.1;
                left.PlayScheduled(start);
                right.PlayScheduled(start);
                StartCoroutine(PlaceBedOnceTracked(left.transform, right.transform));
            }
            else if (background != null)
                BedSource("BackgroundBed", background, false).Play();

            _ringTop = Ring("TouchTop");
            _ringBottom = Ring("TouchBottom");
            _approachRing = Ring("ApproachRing");
            Debug.Log($"[MugScream] Ready: approach {approachStart * 100f:0} cm, touch {TouchZone * 100f:0} cm, " +
                      $"scream '{(scream ? scream.name : "NONE")}', breath '{(breath ? breath.name : "NONE")}'");
        }

        AudioSource BedSource(string name, AudioClip clip, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = true;
            a.playOnAwake = false;
            a.volume = 0f;
            if (spatial)
            {
                a.spatialize = true;
                a.spatialBlend = 1f;
                a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = 2f;    // fairly even level around the room
                a.maxDistance = 30f;
                go.AddComponent<MetaXRAudioSource>();
            }
            else a.spatialBlend = 0f;
            _beds.Add(a);
            return a;
        }

        /// <summary>Place the bed pair left and right of the viewer, once head tracking has a pose.</summary>
        IEnumerator PlaceBedOnceTracked(Transform left, Transform right)
        {
            var cam = Camera.main;
            for (float t = 0f; t < 2f && cam != null && cam.transform.localPosition == Vector3.zero; t += Time.deltaTime) yield return null;
            var head = cam != null ? cam.transform : transform;
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            left.position = head.position + yaw * new Vector3(-bedSpread, 0f, bedForward);
            right.position = head.position + yaw * new Vector3(bedSpread, 0f, bedForward);
            Debug.Log($"[MugScream] Background bed placed in the room: L {left.position:F1}  R {right.position:F1}");
        }

        AudioSource MugSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.clip = clip;
            a.loop = loop;
            a.spatialize = true;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = 0.3f;
            a.maxDistance = 15f;
            go.AddComponent<MetaXRAudioSource>();
            return a;
        }

        // ------------------------------------------------------------ menu actions

        public void CycleZone()
        {
            _preset = (_preset + 1) % zonePresets.Length;
            Debug.Log($"[MugScream] Touch zone now {TouchZone * 100f:0} cm  (so far: screams {Screams}, miss {Misses}, false {FalseFires})");
        }

        /// <summary>Tracked -> Placing (pinch to pin) -> back to Tracked.</summary>
        public void TogglePin()
        {
            ZoneMode = ZoneMode == Mode.Tracked ? Mode.Placing : Mode.Tracked;
            _wasPinching = true;   // ignore a pinch already in progress
            Debug.Log(ZoneMode == Mode.Placing ? "[MugScream] Pin: pinch at the mug to place the zone" : "[MugScream] Zone follows the tracked mug again");
        }

        public void LogMiss() { Misses++; Debug.Log($"[MugScream] MISS logged (touch {TouchZone * 100f:0} cm) total {Misses}"); }
        public void LogFalse() { FalseFires++; Debug.Log($"[MugScream] FALSE fire logged (touch {TouchZone * 100f:0} cm) total {FalseFires}"); }

        /// <summary>Plays the scream now, skipping the approach and silence (testing, Replay).</summary>
        public void Scream(string why)
        {
            _lastScream = Time.time;
            Armed = false;
            Screams++;
            StopBreath();
            _audio.transform.position = MugCentre(_zone);
            if (scream != null) { _audio.Stop(); _audio.Play(); }   // restart, never stack a 12 s scream
            Debug.Log($"[MugScream] SCREAM #{Screams}: {why}, touch {TouchZone * 100f:0} cm, {ZoneMode}, source {detector?.TrackingSource}");
            onScream.Invoke();
        }

        IEnumerator TouchSequence(string why)
        {
            Armed = false;
            _inBeat = true;
            StopBreath();
            Debug.Log($"[MugScream] Touched: {why}. Silence for {silenceBeat:0.00} s");
            yield return new WaitForSeconds(silenceBeat);
            _inBeat = false;
            Scream(why);
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
            if (!Armed && !_inBeat && !_audio.isPlaying && Time.time - _lastScream > cooldown &&
                (float.IsNaN(NearestTip) || NearestTip > TouchZone + rearmMargin))
            {
                Armed = true;
                Debug.Log("[MugScream] Re-armed");
            }

            bool live = Armed && HasZone && !held && !float.IsNaN(NearestTip);
            if (live && NearestTip < TouchZone)
                StartCoroutine(TouchSequence($"{nearestHand} index {NearestTip * 100f:0.0} cm from the mug"));
            else
                UpdateBreath(live);

            if (_audio.isPlaying) _audio.transform.position = MugCentre(_zone);   // a scream in progress follows the mug
            _breath.transform.position = MugCentre(_zone);
            UpdateBed();
            DrawZone(held);
        }

        /// <summary>The breath swells as the fingertip nears; leaving the approach zone cuts it dead.</summary>
        void UpdateBreath(bool live)
        {
            float a = live ? Mathf.InverseLerp(approachStart, TouchZone, NearestTip) : 0f;
            a *= a;   // quiet for most of the approach, then it rushes in
            Approach = a;
            if (a <= 0f)
            {
                if (_breathing)
                {
                    Debug.Log($"[MugScream] Pulled away: breath cut dead (it had reached {_breathPeak:0.00})");
                    StopBreath();
                }
                return;
            }
            if (!_breathing && breath != null)
            {
                _breathing = true;
                _breathPeak = 0f;
                _breath.volume = 0f;
                _breath.Play();
                Debug.Log($"[MugScream] Approach: breath starts ({NearestTip * 100f:0} cm)");
            }
            _breathPeak = Mathf.Max(_breathPeak, a);
            _breath.volume = Mathf.MoveTowards(_breath.volume, a * breathVolume, Time.deltaTime * 3f);
            _breath.pitch = Mathf.Lerp(breathPitch.x, breathPitch.y, a);
        }

        void StopBreath()
        {
            _breathing = false;
            Approach = 0f;
            if (_breath != null) { _breath.Stop(); _breath.volume = 0f; }
        }

        /// <summary>Fade the background in; drain it as a hand approaches, cut it in the silence, duck it under the scream.</summary>
        void UpdateBed()
        {
            if (_beds.Count == 0) return;
            _bedLevel = backgroundFadeIn > 0f ? Mathf.MoveTowards(_bedLevel, 1f, Time.deltaTime / backgroundFadeIn) : 1f;
            float target = _inBeat ? 0f : _audio.isPlaying ? duckTo : 1f - approachDrain * Approach;
            _duck = _inBeat ? 0f : Mathf.MoveTowards(_duck, target, Time.deltaTime * duckSpeed);
            foreach (var bed in _beds) bed.volume = backgroundVolume * _bedLevel * _duck;
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

        /// <summary>Touch zone: rings at base and rim (red armed, white firing, orange re-arming, grey held).
        /// Approach zone: one faint ring at mid-height, brightening as the breath builds.</summary>
        void DrawZone(bool held)
        {
            bool show = showZone && HasZone;
            _ringTop.enabled = _ringBottom.enabled = _approachRing.enabled = show;
            if (!show) return;
            var colour = held ? new Color(0.5f, 0.5f, 0.5f) : _inBeat || Time.time - _lastScream < 0.5f ? Color.white :
                         Armed ? new Color(0.95f, 0.25f, 0.2f) : new Color(1f, 0.6f, 0.2f);
            float r = detector != null ? detector.MugRadius : 0.04f;
            float h = detector != null ? detector.MugHeight : 0.095f;
            SetRing(_ringBottom, r + TouchZone, 0f, colour);
            SetRing(_ringTop, r + TouchZone, h, colour);
            SetRing(_approachRing, r + approachStart, h * 0.5f, Color.Lerp(new Color(0.4f, 0.1f, 0.1f), new Color(1f, 0.2f, 0.2f), Approach));
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
