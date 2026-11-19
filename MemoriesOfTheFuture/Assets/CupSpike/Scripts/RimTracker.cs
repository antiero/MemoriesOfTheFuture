using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// Rim lock: while the mug is held with its opening towards the headset, find the real opening
    /// in the passthrough camera image and correct the held pose, so the well and rune line up with
    /// the real rim.
    ///
    /// Why: the held pose comes from the hand (CupDetector). On device the rim it predicted was
    /// 6-10 cm off the real one (mostly too close to the camera), a bias from the pick-up that
    /// lasts the whole hold. The camera sees the opening clearly when you look in: a bright, round
    /// region (the white interior and lip, bounded by the black band) with the dark rune inside it.
    ///
    /// Per camera frame (on a worker thread, half resolution, only in a window around the
    /// predicted rim):
    ///  1. Threshold at several levels; 4-connected bright regions; fill each region's holes.
    ///  2. Keep regions whose outline is an ellipse (90%+ of boundary pixels within 12% of the
    ///     ellipse fitted from the filled region's moments), whose inner 60% has dark holes (the
    ///     rune), and whose size is 0.4-2.5x the size expected at the predicted distance.
    ///  3. The best one gives the rim centre (ray through the ellipse centre) and distance (angle
    ///     across the major axis vs the mug's 40 mm outer radius).
    /// The difference from the predicted rim nudges the held pose (CupDetector.ApplyHeldCorrection,
    /// gain <see cref="gain"/>), but only once two detections in a row agree, so a single wrong
    /// region can't move the mug. Tested offline on 28 headset frames: the opening was found in
    /// 15, with no wrong regions, and showed a consistent 7-10 cm error.
    /// Logs are prefixed [RimTracker]. The Rim button turns it off and on, for comparison.
    /// </summary>
    public class RimTracker : MonoBehaviour
    {
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] CupDetector detector;
        [SerializeField] bool locking = true;

        [Header("Geometry (m)")]
        [Tooltip("Radius of the bright region's outline: the white lip down to the black band (outer radius).")]
        [SerializeField] float rimRadius = 0.040f;
        [Tooltip("Height of the rim plane above the base.")]
        [SerializeField] float rimHeight = 0.095f;

        [Header("When")]
        [Tooltip("Only while the opening faces the camera within this angle (degrees).")]
        [SerializeField] float maxViewAngle = 75f;
        [SerializeField] float maxDistance = 0.9f;

        [Header("Correction")]
        [SerializeField, Range(0f, 1f)] float gain = 0.5f;
        [Tooltip("A detection further than this from the prediction is ignored (another object).")]
        [SerializeField] float gate = 0.15f;
        [Tooltip("Two detections in a row must agree within this to be used.")]
        [SerializeField] float agree = 0.03f;
        [Tooltip("Largest correction applied at once (CupDetector treats big jumps as glitches).")]
        [SerializeField] float maxStep = 0.05f;

        public bool Locking { get => locking; set { locking = value; Debug.Log($"[RimTracker] Rim lock {(value ? "on" : "off")}"); } }
        public int Detections { get; private set; }
        public int Corrections { get; private set; }
        public float LastError { get; private set; } = float.NaN;
        public Vector3 LastDetectedRim { get; private set; }
        public float LastSeenTime { get; private set; } = -99f;
        public string Status { get; private set; } = "idle";

        public string StatusLine =>
            $"rim lock: {(locking ? Status : "OFF")}  seen {Detections}  fixes {Corrections}" +
            (float.IsNaN(LastError) ? "" : $"  last error {LastError * 100f:0.0} cm");

        // Work handed to the worker thread and back.
        class Job
        {
            public byte[] grey; public int w, h;      // ROI, half resolution, row 0 = bottom
            public int x0, y0;                        // ROI origin in the half-res frame
            public float expR;                        // expected semi-major axis (half-res px)
            public Pose camPose;
            public Vector3 predictedRim;
            public Result result;
        }
        struct Result { public bool found; public float cx, cy, a, b, ax, ay, fit, glyph; public int thr; }

        Task<Job> _task;
        Vector3 _lastResidual;
        bool _hasLast;
        float _lastJobTime = -99f;
        bool _wasHeld;

        const int HalfW = 640, HalfH = 480;
        static readonly int[] Thresholds = { 110, 130, 150, 170, 190 };

        void Update()
        {
            if (_task != null && _task.IsCompleted)
            {
                var job = _task.Status == TaskStatus.RanToCompletion ? _task.Result : null;
                if (_task.IsFaulted) Debug.LogWarning($"[RimTracker] Worker failed: {_task.Exception?.GetBaseException().Message}");
                _task = null;
                if (job != null) Apply(job);
            }

            bool held = detector != null && detector.IsHeld;
            if (held != _wasHeld) { _hasLast = false; _wasHeld = held; if (held) Status = "held"; }
            if (!locking || !held) { if (!held) Status = "not held"; return; }
            if (_task != null || cameraAccess == null || !cameraAccess.IsPlaying || !cameraAccess.IsUpdatedThisFrame) return;
            if (Time.time - _lastJobTime < 0.05f) return;
            Schedule();
        }

        void Schedule()
        {
            var cam = cameraAccess.GetCameraPose();
            var pose = detector.CupPose;
            var up = pose.rotation * Vector3.up;
            var rim = pose.position + up * rimHeight;
            var toCam = cam.position - rim;
            float dist = toCam.magnitude;
            if (dist > maxDistance) { Status = "too far"; return; }
            if (Vector3.Angle(up, toCam) > maxViewAngle) { Status = "opening not facing camera"; return; }
            float z = Vector3.Dot(rim - cam.position, cam.rotation * Vector3.forward);
            if (z < 0.05f) { Status = "rim behind camera"; return; }

            var res = cameraAccess.CurrentResolution;
            var vp = cameraAccess.WorldToViewportPoint(rim, cam);
            // Expected semi-major axis in half-res pixels, from the angle the rim radius subtends.
            var side = cameraAccess.WorldToViewportPoint(rim + (cam.rotation * Vector3.right) * rimRadius, cam);
            float expR = Mathf.Abs(side.x - vp.x) * HalfW;
            if (expR < 6f) { Status = "rim too small in view"; return; }

            int rr = Mathf.CeilToInt(Mathf.Max(3.5f * expR, 100f));
            int pcx = Mathf.RoundToInt(vp.x * HalfW), pcy = Mathf.RoundToInt(vp.y * HalfH);
            int x0 = Mathf.Clamp(pcx - rr, 0, HalfW), x1 = Mathf.Clamp(pcx + rr, 0, HalfW);
            int y0 = Mathf.Clamp(pcy - rr, 0, HalfH), y1 = Mathf.Clamp(pcy + rr, 0, HalfH);
            int w = x1 - x0, h = y1 - y0;
            if (w < 12 || h < 12) { Status = "rim out of camera view"; return; }

            var colors = cameraAccess.GetColors();
            if (!colors.IsCreated || colors.Length != res.x * res.y) return;
            int sx = res.x / HalfW, sy = res.y / HalfH;   // 2 at 1280x960
            var grey = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int srcRow = (y0 + y) * sy * res.x;
                for (int x = 0; x < w; x++)
                {
                    var c = colors[srcRow + (x0 + x) * sx];
                    grey[y * w + x] = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8);
                }
            }

            var job = new Job { grey = grey, w = w, h = h, x0 = x0, y0 = y0, expR = expR, camPose = cam, predictedRim = rim };
            _lastJobTime = Time.time;
            _task = Task.Run(() => { job.result = Detect(job.grey, job.w, job.h, job.expR); return job; });
        }

        void Apply(Job job)
        {
            var r = job.result;
            if (!r.found) { Status = "looking for the opening"; _hasLast = false; return; }

            // Half-res ROI pixel -> viewport (row 0 = bottom, like the camera texture).
            Vector2 Vp(float x, float y) => new Vector2((job.x0 + x + 0.5f) / HalfW, (job.y0 + y + 0.5f) / HalfH);
            var centreRay = cameraAccess.ViewportPointToRay(Vp(r.cx, r.cy), job.camPose);
            var e1 = cameraAccess.ViewportPointToRay(Vp(r.cx - r.ax * r.a, r.cy - r.ay * r.a), job.camPose);
            var e2 = cameraAccess.ViewportPointToRay(Vp(r.cx + r.ax * r.a, r.cy + r.ay * r.a), job.camPose);
            float theta = Vector3.Angle(e1.direction, e2.direction) * Mathf.Deg2Rad;
            if (theta < 1e-4f) return;
            float dist = rimRadius / Mathf.Tan(theta * 0.5f);
            var detected = centreRay.origin + centreRay.direction.normalized * dist;

            var err = detected - job.predictedRim;
            Detections++;
            LastSeenTime = Time.time;
            LastDetectedRim = detected;
            LastError = err.magnitude;
            if (err.magnitude > gate)
            {
                Status = $"ignored: {err.magnitude * 100f:0} cm off (another object?)";
                _hasLast = false;
                return;
            }
            // Use it only if it agrees with the previous detection (after the correction already applied).
            if (!_hasLast || (err - _lastResidual).magnitude > agree)
            {
                Status = $"seen ({err.magnitude * 100f:0.0} cm off), confirming";
                _lastResidual = err;
                _hasLast = true;
                return;
            }

            var step = Vector3.ClampMagnitude(err * gain, maxStep);
            if (detector.ApplyHeldCorrection(step))
            {
                Corrections++;
                _lastResidual = err - step;
                Status = $"LOCKED, corrected {step.magnitude * 100f:0.0} cm";
                if (Corrections == 1 || err.magnitude > 0.03f)
                    Debug.Log($"[RimTracker] Rim {err.magnitude * 100f:0.0} cm off ({dist:0.00} m away, fit {r.fit:0.00}, rune {r.glyph:0.00}, thr {r.thr}); " +
                              $"corrected {step.magnitude * 100f:0.0} cm, {Corrections} fixes");
            }
        }

        // ------------------------------------------------------------ detection (worker thread)

        static Result Detect(byte[] src, int w, int h, float expR)
        {
            var g = Blur3(src, w, h);
            var best = new Result();
            float bestScore = 0f;
            var label = new int[w * h];
            var stack = new Stack<int>();
            var pts = new List<int>();
            float minArea = 0.04f * Mathf.PI * expR * expR, maxArea = 4f * Mathf.PI * expR * expR;

            foreach (int thr in Thresholds)
            {
                Array.Clear(label, 0, label.Length);
                int next = 0;
                for (int i = 0; i < w * h; i++)
                {
                    if (g[i] <= thr || label[i] != 0) continue;
                    next++;
                    pts.Clear();
                    label[i] = next;
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int p = stack.Pop();
                        pts.Add(p);
                        int px = p % w, py = p / w;
                        if (px > 0 && g[p - 1] > thr && label[p - 1] == 0) { label[p - 1] = next; stack.Push(p - 1); }
                        if (px < w - 1 && g[p + 1] > thr && label[p + 1] == 0) { label[p + 1] = next; stack.Push(p + 1); }
                        if (py > 0 && g[p - w] > thr && label[p - w] == 0) { label[p - w] = next; stack.Push(p - w); }
                        if (py < h - 1 && g[p + w] > thr && label[p + w] == 0) { label[p + w] = next; stack.Push(p + w); }
                    }
                    if (pts.Count < minArea || pts.Count > maxArea) continue;
                    if (!Analyse(pts, g, w, expR, out var r)) continue;
                    float score = r.fit * Mathf.Min(r.glyph / 0.3f, 1f);
                    if (score > bestScore) { bestScore = score; best = r; best.thr = thr; best.found = true; }
                }
            }
            return best;
        }

        /// <summary>Fill the region's holes, fit an ellipse from its moments, check the outline and the dark rune inside.</summary>
        static bool Analyse(List<int> pts, byte[] g, int w, float expR, out Result r)
        {
            r = default;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (int p in pts)
            {
                int x = p % w, y = p / w;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            int bw = maxX - minX + 3, bh = maxY - minY + 3;   // 1 px border all round
            if (bw < 8 || bh < 8) return false;

            // 0 = outside (unknown), 1 = region, 2 = reachable from the border (background).
            var m = new byte[bw * bh];
            foreach (int p in pts) m[(p / w - minY + 1) * bw + (p % w - minX + 1)] = 1;
            var st = new Stack<int>();
            m[0] = 2; st.Push(0);
            while (st.Count > 0)
            {
                int q = st.Pop(), qx = q % bw, qy = q / bw;
                if (qx > 0 && m[q - 1] == 0) { m[q - 1] = 2; st.Push(q - 1); }
                if (qx < bw - 1 && m[q + 1] == 0) { m[q + 1] = 2; st.Push(q + 1); }
                if (qy > 0 && m[q - bw] == 0) { m[q - bw] = 2; st.Push(q - bw); }
                if (qy < bh - 1 && m[q + bw] == 0) { m[q + bw] = 2; st.Push(q + bw); }
            }

            // Moments of the filled region (region + holes).
            double n = 0, sx = 0, sy = 0;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                    if (m[y * bw + x] != 2) { n++; sx += x; sy += y; }
            if (n < 20) return false;
            double cx = sx / n, cy = sy / n, sxx = 0, syy = 0, sxy = 0;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                    if (m[y * bw + x] != 2) { double dx = x - cx, dy = y - cy; sxx += dx * dx; syy += dy * dy; sxy += dx * dy; }
            sxx /= n; syy /= n; sxy /= n;
            double tr = (sxx + syy) * 0.5, disc = Math.Sqrt(Math.Max(0, (sxx - syy) * (sxx - syy) * 0.25 + sxy * sxy));
            double l1 = tr + disc, l2 = Math.Max(tr - disc, 1e-6);
            double a = 2 * Math.Sqrt(l1), b = 2 * Math.Sqrt(l2);
            if (b < 4) return false;
            double ax, ay;
            if (Math.Abs(sxy) > 1e-9) { ax = sxy; ay = l1 - sxx; } else if (sxx >= syy) { ax = 1; ay = 0; } else { ax = 0; ay = 1; }
            double al = Math.Sqrt(ax * ax + ay * ay); ax /= al; ay /= al;   // major axis; minor = (-ay, ax)

            double ratio = a / expR;
            if (ratio < 0.4 || ratio > 2.5) return false;

            // Outline: boundary pixels of the filled region should sit on the ellipse.
            int boundary = 0, onEllipse = 0;
            double dark = 0;
            for (int y = 1; y < bh - 1; y++)
                for (int x = 1; x < bw - 1; x++)
                {
                    int i = y * bw + x;
                    if (m[i] == 2) continue;
                    double dx = x - cx, dy = y - cy;
                    double u = dx * ax + dy * ay, v = -dx * ay + dy * ax;
                    double rho = Math.Sqrt((u / a) * (u / a) + (v / b) * (v / b));
                    if (m[i - 1] == 2 || m[i + 1] == 2 || m[i - bw] == 2 || m[i + bw] == 2)
                    {
                        boundary++;
                        if (Math.Abs(rho - 1) < 0.12) onEllipse++;
                    }
                    else if (m[i] == 0 && rho < 0.6)
                    {
                        // A hole in the inner 60%: is it dark (the rune)?
                        int gx = x - 1 + minX, gy = y - 1 + minY;
                        if (g[gy * w + gx] < 90) dark++;
                    }
                }
            if (boundary < 20) return false;
            double fit = onEllipse / (double)boundary;
            double glyph = dark / Math.Max(Math.PI * a * b * 0.36, 1);
            if (fit < 0.8 || glyph < 0.1) return false;

            r = new Result
            {
                cx = (float)(cx - 1 + minX), cy = (float)(cy - 1 + minY),
                a = (float)a, b = (float)b, ax = (float)ax, ay = (float)ay,
                fit = (float)fit, glyph = (float)glyph,
            };
            return true;
        }

        static byte[] Blur3(byte[] s, int w, int h)
        {
            var o = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, cnt = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            sum += s[yy * w + xx]; cnt++;
                        }
                    }
                    o[y * w + x] = (byte)(sum / cnt);
                }
            return o;
        }
    }
}
