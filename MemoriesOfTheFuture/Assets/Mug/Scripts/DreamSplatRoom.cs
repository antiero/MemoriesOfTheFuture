using Gsplat;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// The scanned room (a Gaussian splat of a meeting room: a big table, office chairs, cups) that
    /// materialises around you in the dream, in front of the dark dome. It follows
    /// <see cref="DreamState.Depth"/>: hidden while awake, it grows in from points and brightens as the
    /// dream deepens, and snaps off with the wake-up.
    ///
    /// Placement: when the dream begins, the scanned table is laid over the real one: its top level
    /// with the surface the mug stands on, its centre <see cref="tableAhead"/> beyond the mug (away
    /// from you), turned square to your view. So you're sitting at the dream table with the real mug
    /// on it. World-locked after that (comfort).
    ///
    /// Cost: the splat is only enabled (and uploaded to the GPU) while dreaming. <see cref="Quality"/>
    /// trades looks for frame rate on the Quest (the Splat developer button cycles it, and SplatBench
    /// measures it):
    ///  - how often the splats are re-sorted (every frame, or every N frames plus whenever the head
    ///    moves 5 cm / turns 5 degrees: GsplatSettings),
    ///  - how much each splat is shrunk at full depth (less overdraw),
    ///  - fixed foveated rendering while the scan is visible,
    ///  - and the cup model (YOLO) is paused while the scan is visible, so it doesn't share the GPU.
    /// Logs are prefixed [DreamSplatRoom].
    /// </summary>
    public class DreamSplatRoom : MonoBehaviour
    {
        /// <summary>The scan (400k splats, SH0), cut down for Quest from the artist's 1.08M-splat original.</summary>
        public const string DefaultSplatPath = "Assets/Mug/Splats/RoomScan_400k.ply";

        [SerializeField] DreamState dream;
        [SerializeField] MugInterior interior;
        [Tooltip("Imported from a .ply by the Gsplat package (wu.yize.gsplat).")]
        [SerializeField] GsplatAsset splat;

        [Header("Placement")]
        [Tooltip("The scanned table's centre is this far beyond the mug, away from the viewer (m).")]
        [SerializeField] float tableAhead = 0.8f;
        [Tooltip("The mug's rim is this far above the table it stands on (m).")]
        [SerializeField] float rimAboveTable = 0.1f;
        [Tooltip("Scan to metres (its table is 0.655 units tall, a real one about 0.74 m).")]
        [SerializeField] float scale = 1.13f;
        [Header("The scan's own table (imported units, from the .ply)")]
        [SerializeField] Vector3 scanTableTop = new Vector3(0.03f, 0.52f, -0.82f);
        [Tooltip("Turns the scan so its table is square to the viewer (its short side faces you).")]
        [SerializeField] float scanYaw = 113.4f;

        [Header("Look")]
        [Tooltip("Depth range over which the room grows in from points.")]
        [SerializeField] Vector2 growIn = new Vector2(0.3f, 0.9f);
        [Tooltip("Depth range over which it brightens to full.")]
        [SerializeField] Vector2 brightenIn = new Vector2(0.35f, 1f);
        [SerializeField] float maxBrightness = 1f;
        [Tooltip("SH bands used at run time. The committed scan only has band 0 (flat colour).")]
        [SerializeField] int shDegree;

        public enum SplatQuality { Full, Balanced, Fast }

        [Header("Performance")]
        [Tooltip("Measured on a Quest 3S with the cup model paused: Full holds 72 fps, so it's the default. " +
                 "Balanced and Fast are there for a heavier scan.")]
        [SerializeField] SplatQuality quality = SplatQuality.Full;
        [Tooltip("Pause the cup model while the scan is visible (frees the GPU; the held mug follows the hand meanwhile).")]
        [SerializeField] bool pauseVision = true;
        [SerializeField] CupDetector detector;

        public SplatQuality Quality
        {
            get => quality;
            set { quality = value; if (_on) ApplyQuality(); Debug.Log($"[DreamSplatRoom] Quality: {Describe(value)}"); }
        }
        public bool Visible => _on;
        public bool PauseVision
        {
            get => pauseVision;
            set { pauseVision = value; if (_on && detector != null) detector.PauseVision = value; }
        }
        /// <summary>Frames per second, averaged over the last second.</summary>
        public float Fps { get; private set; }
        public string StatusLine => $"splat: {quality} ({Describe(quality)}){(_on ? "  ON" : "")}  {Fps:0} fps";

        /// <summary>(sort every N frames, 1 = always; shrink at full depth 0..1; foveation)</summary>
        static (uint sortEvery, float shrink, OVRManager.FoveatedRenderingLevel foveation) Preset(SplatQuality q) => q switch
        {
            SplatQuality.Full => (1u, 0f, OVRManager.FoveatedRenderingLevel.Off),
            SplatQuality.Fast => (12u, 0.5f, OVRManager.FoveatedRenderingLevel.High),
            _ => (6u, 0.25f, OVRManager.FoveatedRenderingLevel.Medium),
        };

        public static string Describe(SplatQuality q)
        {
            var p = Preset(q);
            return $"sort {(p.sortEvery <= 1 ? "every frame" : $"every {p.sortEvery}")}, shrink {p.shrink:0.00}, foveation {p.foveation}";
        }

        bool _on;
        OVRManager.FoveatedRenderingLevel _foveationBefore;
        float _fpsTime;
        int _fpsFrames;

        GsplatRenderer _renderer;
        Transform _anchor;

        void Start()
        {
            if (dream == null || splat == null) { Debug.LogWarning("[DreamSplatRoom] Missing dream or splat asset; disabled"); enabled = false; return; }
            if (detector == null) detector = FindAnyObjectByType<CupDetector>();
            _anchor = new GameObject("DreamSplatRoom").transform;
            var go = new GameObject("Splat");
            go.SetActive(false);
            go.transform.SetParent(_anchor, false);
            _renderer = go.AddComponent<GsplatRenderer>();
            _renderer.GsplatAsset = splat;
            _renderer.SHDegree = shDegree;
            _renderer.GammaToLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            _renderer.AsyncUpload = true;                  // no hitch when the dream starts
            _renderer.RenderBeforeUploadComplete = true;   // it's still points at that depth anyway
            _renderer.Brightness = 0f;
            _renderer.SplatDownscaleFactor = 1f;
            Debug.Log($"[DreamSplatRoom] Ready: {splat.SplatCount} splats");
        }

        void LateUpdate()
        {
            _fpsFrames++;
            _fpsTime += Time.unscaledDeltaTime;
            if (_fpsTime >= 1f) { Fps = _fpsFrames / _fpsTime; _fpsFrames = 0; _fpsTime = 0f; }

            if (_renderer == null) return;
            float d = dream.Depth;
            bool on = d > 0f;
            var go = _renderer.gameObject;
            if (go.activeSelf != on)
            {
                if (on) Place();
                go.SetActive(on);
                SetOn(on);
            }
            if (!on) return;

            _renderer.SplatDownscaleFactor = Mathf.Max(Preset(quality).shrink, 1f - Smooth(growIn.x, growIn.y, d));
            _renderer.Brightness = maxBrightness * Smooth(brightenIn.x, brightenIn.y, d);
        }

        void Place()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var head = cam.transform.position;
            var mug = interior != null ? interior.RimCentre : head + cam.transform.forward * 0.5f + Vector3.down * 0.4f;
            var ahead = Vector3.ProjectOnPlane(mug - head, Vector3.up);
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.forward;
            ahead.Normalize();

            // The anchor is the scanned table's top centre, in the world.
            var top = mug + ahead * tableAhead;
            top.y = mug.y - rimAboveTable;
            _anchor.SetPositionAndRotation(top, Quaternion.LookRotation(ahead, Vector3.up));
            var t = _renderer.transform;
            t.localRotation = Quaternion.Euler(0f, scanYaw, 0f);
            t.localScale = Vector3.one * scale;
            t.localPosition = -(t.localRotation * scanTableTop) * scale;
            Debug.Log($"[DreamSplatRoom] Dream table placed: top {top.y:0.00} m, centre {tableAhead:0.0} m beyond the mug");
        }

        void SetOn(bool on)
        {
            _on = on;
            if (on)
            {
                _foveationBefore = OVRManager.foveatedRenderingLevel;
                ApplyQuality();
            }
            else
            {
                OVRManager.foveatedRenderingLevel = _foveationBefore;
                if (detector != null) detector.PauseVision = false;
            }
        }

        void ApplyQuality()
        {
            var p = Preset(quality);
            _renderer.SortMode = p.sortEvery <= 1 ? GsplatRenderer.GsplatSortMode.Always : GsplatRenderer.GsplatSortMode.SortEveryNFrames;
            _renderer.SortRefreshRate = p.sortEvery;
            OVRManager.foveatedRenderingLevel = p.foveation == OVRManager.FoveatedRenderingLevel.Off ? _foveationBefore : p.foveation;
            if (detector != null) detector.PauseVision = pauseVision;
            Debug.Log($"[DreamSplatRoom] Scan on, quality {quality}: {Describe(quality)}, vision {(pauseVision ? "paused" : "running")}, " +
                      $"foveation now {OVRManager.foveatedRenderingLevel}");
        }

        void OnDestroy()
        {
            if (_on) SetOn(false);
            if (_anchor != null) Destroy(_anchor.gameObject);
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    }
}
