using System;
using System.Collections;
using Meta.XR;
using Oculus.Interaction.Input;
using Unity.InferenceEngine;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// Finds the cup in the passthrough camera feed and places a target (the proxy mug) on it.
    ///
    /// <code>
    /// PassthroughCameraAccess texture (1280x960) + camera pose at capture
    ///   -> YOLO11n (COCO, on-device, Unity Inference Engine, GPU) at 640x640
    ///   -> on the GPU: best "cup" box + best box of any class (for debugging), 11 floats read back
    ///   -> ray through the box centre
    ///   -> distance from environment depth (EnvironmentRaycastManager) and from the box's
    ///      apparent height vs the real mug height; Auto prefers depth when the two agree
    ///   -> cup centre
    /// Hand hand-off: while the cup is seen with a tracked hand within reach, the cup's offset
    /// from that wrist is remembered. When vision then loses it (a held mug is often seen as a
    /// "mouse" or "cell phone", or not at all) the target follows the hand. A clear detection
    /// takes over again; weak ones far from the hand are ignored.
    /// </code>
    ///
    /// Everything the debug view shows is public on this component, and each change of state
    /// is logged with the prefix [CupDetector].
    /// </summary>
    public class CupDetector : MonoBehaviour
    {
        public enum DistanceMode { Auto, DepthOnly, SizeOnly }
        public enum Source { None, Vision, Hand }

        [Header("Inputs")]
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] EnvironmentRaycastManager raycaster;
        [SerializeField] Transform head;
        [Tooltip("Interaction SDK hand data sources (OVRHandDataSourceLeft / Right).")]
        [SerializeField] Hand leftHand;
        [SerializeField] Hand rightHand;

        [Header("Model (YOLO11n, COCO)")]
        [SerializeField] ModelAsset modelAsset;
        [SerializeField] TextAsset classLabels;
        [SerializeField] BackendType backend = BackendType.GPUCompute;
        [Tooltip("COCO class index of 'cup' in the labels file.")]
        [SerializeField] int cupClass = 41;
        [Tooltip("Layers scheduled per frame, so inference doesn't stall a single frame.")]
        [SerializeField, Range(5, 400)] int layersPerFrame = 40;

        [Header("Detection")]
        [Tooltip("Minimum 'cup' score. Tested on the headset recording: the mug on a table scores 0.7-0.93; " +
                 "a lamp base scored up to 0.35.")]
        [SerializeField, Range(0f, 1f)] float minScore = 0.45f;
        [Tooltip("While following a hand, a detection further than this from the hand's estimate is ignored " +
                 "unless it scores above Strong Score (m).")]
        [SerializeField] float gateDistance = 0.3f;
        [SerializeField, Range(0f, 1f)] float strongScore = 0.7f;
        [Tooltip("While a tracked hand holds the cup, any detection further than this from it is ignored, " +
                 "whatever its score: it's another cup or a bad distance (m).")]
        [SerializeField] float holdGateDistance = 0.5f;
        [Tooltip("Vision counts as current for this long after the last accepted detection (s).")]
        [SerializeField] float visionFresh = 0.3f;
        [Tooltip("Hide the target when there is no vision and no holding hand for this long (s).")]
        [SerializeField] float lostAfter = 1.0f;
        [SerializeField] DistanceMode distanceMode = DistanceMode.Auto;
        [Tooltip("Editor only: analyse this image instead of the camera feed (e.g. a frame from a headset " +
                 "recording). Rays still use the (mock) camera, so distances are only indicative.")]
        [SerializeField] Texture editorTestImage;

        [Header("Hand hand-off")]
        [Tooltip("A hand whose palm is this close to the cup centre (m) when the cup is seen counts as holding it. " +
                 "It lets go only when the cup is seen more than 1.5x this far from that hand while the hand is tracked.")]
        [SerializeField] float attachRadius = 0.15f;

        [Header("Real mug (m)")]
        [SerializeField] float mugHeight = 0.095f;
        [SerializeField] float mugRadius = 0.040f;

        [Header("Output")]
        [Tooltip("Placed at the base centre of the cup, +Z towards the viewer (the proxy mug).")]
        [SerializeField] Transform target;
        [SerializeField] float followSpeed = 12f;
        [Tooltip("Jumps larger than this snap instead of gliding (m).")]
        [SerializeField] float snapDistance = 0.25f;

        // ------------------------------------------------------------ state, read by CupDebugView

        public string Status { get; private set; } = "Starting";
        public bool ModelReady { get; private set; }
        public int FramesAnalysed { get; private set; }
        public float InferenceMs { get; private set; }
        public float AnalysedPerSecond { get; private set; }

        /// <summary>True if the last analysed frame had a cup above minScore.</summary>
        public bool CupInLastFrame { get; private set; }
        public float CupScore { get; private set; }
        /// <summary>Cup box in camera viewport coordinates (0..1, origin bottom-left).</summary>
        public Rect CupViewportRect { get; private set; }
        public string BestLabel { get; private set; } = "-";
        public float BestScore { get; private set; }
        public Rect BestViewportRect { get; private set; }
        /// <summary>Why the last detection above minScore was not used, or empty.</summary>
        public string Rejected { get; private set; } = "";

        public EnvironmentRaycastHitStatus DepthStatus { get; private set; } = EnvironmentRaycastHitStatus.NotReady;
        /// <summary>Distance to the cup centre from depth (front surface + radius), or NaN.</summary>
        public float DepthDistance { get; private set; } = float.NaN;
        /// <summary>Distance to the cup centre from its apparent height, or NaN.</summary>
        public float SizeDistance { get; private set; } = float.NaN;
        public string DistanceSource { get; private set; } = "-";
        public Vector3 DepthHitPoint { get; private set; }
        /// <summary>Last cup centre from vision.</summary>
        public Vector3 VisionCentre { get; private set; }
        public Pose FrameCameraPose { get; private set; }
        public float LastSeenTime { get; private set; } = -99f;

        /// <summary>Where the target is being driven from this frame.</summary>
        public Source TrackingSource { get; private set; } = Source.None;
        public bool Tracking => TrackingSource != Source.None;
        /// <summary>Current cup centre (from vision or the holding hand).</summary>
        public Vector3 CupCentre { get; private set; }
        /// <summary>"L" or "R" while a hand is attached, else empty.</summary>
        public string HoldingHand => _holdHand == null ? "" : _holdHand == leftHand ? "L" : "R";
        /// <summary>Palm-to-cup distance of the nearest tracked hand at the last detection, or NaN.</summary>
        public float NearestHandDistance { get; private set; } = float.NaN;

        /// <summary>The frame the last result came from (copied only while Keep Debug Frame is on).</summary>
        public RenderTexture DebugFrame => _debugFrame;
        public bool KeepDebugFrame { get; set; }
        public int ResultVersion { get; private set; }

        /// <summary>When frozen, detection keeps running but the target stays where it is.</summary>
        public bool Frozen { get; set; }
        public DistanceMode Mode { get => distanceMode; set => distanceMode = value; }

        // ------------------------------------------------------------ internals

        Worker _worker;
        Tensor<float> _input;
        int _inputW = 640, _inputH = 640;
        string[] _labels;
        bool _busy;
        RenderTexture _debugFrame;
        float _rateWindowStart;
        int _rateWindowCount;
        bool _snapNext = true;
        string _lastLoggedStatus;
        Source _lastSource = Source.None;

        Hand _holdHand;
        Pose _holdOffset;        // cup base pose in the holding hand's wrist space
        Pose _cupPose;           // desired target pose this frame
        float _handLostTime = -99f;
        bool _loggedHandDropout, _loggedFarReject;

        void Reset()
        {
            cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            raycaster = FindAnyObjectByType<EnvironmentRaycastManager>();
        }

        void Start()
        {
            if (classLabels != null)
                _labels = classLabels.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (target != null) target.gameObject.SetActive(false);
            StartCoroutine(LoadModel());
        }

        IEnumerator LoadModel()
        {
            SetStatus("Loading model");
            yield return null;   // let a frame render first; loading blocks the main thread
            if (modelAsset == null) { SetStatus("ERROR: no model assigned"); yield break; }

            Model model;
            try
            {
                var source = ModelLoader.Load(modelAsset);
                var shape = source.inputs[0].shape;   // (1, 3, 640, 640)
                if (shape.IsStatic())
                {
                    var s = shape.ToTensorShape();
                    _inputH = s[2]; _inputW = s[3];
                }
                model = BuildHeadGraph(source, cupClass);
            }
            catch (Exception e) { SetStatus("ERROR loading model: " + e.Message); Debug.LogException(e); yield break; }
            yield return null;

            try
            {
                _worker = new Worker(model, backend);
                _input = new Tensor<float>(new TensorShape(1, 3, _inputH, _inputW));
            }
            catch (Exception e) { SetStatus("ERROR creating worker: " + e.Message); Debug.LogException(e); yield break; }

            ModelReady = true;
            Debug.Log($"[CupDetector] Model ready: {_inputW}x{_inputH}, backend {backend}, cup class {cupClass} " +
                      $"('{Label(cupClass)}'), {(_labels?.Length ?? 0)} labels");
        }

        /// <summary>
        /// Appends the post-processing to YOLO's raw output (1, 84, 8400): rows 0-3 are the box
        /// (centre x, centre y, width, height, in input pixels), rows 4-83 the class scores.
        /// Output: 11 floats = cup box (4), cup score, best score of any class, its class index, its box (4).
        /// </summary>
        static Model BuildHeadGraph(Model source, int cupClass)
        {
            var graph = new FunctionalGraph();
            var inputs = graph.AddInputs(source);
            var raw = Functional.Forward(source, inputs)[0];
            var x = raw[0, .., ..];                                   // (84, 8400)
            var boxes = x[0..4, ..];                                  // (4, 8400)
            var classes = x[4.., ..];                                 // (80, 8400)

            var cupScores = x[4 + cupClass, ..];                      // (8400)
            var cupIdx = Functional.ArgMax(cupScores, 0, true);       // (1)
            var cupBox = boxes.IndexSelect(1, cupIdx).Reshape(new[] { 4 });
            var cupScore = cupScores.IndexSelect(0, cupIdx);          // (1)

            var perAnchorMax = Functional.ReduceMax(classes, 0);      // (8400)
            var perAnchorClass = Functional.ArgMax(classes, 0);       // (8400)
            var bestIdx = Functional.ArgMax(perAnchorMax, 0, true);   // (1)
            var bestScore = perAnchorMax.IndexSelect(0, bestIdx);
            var bestClass = perAnchorClass.IndexSelect(0, bestIdx).Float();
            var bestBox = boxes.IndexSelect(1, bestIdx).Reshape(new[] { 4 });

            graph.AddOutput(Functional.Concat(new[] { cupBox, cupScore, bestScore, bestClass, bestBox }, 0), "result");
            return graph.Compile();
        }

        void Update()
        {
            UpdateStatus();
            if (ModelReady && !_busy && cameraAccess != null && cameraAccess.IsPlaying && cameraAccess.IsUpdatedThisFrame)
                StartCoroutine(Analyse());
            UpdateSource();
            UpdateTarget();
        }

        void UpdateStatus()
        {
            if (!ModelReady) return;   // LoadModel sets its own status
            if (cameraAccess == null) SetStatus("ERROR: no PassthroughCameraAccess");
            else if (!cameraAccess.IsPlaying) SetStatus("Waiting for camera (permission?)");
            else if (TrackingSource == Source.Vision) SetStatus("Tracking cup (camera)");
            else if (TrackingSource == Source.Hand)
                SetStatus(HoldingHand == "" ? "Tracking cup (holding last position)" : $"Tracking cup (following {HoldingHand} hand)");
            else SetStatus("Looking for a cup");
        }

        IEnumerator Analyse()
        {
            _busy = true;
            float t0 = Time.realtimeSinceStartup;
            var pose = cameraAccess.GetCameraPose();
            var tex = cameraAccess.GetTexture();
            if (Application.isEditor && editorTestImage != null) tex = editorTestImage;
            if (tex == null) { _busy = false; yield break; }

            TextureConverter.ToTensor(tex, _input);
            if (KeepDebugFrame) CopyDebugFrame(tex);

            var schedule = _worker.ScheduleIterable(_input);
            int n = 0;
            while (true)
            {
                bool more;
                try { more = schedule.MoveNext(); }
                catch (Exception e) { SetStatus("ERROR running model: " + e.Message); Debug.LogException(e); ModelReady = false; _busy = false; yield break; }
                if (!more) break;
                if (++n % layersPerFrame == 0) yield return null;
            }

            var output = _worker.PeekOutput("result") as Tensor<float>;
            output.ReadbackRequest();   // non-blocking GPU -> CPU copy
            while (!output.IsReadbackRequestDone()) yield return null;
            float[] r;
            using (var cpu = output.ReadbackAndClone()) r = cpu.DownloadToArray();

            InferenceMs = (Time.realtimeSinceStartup - t0) * 1000f;
            HandleResult(r, pose);
            CountRate();
            _busy = false;
        }

        void HandleResult(float[] r, Pose pose)
        {
            FramesAnalysed++;
            ResultVersion++;
            FrameCameraPose = pose;

            CupScore = r[4];
            CupViewportRect = ToViewport(r[0], r[1], r[2], r[3]);
            BestScore = r[5];
            BestLabel = Label(Mathf.RoundToInt(r[6]));
            BestViewportRect = ToViewport(r[7], r[8], r[9], r[10]);

            bool was = CupInLastFrame;
            CupInLastFrame = CupScore >= minScore;
            if (CupInLastFrame != was)
                Debug.Log(CupInLastFrame
                    ? $"[CupDetector] Cup seen, score {CupScore:0.00}"
                    : $"[CupDetector] Cup not seen (best now '{BestLabel}' {BestScore:0.00})");
            if (!CupInLastFrame) return;

            if (!EstimateCentre(pose, out var centre)) { Rejected = "distance " + DistanceSource; return; }

            // While a tracked hand holds the cup, a detection far from that hand is a different cup
            // (or a bad distance), whatever its score. Seen on device: "cups" ~3 m away broke the hold.
            if (TryGetHeldCentre(out var held))
            {
                float off = Vector3.Distance(centre, held);
                if (off > holdGateDistance)
                {
                    Rejected = $"{off:0.00} m from the held cup (score {CupScore:0.00}, {DistanceSource})";
                    if (!_loggedFarReject) Debug.Log($"[CupDetector] Ignored a cup {off:0.00} m from the held one " +
                                                     $"(score {CupScore:0.00}, distance from {DistanceSource})");
                    _loggedFarReject = true;
                    return;
                }
            }
            _loggedFarReject = false;

            // While following a hand, ignore a weak detection somewhere else (e.g. the lamp).
            if (TrackingSource == Source.Hand && CupScore < strongScore &&
                Vector3.Distance(centre, CupCentre) > gateDistance)
            {
                Rejected = $"{Vector3.Distance(centre, CupCentre):0.00} m from the hand's cup";
                return;
            }

            Rejected = "";
            VisionCentre = centre;
            LastSeenTime = Time.time;
            UpdateHold(VisionPose(centre));
        }

        /// <summary>YOLO box (input pixels, y down) to camera viewport (0..1, y up).</summary>
        Rect ToViewport(float cx, float cy, float w, float h)
        {
            float x0 = (cx - w * 0.5f) / _inputW, x1 = (cx + w * 0.5f) / _inputW;
            float yTop = cy - h * 0.5f, yBottom = cy + h * 0.5f;
            float v0 = 1f - yBottom / _inputH, v1 = 1f - yTop / _inputH;
            return Rect.MinMaxRect(x0, v0, x1, v1);
        }

        bool EstimateCentre(Pose pose, out Vector3 centre)
        {
            centre = default;
            var box = CupViewportRect;
            var ray = cameraAccess.ViewportPointToRay(box.center, pose);
            if (ray.direction == Vector3.zero) return false;

            // 1. Depth: the ray hits the cup's front surface, so add the radius.
            DepthDistance = float.NaN;
            if (raycaster != null && EnvironmentRaycastManager.IsSupported)
            {
                raycaster.Raycast(ray, out var hit, 4f);
                DepthStatus = hit.status;
                if (hit.status == EnvironmentRaycastHitStatus.Hit)
                {
                    DepthHitPoint = hit.point;
                    DepthDistance = Vector3.Distance(ray.origin, hit.point) + mugRadius;
                }
            }
            else DepthStatus = EnvironmentRaycastHitStatus.NotSupported;

            // 2. Size: the angle the box spans vertically vs the mug's real height.
            var top = cameraAccess.ViewportPointToRay(new Vector2(box.center.x, box.yMax), pose).direction;
            var bottom = cameraAccess.ViewportPointToRay(new Vector2(box.center.x, box.yMin), pose).direction;
            float angle = Vector3.Angle(top, bottom) * Mathf.Deg2Rad;
            SizeDistance = angle > 1e-4f ? mugHeight * 0.5f / Mathf.Tan(angle * 0.5f) : float.NaN;

            float d;
            switch (distanceMode)
            {
                case DistanceMode.DepthOnly: d = DepthDistance; DistanceSource = "depth"; break;
                case DistanceMode.SizeOnly: d = SizeDistance; DistanceSource = "size"; break;
                default:
                    // Depth is better when it agrees with the size estimate; it can miss a small,
                    // close object (low-res depth, or the ray hitting the hand or the table behind).
                    bool depthOk = !float.IsNaN(DepthDistance) &&
                                   (float.IsNaN(SizeDistance) || Mathf.Abs(DepthDistance - SizeDistance) < 0.35f * SizeDistance);
                    d = depthOk ? DepthDistance : SizeDistance;
                    DistanceSource = depthOk ? "depth" : "size";
                    break;
            }
            if (float.IsNaN(d) || d < 0.1f || d > 4f) { DistanceSource += " (rejected)"; return false; }
            centre = ray.origin + ray.direction.normalized * d;
            return true;
        }

        /// <summary>Cup base pose from a vision centre: upright, logo (+Z) facing the viewer.</summary>
        Pose VisionPose(Vector3 centre)
        {
            var viewer = head != null ? head.position : FrameCameraPose.position;
            var toViewer = Vector3.ProjectOnPlane(viewer - centre, Vector3.up);
            var rot = toViewer.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toViewer.normalized, Vector3.up) : Quaternion.identity;
            return new Pose(centre - Vector3.up * (mugHeight * 0.5f), rot);
        }

        // ------------------------------------------------------------ hand hand-off

        /// <summary>On each accepted detection: attach to the nearest hand within reach, or let go.</summary>
        void UpdateHold(Pose cupPose)
        {
            Hand nearest = null;
            float best = float.MaxValue;
            foreach (var h in new[] { leftHand, rightHand })
            {
                if (!TryGetPalm(h, out var palm, out _)) continue;
                float d = Vector3.Distance(palm, cupPose.position + Vector3.up * (mugHeight * 0.5f));
                if (d < best) { best = d; nearest = h; }
            }
            NearestHandDistance = nearest != null ? best : float.NaN;
            var cupCentre = cupPose.position + Vector3.up * (mugHeight * 0.5f);

            // Attach (or refresh the offset) when a tracked palm is within reach.
            if (nearest != null && best < attachRadius && TryGetPalm(nearest, out _, out var wrist))
            {
                if (_holdHand != nearest) Debug.Log($"[CupDetector] Attached to {HandName(nearest)} hand ({best:0.00} m)");
                _holdHand = nearest;
                _holdOffset = new Pose(
                    Quaternion.Inverse(wrist.rotation) * (cupPose.position - wrist.position),
                    Quaternion.Inverse(wrist.rotation) * cupPose.rotation);
                return;
            }
            if (_holdHand == null) return;

            // Let go only when the cup is seen clearly away from the holding hand while that hand is
            // tracked (hysteresis: 1.5x the attach radius). Hand tracking often drops for a moment
            // while fingers wrap round the mug; that must not release it (seen on device).
            if (!TryGetPalm(_holdHand, out var holdPalm, out _))
            {
                if (!_loggedHandDropout) Debug.Log($"[CupDetector] {HandName(_holdHand)} hand not tracked; keeping hold");
                _loggedHandDropout = true;
                return;
            }
            _loggedHandDropout = false;
            float away = Vector3.Distance(holdPalm, cupCentre);
            if (away > attachRadius * 1.5f)
            {
                Debug.Log($"[CupDetector] Released from {HandName(_holdHand)} hand: cup seen {away:0.00} m from its palm");
                _holdHand = null;
            }
        }

        string HandName(Hand h) => h == leftHand ? "left" : "right";

        /// <summary>Where the holding hand puts the cup centre, if a hand holds it and is tracked.</summary>
        bool TryGetHeldCentre(out Vector3 centre)
        {
            centre = default;
            if (_holdHand == null || !TryGetPalm(_holdHand, out _, out var wrist)) return false;
            var basePos = wrist.position + wrist.rotation * _holdOffset.position;
            var rot = wrist.rotation * _holdOffset.rotation;
            centre = basePos + rot * (Vector3.up * (mugHeight * 0.5f));
            return true;
        }

        bool TryGetPalm(Hand hand, out Vector3 palm, out Pose wrist)
        {
            palm = default; wrist = default;
            if (hand == null || !hand.IsTrackedDataValid) return false;
            if (!hand.GetJointPose(HandJointId.HandWristRoot, out wrist) ||
                !hand.GetJointPose(HandJointId.HandIndex1, out var index) ||
                !hand.GetJointPose(HandJointId.HandMiddle1, out var middle) ||
                !hand.GetJointPose(HandJointId.HandPinky1, out var pinky)) return false;
            var knuckles = (index.position + middle.position + pinky.position) / 3f;
            palm = Vector3.Lerp(wrist.position, knuckles, 0.7f);
            return true;
        }

        void UpdateSource()
        {
            var source = Source.None;
            if (Time.time - LastSeenTime < visionFresh)
            {
                source = Source.Vision;
                _cupPose = VisionPose(VisionCentre);
            }
            else if (_holdHand != null && TryGetPalm(_holdHand, out _, out var wrist))
            {
                source = Source.Hand;
                _handLostTime = Time.time;
                _cupPose = new Pose(wrist.position + wrist.rotation * _holdOffset.position, wrist.rotation * _holdOffset.rotation);
            }
            else if (_lastSource != Source.None && Time.time - Mathf.Max(LastSeenTime, _handLostTime) < lostAfter)
            {
                source = _lastSource;   // short gap: hold the last pose
            }

            if (source != _lastSource) Debug.Log($"[CupDetector] Source: {_lastSource} -> {source}");
            _lastSource = source;
            TrackingSource = source;
            if (source != Source.None) CupCentre = _cupPose.position + _cupPose.rotation * (Vector3.up * (mugHeight * 0.5f));
        }

        void UpdateTarget()
        {
            if (target == null) return;
            bool show = Tracking || Frozen;
            if (target.gameObject.activeSelf != show)
            {
                target.gameObject.SetActive(show);
                _snapNext = true;
            }
            if (!show || Frozen) return;

            if (_snapNext || Vector3.Distance(target.position, _cupPose.position) > snapDistance)
            {
                target.SetPositionAndRotation(_cupPose.position, _cupPose.rotation);
                _snapNext = false;
            }
            else
            {
                float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
                target.SetPositionAndRotation(Vector3.Lerp(target.position, _cupPose.position, t),
                                              Quaternion.Slerp(target.rotation, _cupPose.rotation, t));
            }
        }

        // ------------------------------------------------------------ misc

        void CopyDebugFrame(Texture tex)
        {
            if (_debugFrame == null)
            {
                _debugFrame = new RenderTexture(320, 240, 0, RenderTextureFormat.ARGB32) { name = "CupDetectorDebugFrame" };
                _debugFrame.Create();
            }
            Graphics.Blit(tex, _debugFrame);
        }

        void CountRate()
        {
            _rateWindowCount++;
            float now = Time.realtimeSinceStartup;
            if (now - _rateWindowStart >= 1f)
            {
                AnalysedPerSecond = _rateWindowCount / (now - _rateWindowStart);
                _rateWindowStart = now;
                _rateWindowCount = 0;
            }
        }

        string Label(int i) => _labels != null && i >= 0 && i < _labels.Length ? _labels[i] : $"#{i}";

        void SetStatus(string s)
        {
            Status = s;
            if (s == _lastLoggedStatus) return;
            _lastLoggedStatus = s;
            if (s.StartsWith("ERROR")) Debug.LogError("[CupDetector] " + s);
            else Debug.Log("[CupDetector] " + s);
        }

        void OnDestroy()
        {
            _worker?.Dispose();
            _input?.Dispose();
            if (_debugFrame != null) _debugFrame.Release();
        }
    }
}
