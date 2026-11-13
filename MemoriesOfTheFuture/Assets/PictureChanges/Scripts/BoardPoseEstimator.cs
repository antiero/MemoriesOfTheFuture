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
    /// Per frame: camera image -> greyscale -> GlyphDetector (worker thread) -> four rays via
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
        public GlyphDetector.Settings detector = new GlyphDetector.Settings();

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

        public event Action<BoardPose> Locked;

        public bool IsLocked { get; private set; }
        public int SampleCount => _samples.Count;
        public int FramesProcessed { get; private set; }
        public int FramesDetected { get; private set; }
        /// <summary>Most recent single-frame estimate, for live debug gizmos.</summary>
        public BoardPose? LastSample { get; private set; }

        struct Job
        {
            public Task<GlyphDetector.Result> task;
            public Pose cameraPose;
            public int width, height, step;
        }

        readonly List<BoardPose> _samples = new List<BoardPose>();
        Job? _job;

        void Reset() => cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();

        public void Restart()
        {
            _samples.Clear();
            IsLocked = false;
            LastSample = null;
            FramesProcessed = FramesDetected = 0;
        }

        void Update()
        {
            if (IsLocked || cameraAccess == null || !cameraAccess.IsPlaying) return;

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

            var settings = detector;
            bool bottomLeft = !imageRowsTopFirst;
            _job = new Job
            {
                task = Task.Run(() => GlyphDetector.Detect(gray, w, h, settings, bottomLeft)),
                cameraPose = pose,
                width = w, height = h, step = step,
            };
        }

        void Consume(Job job, GlyphDetector.Result result)
        {
            FramesProcessed++;
            if (!result.found) return;
            FramesDetected++;

            // Pixel -> viewport -> world ray -> normalised camera coords.
            var inv = Quaternion.Inverse(job.cameraPose.rotation);
            var norm = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                var p = result.corners[k];
                float vx = (p.x + 0.5f) / job.width;
                float vy = (p.y + 0.5f) / job.height;
                if (imageRowsTopFirst) vy = 1f - vy;
                var ray = cameraAccess.ViewportPointToRay(new Vector2(vx, vy), job.cameraPose);
                var d = inv * ray.direction;
                if (d.z <= 1e-4f) return;
                norm[k] = new Vector2(d.x / d.z, d.y / d.z);
            }

            if (!PlanarPose.Solve(layout.GlyphCentres, norm, out var cam)) return;

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

            float distance = cam.position.magnitude;
            float viewAngle = Vector3.Angle(cam.intoWall, cam.position);
            if (logEachDetection)
                Debug.Log($"[BoardPose] detected at {distance:F2} m, view {viewAngle:F0} deg, centre {centre:F3}");
            if (distance > maxSampleDistance || viewAngle > maxViewAngle) return;

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
            if (inliers.Count < samplesNeeded) return;

            var mean = sumPos / inliers.Count;
            float sq = 0f;
            foreach (var s in inliers) sq += (s.centre - mean).sqrMagnitude;
            float rmsMm = Mathf.Sqrt(sq / inliers.Count) * 1000f;
            if (rmsMm > maxRmsMm) return;

            var pose = new BoardPose
            {
                centre = mean,
                rotation = Quaternion.LookRotation(sumFwd.normalized, sumUp.normalized),
                rmsErrorMm = rmsMm,
                samples = inliers.Count,
            };
            IsLocked = true;
            Debug.Log($"[BoardPose] LOCKED from {inliers.Count}/{_samples.Count} samples, RMS {rmsMm:F1} mm, " +
                      $"detected in {FramesDetected}/{FramesProcessed} frames");
            Locked?.Invoke(pose);
        }
    }
}
