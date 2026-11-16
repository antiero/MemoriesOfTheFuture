using System;
using System.Collections;
using Meta.XR;
using Oculus.Interaction.Input;
using Unity.InferenceEngine;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// Finds the cup and places a target (the proxy mug) on it. Two sources:
    ///
    /// <b>Camera</b> (cup not held):
    /// <code>
    /// PassthroughCameraAccess texture (1280x960) + camera pose at capture
    ///   -> YOLO11n (COCO, on-device, Unity Inference Engine, GPU) at 640x640
    ///   -> on the GPU: best "cup" box + best box of any class (for debugging), 11 floats read back
    ///   -> ray through the box centre; distance from environment depth or from the box height vs the
    ///      mug's apparent height at that viewing angle (Auto prefers depth when the two agree)
    ///   -> upright mug, logo towards the viewer
    /// </code>
    ///
    /// <b>Hand</b> (cup held by the handle): the pose comes only from the hand joints, through the
    /// handle geometry (<see cref="MugGrip"/>). The camera only decides when the cup is picked up:
    /// a gripping hand whose mug would sit inside the last detected cup box (projected into that
    /// frame, so the camera's unreliable distance isn't used) attaches. Opening the hand lets go.
    /// Detections of other cups can't break the hold.
    ///
    /// Everything the debug view shows is public here, and each change of state is logged with
    /// the prefix [CupDetector].
    /// </summary>
    public class CupDetector : MonoBehaviour
    {
        public enum DistanceMode { Auto, DepthOnly, SizeOnly }
        public enum Source { None, Vision, Hand }
        public enum HeldTilt { Rigid, Damped, Upright }

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
        [Tooltip("Vision counts as current for this long after the last accepted detection (s).")]
        [SerializeField] float visionFresh = 0.3f;
        [Tooltip("Hide the target when there is no current source for this long (s).")]
        [SerializeField] float lostAfter = 1.0f;
        [SerializeField] DistanceMode distanceMode = DistanceMode.Auto;
        [Tooltip("Editor only: analyse this image instead of the camera feed (e.g. a frame from a headset " +
                 "recording). Rays still use the (mock) camera, so distances are only indicative.")]
        [SerializeField] Texture editorTestImage;

        [Header("Holding (hand joints -> mug through the handle)")]
        [SerializeField] MugGrip grip = new MugGrip();
        [Tooltip("A gripping hand can pick up a cup seen within this long (s).")]
        [SerializeField] float pickUpWindow = 1.0f;
        [Tooltip("The detected box is grown by this fraction of its size when checking a hand's mug is inside it.")]
        [SerializeField] float pickUpBoxMargin = 0.35f;
        [Tooltip("...and the hand's mug must be within this distance of the camera estimate (m). Loose: the " +
                 "camera's distance can be ~0.15 m out.")]
        [SerializeField] float pickUpDepthTolerance = 0.3f;
        [Tooltip("The hand must stay open this long to let go (s).")]
        [SerializeField] float releaseAfterOpen = 0.25f;
        [Tooltip("While held, a camera sighting within this distance sideways of the held mug nudges the " +
                 "offset towards it (m). Further away: another cup, ignored.")]
        [SerializeField] float correctionGate = 0.08f;
        [Tooltip("Fraction of the sideways error corrected per sighting.")]
        [SerializeField, Range(0f, 1f)] float correctionGain = 0.35f;
        [Tooltip("Only correct while the wrist moves slower than this (m/s): camera frames are ~140 ms old.")]
        [SerializeField] float correctionMaxWristSpeed = 0.15f;
        [Tooltip("A held pose jumping further than this in one frame is a tracking glitch (m)...")]
        [SerializeField] float glitchJump = 0.08f;
        [Tooltip("...and is ignored for up to this long before it's accepted as real (s).")]
        [SerializeField] float glitchHold = 0.25f;
        [Tooltip("How the held mug follows the hand's tilt. Rigid: fully. Damped: past a dead zone, scaled. " +
                 "Upright: never (turning about the vertical always follows).")]
        [SerializeField] HeldTilt tiltMode = HeldTilt.Damped;
        [Tooltip("Damped: hand tilt below this is ignored (degrees). A hooked mug swings on the fingers.")]
        [SerializeField] float tiltDeadZone = 15f;
        [Tooltip("Damped: from this hand tilt up, the mug follows fully (degrees). In between it blends smoothly. " +
                 "A linear 60% scale was too weak on device: a ~90 degree tip showed as ~42.")]
        [SerializeField] float tiltFullAt = 50f;

        [Header("Real mug (m)")]
        [SerializeField] float mugHeight = 0.095f;
        [SerializeField] float mugRadius = 0.040f;

        [Header("Output")]
        [Tooltip("Placed at the base centre of the cup (the proxy mug).")]
        [SerializeField] Transform target;
        [SerializeField] float followSpeed = 12f;
        [Tooltip("While held, follow faster: the hand pose is already smooth (1/s).")]
        [SerializeField] float heldFollowSpeed = 30f;
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
        /// <summary>Current cup base pose (base centre, +Y up, handle on -X), valid while Tracking.</summary>
        public Pose CupPose => _cupPose;
        public float MugHeight => mugHeight;
        public float MugRadius => mugRadius;
        /// <summary>"L" or "R" while a hand holds the cup, else empty.</summary>
        public string HoldingHand => _holdHand == null ? "" : _holdHand == leftHand ? "L" : "R";
        /// <summary>Per-hand grip state for the debug panel, e.g. "L open 0.95  R grip 0.31".</summary>
        public string HandsText { get; private set; } = "";
        /// <summary>Held mug centre and rotation (for the debug gizmo).</summary>
        public Pose HeldHole { get; private set; }
        /// <summary>Held: how far the finger-only model was from the fused pose at pick-up, and the
        /// total sideways camera correction since (m), for the debug panel.</summary>
        public float PickUpModelError { get; private set; } = float.NaN;
        public float HeldCorrection { get; private set; }
        public int GlitchesIgnored { get; private set; }
        /// <summary>Held: the hand's tilt since pick-up, and the tilt applied to the mug (degrees).</summary>
        public float HandTilt { get; private set; }
        public float MugTilt { get; private set; }
        public HeldTilt TiltMode { get => tiltMode; set => tiltMode = value; }

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
        float _openSince = -1f;
        float _lastHandPoseTime = -99f;
        bool _loggedHandDropout;
        bool _leftCurled, _rightCurled;
        MugGrip.Result _left, _right;
        Pose _cupPose;              // desired target pose this frame
        // Held: fused at pick-up. The handle point in wrist space, and both rotations at that moment.
        Vector3 _handleInWrist;
        Quaternion _wristRotAtPickUp = Quaternion.identity, _mugRotAtPickUp = Quaternion.identity;
        Vector3 _lastWristPos;
        float _wristSpeed;
        float _glitchSince = -1f;
        // The last accepted detection, for picking up: box + camera pose + estimated distance.
        Rect _pickBox;
        Pose _pickPose;
        float _pickDistance;

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
            UpdateHands();
            UpdateHold();
            UpdateSource();
            UpdateTarget();
        }

        void UpdateStatus()
        {
            if (!ModelReady) return;   // LoadModel sets its own status
            if (cameraAccess == null) SetStatus("ERROR: no PassthroughCameraAccess");
            else if (!cameraAccess.IsPlaying) SetStatus("Waiting for camera (permission?)");
            else if (TrackingSource == Source.Vision) SetStatus("Tracking cup (camera)");
            else if (TrackingSource == Source.Hand) SetStatus($"Held in {(HoldingHand == "L" ? "left" : "right")} hand");
            else SetStatus("Looking for a cup");
        }

        // ------------------------------------------------------------ camera

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

            // While held, the hand drives the mug; a sighting of it only corrects sideways drift.
            // Other cups in the room can't move or release it (gated in CorrectHeldFromCamera).
            if (_holdHand != null) { CorrectHeldFromCamera(pose); return; }

            Rejected = "";
            VisionCentre = centre;
            LastSeenTime = Time.time;
            _pickBox = CupViewportRect;
            _pickPose = pose;
            _pickDistance = Vector3.Distance(pose.position, centre);
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

            // 2. Size: the angle the box spans vertically vs the mug's apparent height. Seen from
            // above, the rim's ellipse adds to the height: apparent = H cos(e) + 2R sin(e), where e is
            // the angle the view ray makes with the horizontal. Ignoring it put a mug seen from above
            // much too close (0.45 m on device).
            var top = cameraAccess.ViewportPointToRay(new Vector2(box.center.x, box.yMax), pose).direction;
            var bottom = cameraAccess.ViewportPointToRay(new Vector2(box.center.x, box.yMin), pose).direction;
            float angle = Vector3.Angle(top, bottom) * Mathf.Deg2Rad;
            float elevation = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(ray.direction.normalized, Vector3.up), -1f, 1f)));
            float apparent = mugHeight * Mathf.Cos(elevation) + 2f * mugRadius * Mathf.Sin(elevation);
            SizeDistance = angle > 1e-4f ? apparent * 0.5f / Mathf.Tan(angle * 0.5f) : float.NaN;

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

        // ------------------------------------------------------------ hands

        void UpdateHands()
        {
            _left = grip.Evaluate(leftHand, true);
            _right = grip.Evaluate(rightHand, false);
            _leftCurled = Curled(_left, _leftCurled);
            _rightCurled = Curled(_right, _rightCurled);
            HandsText = $"L {HandState(_left, _leftCurled)}   R {HandState(_right, _rightCurled)}";
        }

        bool Curled(MugGrip.Result r, bool was)
        {
            if (!r.tracked) return was;   // keep the last state through dropouts
            return was ? r.straightness < grip.openAbove : r.straightness < grip.curledBelow;
        }

        static string HandState(MugGrip.Result r, bool curled) =>
            !r.tracked ? "not tracked" : $"{(curled ? "grip" : "open")} {r.straightness:0.00}";

        void UpdateHold()
        {
            if (_holdHand == null)
            {
                TryPickUp(leftHand, _left, _leftCurled);
                if (_holdHand == null) TryPickUp(rightHand, _right, _rightCurled);
                return;
            }

            var r = _holdHand == leftHand ? _left : _right;
            bool curled = _holdHand == leftHand ? _leftCurled : _rightCurled;
            if (!r.tracked)
            {
                if (!_loggedHandDropout) Debug.Log($"[CupDetector] {HandName(_holdHand)} hand not tracked; keeping hold");
                _loggedHandDropout = true;
                // A long dropout while the camera can see a cup: trust the camera again.
                if (Time.time - _lastHandPoseTime > lostAfter && CupInLastFrame) Release("hand lost, camera sees a cup");
                return;
            }
            _loggedHandDropout = false;

            // Release needs the hand CONTINUOUSLY open (raw straightness, not the latched curl state):
            // on device one noisy reading above the open threshold latched "open" and released
            // mugs that were still held (holds of 0.3-4 s, "opened" at 0.76).
            if (r.straightness <= grip.openAbove) _openSince = -1f;
            else if (_openSince < 0f) _openSince = Time.time;
            else if (Time.time - _openSince > releaseAfterOpen) { Release($"hand opened ({r.straightness:0.00})"); return; }

            // Follow the hand from the pose fused at pick-up, pivoting about the handle.
            var candidate = HeldPose(r.wrist);
            if (Time.deltaTime > 0f) _wristSpeed = Vector3.Distance(r.wrist.position, _lastWristPos) / Time.deltaTime;
            _lastWristPos = r.wrist.position;

            // Glitch guard: a jump this big in one frame is bad tracking, unless it persists.
            if (Vector3.Distance(candidate.position, _cupPose.position) > glitchJump)
            {
                if (_glitchSince < 0f) _glitchSince = Time.time;
                if (Time.time - _glitchSince < glitchHold) return;   // keep the last pose
                Debug.Log($"[CupDetector] Accepted a {Vector3.Distance(candidate.position, _cupPose.position) * 100f:0} cm jump after {glitchHold:0.00} s");
            }
            else if (_glitchSince >= 0f)
            {
                GlitchesIgnored++;
                Debug.Log($"[CupDetector] Ignored a hand-tracking glitch ({(Time.time - _glitchSince) * 1000f:0} ms), total {GlitchesIgnored}");
            }
            _glitchSince = -1f;

            _lastHandPoseTime = Time.time;
            _cupPose = candidate;
            HeldHole = new Pose(_cupPose.position + _cupPose.rotation * (Vector3.up * (mugHeight * 0.5f)), _cupPose.rotation);
        }

        static Pose Compose(Pose parent, Pose local) =>
            new Pose(parent.position + parent.rotation * local.position, parent.rotation * local.rotation);

        static Pose Relative(Pose parent, Pose world)
        {
            var inv = Quaternion.Inverse(parent.rotation);
            return new Pose(inv * (world.position - parent.position), inv * world.rotation);
        }

        /// <summary>
        /// The held mug's pose from the current wrist:
        ///  - the handle point (where the fingers go through) is locked to the hand, and the mug
        ///    rotates about it, so a rotation error barely moves the mug (the wrist is ~10 cm away);
        ///  - the hand's rotation since pick-up is split into turning about the vertical (followed
        ///    fully) and tilt (damped: a hooked mug swings on the fingers and stays near upright).
        /// </summary>
        Pose HeldPose(Pose wrist)
        {
            var handle = wrist.position + wrist.rotation * _handleInWrist;
            var delta = wrist.rotation * Quaternion.Inverse(_wristRotAtPickUp);
            var rot = DampTilt(delta, out float handTilt, out float mugTilt) * _mugRotAtPickUp;
            HandTilt = handTilt;
            MugTilt = mugTilt;
            return new Pose(handle - rot * grip.holeInMug, rot);
        }

        /// <summary>Swing-twist split about world up; the swing (tilt) is damped per <see cref="TiltMode"/>.</summary>
        Quaternion DampTilt(Quaternion q, out float handTilt, out float mugTilt)
        {
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            var p = Vector3.Project(new Vector3(q.x, q.y, q.z), Vector3.up);
            var twist = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            twist = m > 1e-6f ? new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m) : Quaternion.identity;
            var swing = q * Quaternion.Inverse(twist);
            swing.ToAngleAxis(out float angle, out var axis);
            if (angle > 180f) angle -= 360f;
            handTilt = Mathf.Abs(angle);

            float applied;
            switch (TiltMode)
            {
                case HeldTilt.Rigid: applied = handTilt; break;
                case HeldTilt.Upright: applied = 0f; break;
                default: applied = handTilt * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tiltDeadZone, tiltFullAt, handTilt)); break;
            }
            mugTilt = applied;
            if (handTilt < 1e-3f || float.IsNaN(axis.x)) return twist;
            return Quaternion.AngleAxis(Mathf.Sign(angle) * applied, axis) * twist;
        }

        /// <summary>
        /// The mug's pose at pick-up, from both sources. It's still upright on the table, so:
        /// sideways and height from the camera ray through the detected box (accurate), distance
        /// along that ray from the finger model (agreed with the camera to ~3 cm on device, and
        /// steadier at close range), handle pointing at the fingers.
        /// </summary>
        Pose FusedPickUpPose(MugGrip.Result r)
        {
            var model = r.MugCentre(mugHeight);
            var ray = cameraAccess.ViewportPointToRay(_pickBox.center, _pickPose);
            var dir = ray.direction.normalized;
            float along = Mathf.Max(0.1f, Vector3.Dot(model - ray.origin, dir));
            var centre = ray.origin + dir * along;

            // Handle (-X) points from the body centre to the fingers, horizontally.
            var toFingers = Vector3.ProjectOnPlane(r.hole.position - centre, Vector3.up);
            var x = toFingers.sqrMagnitude > 1e-6f ? -toFingers.normalized : Vector3.ProjectOnPlane(r.mugBase.rotation * Vector3.right, Vector3.up).normalized;
            var rot = Quaternion.LookRotation(Vector3.Cross(x, Vector3.up), Vector3.up);
            return new Pose(centre - Vector3.up * (mugHeight * 0.5f), rot);
        }

        /// <summary>
        /// While held, a camera sighting of this mug nudges the wrist offset sideways (perpendicular to
        /// the camera ray). Distance along the ray is left to the hand: the camera is weakest there.
        /// </summary>
        void CorrectHeldFromCamera(Pose framePose)
        {
            var r = _holdHand == leftHand ? _left : _right;
            if (!r.tracked || _wristSpeed > correctionMaxWristSpeed) { Rejected = $"held: wrist moving {_wristSpeed:0.00} m/s"; return; }
            var ray = cameraAccess.ViewportPointToRay(CupViewportRect.center, framePose);
            var dir = ray.direction.normalized;
            var held = CupCentre;
            var onRay = ray.origin + dir * Vector3.Dot(held - ray.origin, dir);
            var err = onRay - held;   // perpendicular to the ray
            if (err.magnitude > correctionGate) { Rejected = $"held: sighting {err.magnitude * 100f:0} cm off (another cup?)"; return; }

            var step = err * correctionGain;
            _handleInWrist += Quaternion.Inverse(r.wrist.rotation) * step;
            HeldCorrection += step.magnitude;
            Rejected = $"held: corrected {step.magnitude * 100f:0.0} cm sideways";
        }

        /// <summary>
        /// A gripping hand picks the cup up if the mug it would hold sits inside the last detected cup
        /// box (projected into that frame) at about the camera's distance.
        /// </summary>
        void TryPickUp(Hand hand, MugGrip.Result r, bool curled)
        {
            if (!curled || !r.tracked || Time.time - LastSeenTime > pickUpWindow) return;
            var centre = r.MugCentre(mugHeight);
            var vp = cameraAccess.WorldToViewportPoint(centre, _pickPose);
            var box = _pickBox;
            float mx = box.width * pickUpBoxMargin, my = box.height * pickUpBoxMargin;
            bool inBox = vp.x > box.xMin - mx && vp.x < box.xMax + mx && vp.y > box.yMin - my && vp.y < box.yMax + my;
            float dist = Vector3.Distance(_pickPose.position, centre);
            bool depthOk = Mathf.Abs(dist - _pickDistance) < pickUpDepthTolerance;
            if (!inBox || !depthOk) return;

            var fused = FusedPickUpPose(r);
            var handleWorld = fused.position + fused.rotation * grip.holeInMug;
            _handleInWrist = Quaternion.Inverse(r.wrist.rotation) * (handleWorld - r.wrist.position);
            _wristRotAtPickUp = r.wrist.rotation;
            _mugRotAtPickUp = fused.rotation;
            _holdHand = hand;
            _openSince = -1f;
            _glitchSince = -1f;
            _lastHandPoseTime = Time.time;
            _lastWristPos = r.wrist.position;
            _loggedHandDropout = false;
            _cupPose = fused;
            HeldCorrection = 0f;
            PickUpModelError = Vector3.Distance(fused.position, r.mugBase.position);
            float tilt = Quaternion.Angle(Quaternion.FromToRotation(Vector3.up, r.mugBase.rotation * Vector3.up), Quaternion.identity);
            Debug.Log($"[CupDetector] Picked up by {HandName(hand)} hand (grip {r.straightness:0.00}, " +
                      $"hand mug {dist:0.00} m vs camera {_pickDistance:0.00} m). Finger model was " +
                      $"{PickUpModelError * 100f:0.0} cm and {tilt:0}° tilt from the fused pose");
        }

        void Release(string why)
        {
            Debug.Log($"[CupDetector] Released from {HandName(_holdHand)} hand: {why}");
            _holdHand = null;
            _openSince = -1f;
            LastSeenTime = -99f;   // the old camera position is stale; wait for a fresh detection
        }

        string HandName(Hand h) => h == leftHand ? "left" : "right";

        // ------------------------------------------------------------ output

        void UpdateSource()
        {
            var source = Source.None;
            if (_holdHand != null && Time.time - _lastHandPoseTime < lostAfter)
                source = Source.Hand;   // _cupPose set by UpdateHold (held through short dropouts)
            else if (Time.time - LastSeenTime < visionFresh)
            {
                source = Source.Vision;
                _cupPose = VisionPose(VisionCentre);
            }
            else if (_lastSource == Source.Vision && Time.time - LastSeenTime < lostAfter)
                source = Source.Vision;   // short gap: hold the last pose

            if (source != _lastSource) Debug.Log($"[CupDetector] Source: {_lastSource} -> {source}");
            if (source == Source.Hand && _lastSource != Source.Hand) _snapNext = true;
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
                float speed = TrackingSource == Source.Hand ? heldFollowSpeed : followSpeed;
                float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
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
