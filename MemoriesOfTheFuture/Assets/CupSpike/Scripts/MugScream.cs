using System.Collections;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;
using Memories.Audio;

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

        [Header("Sound (files, levels and space live in the Sound Slot assets: Memories > Sound Design)")]
        [Tooltip("Played from inside the mug. Restarts rather than overlapping.")]
        [SerializeField] SoundSlot screamSlot;
        [Tooltip("Looping breath from inside the mug while a hand approaches.")]
        [SerializeField] SoundSlot breathSlot;
        [Tooltip("The breath's pitch, scaled from far (x) to touching (y), on top of the slot's pitch.")]
        [SerializeField] Vector2 breathPitch = new Vector2(0.9f, 1.15f);
        [Tooltip("Looping background bed, stereo, head-locked. Used only if the left/right pair below has no sound.")]
        [SerializeField] SoundSlot backgroundSlot;
        [Tooltip("The bed's left and right channels as mono clips. Played from two fixed points in the room " +
                 "(world-locked, spatialised), so the bed stays put when you turn your head.")]
        [SerializeField] SoundSlot backgroundLeftSlot;
        [SerializeField] SoundSlot backgroundRightSlot;
        [Tooltip("The two bed emitters sit this far left and right of where you start, at head height (m).")]
        [SerializeField] float bedSpread = 3f;
        [Tooltip("...and this far in front (m).")]
        [SerializeField] float bedForward = 1f;
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
        [SerializeField] bool showZone = false;

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
        /// <summary>While true (e.g. dreaming), the approach and touch don't fire.</summary>
        public bool Suppressed { get; set; }
        /// <summary>Extra multiplier on the background bed (e.g. muffled during the dream).</summary>
        public float ExternalDuck { get; set; } = 1f;
        public bool ScreamPlaying => _scream != null && _scream.Source.isPlaying;
        public bool InSilence => _inBeat;

        /// <summary>A fingertip position supplied from outside (the desktop debug build's simulated hand).</summary>
        public Vector3? SimulatedFingertip { get; set; }

        // Tuning (used by the desktop debug build's panel). Levels live in the Sound Slots.
        public float ApproachStart { get => approachStart; set => approachStart = Mathf.Max(TouchZone + 0.02f, value); }
        public float SilenceBeat { get => silenceBeat; set => silenceBeat = Mathf.Max(0f, value); }

        public string StatusLine =>
            $"scream: {(HasZone ? (_inBeat ? "SILENCE" : Armed ? (Approach > 0f ? $"breathing {Approach:0.00}" : "armed") : "re-arming") : "no mug yet")}" +
            $"  touch {TouchZone * 100f:0} cm ({ZoneMode})  tip {(float.IsNaN(NearestTip) ? "-" : $"{NearestTip * 100f:0} cm")}  " +
            $"screams {Screams}  miss {Misses}  false {FalseFires}";

        int _preset;
        Pose _zone;              // mug base pose the zones are around
        float _lastScream = -99f;
        SlotVoice _scream, _breath;
        readonly System.Collections.Generic.List<SlotVoice> _beds = new System.Collections.Generic.List<SlotVoice>();
        float _bedLevel;          // 0..1 fade-in progress
        bool _bedPlaced;
        Vector3 _bedLeftAt, _bedRightAt;
        float _duck = 1f;
        bool _inBeat;
        bool _breathing;
        float _breathPeak;
        LineRenderer _ringTop, _ringBottom, _approachRing;
        bool _wasPinching;

        void Start()
        {
            _scream = SlotVoice.Create(screamSlot, transform, "ScreamSource", false);
            _scream.Level = 1f;
            _breath = SlotVoice.Create(breathSlot, transform, "BreathSource", true);

            RebuildBed();

            _ringTop = Ring("TouchTop");
            _ringBottom = Ring("TouchBottom");
            _approachRing = Ring("ApproachRing");
            Debug.Log($"[MugScream] Ready: approach {approachStart * 100f:0} cm, touch {TouchZone * 100f:0} cm, " +
                      $"scream '{ClipName(_scream)}', breath '{ClipName(_breath)}', bed: {_beds.Count} source(s)");
        }

        /// <summary>
        /// (Re)start the background bed: the world-locked left/right pair if both have sound, else the
        /// stereo Background slot. A stereo file loaded from the desktop sounds folder (Background's
        /// RuntimeOverride) wins over a built-in pair, so one dropped file replaces the whole bed.
        /// </summary>
        public void RebuildBed()
        {
            foreach (var b in _beds) if (b.Source != null) Destroy(b.Source.gameObject);
            _beds.Clear();
            bool stereoDropped = backgroundSlot != null && backgroundSlot.RuntimeOverride != null &&
                                 (backgroundLeftSlot == null || backgroundLeftSlot.RuntimeOverride == null) &&
                                 (backgroundRightSlot == null || backgroundRightSlot.RuntimeOverride == null);
            if (!stereoDropped && SlotVoice.SlotHasSound(backgroundLeftSlot) && SlotVoice.SlotHasSound(backgroundRightSlot))
            {
                // World-locked pair, started together so the channels stay in sync.
                var left = Bed("BackgroundBedLeft", backgroundLeftSlot);
                var right = Bed("BackgroundBedRight", backgroundRightSlot);
                left.Tick(); right.Tick();
                double start = AudioSettings.dspTime + 0.1;
                left.Source.PlayScheduled(start);
                right.Source.PlayScheduled(start);
                StartCoroutine(PlaceBedOnceTracked(left.Source.transform, right.Source.transform));
            }
            else if (SlotVoice.SlotHasSound(backgroundSlot))
                Bed("BackgroundBed", backgroundSlot).Play();
        }

        static string ClipName(SlotVoice v) => v.Source.clip != null ? v.Source.clip.name : "NONE";

        /// <summary>A looping background voice. Silent until played; <see cref="UpdateBed"/> sets its level.</summary>
        SlotVoice Bed(string name, SoundSlot slot)
        {
            var v = SlotVoice.Create(slot, transform, name, true);
            _beds.Add(v);
            return v;
        }

        /// <summary>Place the bed pair left and right of the viewer, once head tracking has a pose.</summary>
        IEnumerator PlaceBedOnceTracked(Transform left, Transform right)
        {
            if (_bedPlaced)
            {
                // Rebuilt (the desktop app loaded a new file): keep the pair where it was.
                left.position = _bedLeftAt;
                right.position = _bedRightAt;
                yield break;
            }
            var cam = Camera.main;
            for (float t = 0f; t < 2f && cam != null && cam.transform.localPosition == Vector3.zero; t += Time.deltaTime) yield return null;
            if (left == null || right == null) yield break;   // rebuilt meanwhile
            var head = cam != null ? cam.transform : transform;
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            left.position = head.position + yaw * new Vector3(-bedSpread, 0f, bedForward);
            right.position = head.position + yaw * new Vector3(bedSpread, 0f, bedForward);
            _bedLeftAt = left.position;
            _bedRightAt = right.position;
            _bedPlaced = true;
            Debug.Log($"[MugScream] Background bed placed in the room: L {left.position:F1}  R {right.position:F1}");
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
            _scream.Source.transform.position = MugCentre(_zone);
            _scream.Stop();
            _scream.Play();   // restart, never stack a 12 s scream
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

            if (HasZone && SimulatedFingertip.HasValue)
            {
                float d = SurfaceDistance(SimulatedFingertip.Value);
                if (float.IsNaN(NearestTip) || d < NearestTip) { NearestTip = d; nearestHand = "simulated"; }
            }

            // Re-arm once the scream has finished, the cooldown has passed and every fingertip is clear.
            if (!Armed && !_inBeat && !_scream.Source.isPlaying && Time.time - _lastScream > cooldown &&
                (float.IsNaN(NearestTip) || NearestTip > TouchZone + rearmMargin))
            {
                Armed = true;
                Debug.Log("[MugScream] Re-armed");
            }

            bool live = Armed && HasZone && !held && !Suppressed && !float.IsNaN(NearestTip);
            if (live && NearestTip < TouchZone)
                StartCoroutine(TouchSequence($"{nearestHand} index {NearestTip * 100f:0.0} cm from the mug"));
            else
                UpdateBreath(live);

            if (_scream.Source.isPlaying) _scream.Source.transform.position = MugCentre(_zone);   // a scream in progress follows the mug
            _breath.Source.transform.position = MugCentre(_zone);
            UpdateBed();
            _scream.Tick();
            _breath.Tick();
            foreach (var bed in _beds) bed.Tick();
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
            if (!_breathing && _breath.HasSound)
            {
                _breathing = true;
                _breathPeak = 0f;
                _breath.Level = 0f;
                _breath.Play();
                Debug.Log($"[MugScream] Approach: breath starts ({NearestTip * 100f:0} cm)");
            }
            _breathPeak = Mathf.Max(_breathPeak, a);
            _breath.Level = Mathf.MoveTowards(_breath.Level, a, Time.deltaTime * 3f);
            _breath.PitchScale = Mathf.Lerp(breathPitch.x, breathPitch.y, a);
        }

        void StopBreath()
        {
            _breathing = false;
            Approach = 0f;
            if (_breath != null) { _breath.Stop(); _breath.Level = 0f; _breath.Tick(); }
        }

        /// <summary>Fade the background in; drain it as a hand approaches, cut it in the silence, duck it under the scream.</summary>
        void UpdateBed()
        {
            if (_beds.Count == 0) return;
            _bedLevel = backgroundFadeIn > 0f ? Mathf.MoveTowards(_bedLevel, 1f, Time.deltaTime / backgroundFadeIn) : 1f;
            float target = _inBeat ? 0f : _scream.Source.isPlaying ? duckTo : 1f - approachDrain * Approach;
            _duck = _inBeat ? 0f : Mathf.MoveTowards(_duck, target, Time.deltaTime * duckSpeed);
            foreach (var bed in _beds) bed.Level = _bedLevel * _duck * Mathf.Clamp01(ExternalDuck);
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
