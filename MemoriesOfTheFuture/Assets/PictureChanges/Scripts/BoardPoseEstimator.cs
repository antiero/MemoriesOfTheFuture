using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR;
using PictureChanges.Vision;
using UnityEngine;

namespace PictureChanges
{
    /// <summary>
    /// Finds the board from its four corner glyphs in the passthrough camera feed and
    /// reports a single, averaged world pose once it is confident.
    ///
    /// Per frame: camera image -> greyscale -> SquareMarkerDetector (worker thread) -> rays via
    /// PassthroughCameraAccess.ViewportPointToRay -> PlanarPose -> world-space sample.
    /// Samples are filtered for distance and viewing angle, outliers are rejected around the
    /// median, and the rest are averaged. After locking, the camera is no longer read.
    /// </summary>
    public class BoardPoseEstimator : MonoBehaviour
    {
        public struct BoardPose
        {
            public Vector3 centre;     // centre of the glyph rectangle, world space
            public Quaternion rotation; // +Z into the wall, +Y up the board
            public float rmsErrorMm;   // spread of the averaged samples
            public int samples;
        }

        [Header("Inputs")]
        [SerializeField] PassthroughCameraAccess cameraAccess;
        public BoardLayout layout = new BoardLayout();
        public SquareMarkerDetector.Settings detector = new SquareMarkerDetector.Settings();

        [Header("Image")]
        [Tooltip("Downsample factor before detection. 1 = full camera resolution.")]
        [SerializeField, Range(1, 4)] int downsample = 1;
        [Tooltip("Set if the camera buffer turns out to be top-row-first on device. CHECK ON DEVICE.")]
        [SerializeField] bool imageRowsTopFirst = false;

        [Header("Sampling")]
        [SerializeField] int samplesNeeded = 30;
        [Tooltip("Ignore samples taken further away than this (metres). Accuracy falls with distance.")]
        [SerializeField] float maxSampleDistance = 1.8f;
        [Tooltip("Ignore samples where the board is viewed more obliquely than this (degrees).")]
        [SerializeField] float maxViewAngle = 50f;
        [Tooltip("Samples further than this from the median are outliers (metres).")]
        [SerializeField] float outlierDistance = 0.012f;
        [Tooltip("Lock only when the averaged samples agree to within this RMS (millimetres).")]
        [SerializeField] float maxRmsMm = 4f;

        [Header("Debug")]
        [SerializeField] bool logEachDetection = false;
        [Tooltip("Log a one-line status summary this often (seconds). 0 = off.")]
        [SerializeField] float statusInterval = 2f;
        [Tooltip("Allow manual captures (A, or right-hand index pinch held). Saved to persistentDataPath/captures.")]
        [SerializeField] bool enableFrameCapture = true;
        [Tooltip("While searching, save a frame this often when glyph candidates are visible (seconds). 0 = off.")]
        [SerializeField] float autoCaptureInterval = 2f;
        [SerializeField] int autoCaptureMax = 40;

        public event Action<BoardPose> Locked;

        public bool IsLocked { get; private set; }
        public int SampleCount => _samples.Count;
        public int FramesProcessed { get; private set; }
        public int FramesDetected { get; private set; }
        /// <summary>Most recent single-frame estimate, for live debug gizmos.</summary>
        public BoardPose? LastSample { get; private set; }
        /// <summary>Time.time of LastSample.</summary>
        public float LastSampleTime { get; private set; } = -999f;
        /// <summary>Whether LastSample passed the distance/angle filters and was kept.</summary>
        public bool LastSampleAccepted { get; private set; }
        public BoardPose? LockedPose { get; private set; }
        public int SamplesNeeded => samplesNeeded;
        /// <summary>Markers used in the most recent detection (3 or 4).</summary>
        public int MarkersSeenLast { get; private set; }
        public float MaxSampleDistance => maxSampleDistance;
        public bool ImageRowsTopFirst => imageRowsTopFirst;

        /// <summary>A processed camera frame and what the detector found in it.</summary>
        public class DebugFrame
        {
            public byte[] gray;
            public int width, height;
            public GlyphDetector.Result result;
        }
        public DebugFrame LastFrame { get; private set; }
        /// <summary>Increments every time LastFrame changes.</summary>
        public int FrameVersion { get; private set; }

        /// <summary>One line of live status, also used by the in-headset debug view.</summary>
        public string StatusText =>
            $"cam {(cameraAccess != null && cameraAccess.IsPlaying ? $"{cameraAccess.CurrentResolution.x}x{cameraAccess.CurrentResolution.y}" : "NOT PLAYING")}  " +
            $"frames {FramesProcessed}  found {FramesDetected} ({MarkersSeenLast}/4 markers)  marker candidates {_lastCandidates}\n" +
            $"samples {_samples.Count}/{samplesNeeded}  too far {_rejectFar}  too oblique {_rejectOblique}  " +
            $"last {(_lastDistance < 0 ? "-" : $"{_lastDistance:F2} m, {_lastViewAngle:F0} deg")}\n" +
            $"lock blocker: {_lockBlocker}";

        /// <summary>Plain-language hint for the wearer.</summary>
        public string Hint
        {
            get
            {
                if (cameraAccess == null || !cameraAccess.IsPlaying) return "Camera not running (permission?)";
                if (IsLocked) return "LOCKED. Walk up and face the painting.  B = rescan";
                bool recent = Time.time - LastSampleTime < 0.5f;
                if (!recent)
                    return _lastCandidates < 3
                        ? $"Board not found ({_lastCandidates} marker squares seen). Point at it, 0.6-1.2 m, good light"
                        : $"{_lastCandidates} marker squares but no board shape. Face the board squarely";
                if (!LastSampleAccepted)
                    return _lastDistance > maxSampleDistance
                        ? $"Seen at {_lastDistance:F1} m. Move closer (under {maxSampleDistance:F1} m)"
                        : $"Seen at {_lastViewAngle:F0} deg. Face it more squarely";
                return $"Collecting {_samples.Count}/{samplesNeeded}. Hold still";
            }
        }

        struct Job
        {
            public Task<SquareMarkerDetector.Result> task;
            public Pose cameraPose;
            public int width, height, step;
            public byte[] gray;
        }

        readonly List<BoardPose> _samples = new List<BoardPose>();
        Job? _job;

        // Diagnostics
        float _nextStatus;
        int _rejectFar, _rejectOblique, _lastCandidates;
        float _lastDistance = -1f, _lastViewAngle = -1f;
        string _lockBlocker = "-";
        byte[] _lastGray; int _lastW, _lastH;
        bool _loggedCameraInfo;
        string _captureDir;
        int _captureCount;
        float _nextAutoCapture;
        public int CaptureCount => _captureCount;

        void Reset() => cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();

        public void Restart()
        {
            _samples.Clear();
            IsLocked = false;
            LastSample = null;
            LockedPose = null;
            LastSampleTime = -999f;
            FramesProcessed = FramesDetected = 0;
            _rejectFar = _rejectOblique = 0;
            _lockBlocker = "-";
        }

        void Update()
        {
            LogStatus();
            var input = PictureChangesInput.Instance;
            if (enableFrameCapture && input != null && input.CaptureRequested)
            {
                var path = CaptureFrame("manual");
                input.Say(path != null ? $"Captured frame #{_captureCount}" : "No camera frame yet");
            }
            if (!IsLocked && autoCaptureInterval > 0f && _captureCount < autoCaptureMax &&
                _lastCandidates > 0 && Time.time >= _nextAutoCapture)
            {
                _nextAutoCapture = Time.time + autoCaptureInterval;
                CaptureFrame("auto");
            }
            if (IsLocked || cameraAccess == null || !cameraAccess.IsPlaying) return;
            if (!_loggedCameraInfo)
            {
                _loggedCameraInfo = true;
                var k = cameraAccess.Intrinsics;
                Debug.Log($"[BoardPose] camera playing: {cameraAccess.CurrentResolution.x}x{cameraAccess.CurrentResolution.y}, " +
                          $"focal {k.FocalLength}, principal {k.PrincipalPoint}, sensor {k.SensorResolution}");
            }

            if (_job.HasValue)
            {
                if (!_job.Value.task.IsCompleted) return;
                var job = _job.Value;
                _job = null;
                if (job.task.IsFaulted) Debug.LogException(job.task.Exception);
                else Consume(job, job.task.Result);
            }

            if (!cameraAccess.IsUpdatedThisFrame) return;
            StartJob();
        }

        void StartJob()
        {
            var res = cameraAccess.CurrentResolution;
            var colors = cameraAccess.GetColors();
            if (!colors.IsCreated || colors.Length < res.x * res.y) return;

            // Snapshot the pose that belongs to this image, and copy pixels to greyscale here
            // (the native buffer is reused next frame); the heavy work runs on a worker thread.
            var pose = cameraAccess.GetCameraPose();
            int step = Mathf.Max(1, downsample);
            int w = res.x / step, h = res.y / step;
            var gray = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (y * step) * res.x;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = colors[src + x * step];
                    gray[dst + x] = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8);
                }
            }

            _lastGray = gray; _lastW = w; _lastH = h;
            var settings = detector;
            bool bottomLeft = !imageRowsTopFirst;
            _job = new Job
            {
                task = Task.Run(() => SquareMarkerDetector.Detect(gray, w, h, settings, bottomLeft)),
                cameraPose = pose,
                width = w, height = h, step = step,
                gray = gray,
            };
        }

        void Consume(Job job, SquareMarkerDetector.Result result)
        {
            FramesProcessed++;
            _lastCandidates = result.candidates.Count;
            LastFrame = new DebugFrame { gray = job.gray, width = job.width, height = job.height, result = result };
            FrameVersion++;
            if (!result.found) return;
            FramesDetected++;

            // Use the inner corners of every marker actually seen (16 points, or 12 with one hidden).
            // A predicted (unseen) marker is never used for the pose.
            if (result.markerCorners == null) return;
            var allBoard = layout.MarkerCorners;
            var imageList = new List<Vector2>(16);
            var boardList = new List<Vector2>(16);
            for (int m = 0; m < 4; m++)
            {
                if (result.markerCorners[m] == null) continue;
                for (int k = 0; k < 4; k++)
                {
                    imageList.Add(result.markerCorners[m][k]);
                    boardList.Add(allBoard[m * 4 + k]);
                }
            }
            if (imageList.Count < 12) return;
            var imagePts = imageList.ToArray();
            var boardPts = boardList.ToArray();
            MarkersSeenLast = imageList.Count / 4;

            // Pixel -> viewport -> world ray -> normalised camera coords.
            var inv = Quaternion.Inverse(job.cameraPose.rotation);
            var norm = new Vector2[imagePts.Length];
            for (int k = 0; k < imagePts.Length; k++)
            {
                var p = imagePts[k];
                float vx = (p.x + 0.5f) / job.width;
                float vy = (p.y + 0.5f) / job.height;
                if (imageRowsTopFirst) vy = 1f - vy;
                var ray = cameraAccess.ViewportPointToRay(new Vector2(vx, vy), job.cameraPose);
                var d = inv * ray.direction;
                if (d.z <= 1e-4f) return;
                norm[k] = new Vector2(d.x / d.z, d.y / d.z);
            }

            if (!PlanarPose.Solve(boardPts, norm, out var cam)) return;

            var rot = job.cameraPose.rotation;
            var centre = job.cameraPose.position + rot * cam.position;
            var up = rot * cam.up;
            var into = rot * cam.intoWall;
            var sample = new BoardPose
            {
                centre = centre,
                rotation = Quaternion.LookRotation(into, up),
                samples = 1,
            };
            LastSample = sample;
            LastSampleTime = Time.time;
            LastSampleAccepted = false;

            float distance = cam.position.magnitude;
            float viewAngle = Vector3.Angle(cam.intoWall, cam.position);
            if (logEachDetection)
                Debug.Log($"[BoardPose] detected at {distance:F2} m, view {viewAngle:F0} deg, centre {centre:F3}");
            _lastDistance = distance; _lastViewAngle = viewAngle;
            if (distance > maxSampleDistance) { _rejectFar++; return; }
            if (viewAngle > maxViewAngle) { _rejectOblique++; return; }
            LastSampleAccepted = true;

            _samples.Add(sample);
            if (_samples.Count > samplesNeeded * 2) _samples.RemoveAt(0);
            if (_samples.Count >= samplesNeeded) TryLock();
        }

        void TryLock()
        {
            // Median position (per axis) as a robust centre, then keep samples near it.
            var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
            foreach (var s in _samples) { xs.Add(s.centre.x); ys.Add(s.centre.y); zs.Add(s.centre.z); }
            xs.Sort(); ys.Sort(); zs.Sort();
            var median = new Vector3(xs[xs.Count / 2], ys[ys.Count / 2], zs[zs.Count / 2]);

            Vector3 sumPos = Vector3.zero, sumFwd = Vector3.zero, sumUp = Vector3.zero;
            var inliers = new List<BoardPose>();
            foreach (var s in _samples)
            {
                if ((s.centre - median).magnitude > outlierDistance) continue;
                inliers.Add(s);
                sumPos += s.centre;
                sumFwd += s.rotation * Vector3.forward;
                sumUp += s.rotation * Vector3.up;
            }
            if (inliers.Count < samplesNeeded) { _lockBlocker = $"only {inliers.Count}/{_samples.Count} samples within {outlierDistance * 1000f:F0} mm of median"; return; }

            var mean = sumPos / inliers.Count;
            float sq = 0f;
            foreach (var s in inliers) sq += (s.centre - mean).sqrMagnitude;
            float rmsMm = Mathf.Sqrt(sq / inliers.Count) * 1000f;
            if (rmsMm > maxRmsMm) { _lockBlocker = $"RMS {rmsMm:F1} mm > {maxRmsMm} mm"; return; }

            var pose = new BoardPose
            {
                centre = mean,
                rotation = Quaternion.LookRotation(sumFwd.normalized, sumUp.normalized),
                rmsErrorMm = rmsMm,
                samples = inliers.Count,
            };
            IsLocked = true;
            LockedPose = pose;
            Debug.Log($"[BoardPose] LOCKED from {inliers.Count}/{_samples.Count} samples, RMS {rmsMm:F1} mm, " +
                      $"detected in {FramesDetected}/{FramesProcessed} frames");
            Locked?.Invoke(pose);
        }

        void LogStatus()
        {
            if (statusInterval <= 0f || Time.unscaledTime < _nextStatus) return;
            _nextStatus = Time.unscaledTime + statusInterval;
            if (cameraAccess == null) { Debug.LogWarning("[BoardPose] STATUS no PassthroughCameraAccess assigned"); return; }
            Debug.Log($"[BoardPose] STATUS locked={IsLocked} | {StatusText.Replace("\n", " | ")} | hint: {Hint}");
        }

        /// <summary>Saves the detector's current input frame as a greyscale PNG. Returns the path, or null.</summary>
        string CaptureFrame(string reason)
        {
            if (_lastGray == null || cameraAccess == null) { Debug.LogWarning("[BoardPose] CAPTURE no frame yet"); return null; }
            if (_captureDir == null)
            {
                _captureDir = System.IO.Path.Combine(Application.persistentDataPath, "captures", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                System.IO.Directory.CreateDirectory(_captureDir);
                var k = cameraAccess.Intrinsics;
                var res = cameraAccess.CurrentResolution;
                System.IO.File.WriteAllText(System.IO.Path.Combine(_captureDir, "camera.json"),
                    "{" + $"\"width\":{res.x},\"height\":{res.y},\"downsample\":{Mathf.Max(1, downsample)}," +
                    $"\"fx\":{k.FocalLength.x},\"fy\":{k.FocalLength.y},\"cx\":{k.PrincipalPoint.x},\"cy\":{k.PrincipalPoint.y}," +
                    $"\"sensorWidth\":{k.SensorResolution.x},\"sensorHeight\":{k.SensorResolution.y},\"rowsTopFirst\":{(imageRowsTopFirst ? "true" : "false")}" + "}");
            }
            var tex = new Texture2D(_lastW, _lastH, TextureFormat.R8, false);
            tex.LoadRawTextureData(_lastGray);
            tex.Apply();
            _captureCount++;
            var path = System.IO.Path.Combine(_captureDir, $"{_captureCount:D3}_{reason}_{DateTime.Now:HHmmss}.png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log($"[BoardPose] CAPTURE {reason} saved {path} ({_lastW}x{_lastH}, candidates={_lastCandidates})");
            return path;
        }
    }
}
