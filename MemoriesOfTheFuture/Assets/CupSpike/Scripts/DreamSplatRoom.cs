using Gsplat;
using UnityEngine;

namespace CupSpike
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
    /// Cost: the splat is only enabled (and uploaded to the GPU) while dreaming.
    /// Logs are prefixed [DreamSplatRoom].
    /// </summary>
    public class DreamSplatRoom : MonoBehaviour
    {
        /// <summary>The scan (400k splats, SH0), cut down for Quest from the artist's 1.08M-splat original.</summary>
        public const string DefaultSplatPath = "Assets/CupSpike/Splats/RoomScan_400k.ply";

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

        GsplatRenderer _renderer;
        Transform _anchor;

        void Start()
        {
            if (dream == null || splat == null) { Debug.LogWarning("[DreamSplatRoom] Missing dream or splat asset; disabled"); enabled = false; return; }
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
            if (_renderer == null) return;
            float d = dream.Depth;
            bool on = d > 0f;
            var go = _renderer.gameObject;
            if (go.activeSelf != on)
            {
                if (on) Place();
                go.SetActive(on);
            }
            if (!on) return;

            _renderer.SplatDownscaleFactor = 1f - Smooth(growIn.x, growIn.y, d);
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

        void OnDestroy() { if (_anchor != null) Destroy(_anchor.gameObject); }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    }
}
