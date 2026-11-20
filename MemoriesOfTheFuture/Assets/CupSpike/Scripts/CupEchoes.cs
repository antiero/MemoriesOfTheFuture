using Meta.XR;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// Echoes from the held cup (CupSpike/CupEchoes): the real room around the mug ripples like heat
    /// haze, and ghost copies of the mug swell out of it and fade. A disc centred on the mug, facing the
    /// viewer, shows the passthrough camera's image reprojected onto it, warped and drained to grey, and
    /// fades out at its edge into the real passthrough. It clears over the mug itself, so the real mug
    /// stays crisp. <see cref="Intensity"/> (0..1) is driven by <see cref="DreamState"/> while the cup is held.
    ///
    /// On the headset the image comes from <see cref="PassthroughCameraAccess"/>; in the desktop test
    /// build (<see cref="UseScreen"/>) from a small copy of the view, rendered by a helper camera
    /// without the disc. Logs are prefixed [CupEchoes].
    /// </summary>
    public class CupEchoes : MonoBehaviour
    {
        [SerializeField] CupDetector detector;
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [Tooltip("CupSpike/CupEchoes.")]
        [SerializeField] Material material;
        [Tooltip("Radius of the disc around the mug's centre (m). The echoes fade out before its edge.")]
        [SerializeField] float radius = 0.3f;
        [Range(0f, 1f)] [SerializeField] float maxIntensity = 1f;
        [Tooltip("Debug: show the echoes at full strength, held or not.")]
        [SerializeField] bool forceOn;

        /// <summary>0..1, set by the dream each frame.</summary>
        public float Intensity { get; set; }
        /// <summary>Desktop test build: echo the camera's own image instead of the passthrough camera.</summary>
        public bool UseScreen { get => _useScreen; set => _useScreen = value; }
        public bool ForceOn { get => forceOn; set => forceOn = value; }

        Transform _disc;
        Renderer _discRenderer;
        Material _mat;
        bool _useScreen, _warned;
        Camera _copyCam;
        RenderTexture _copy;

        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), CentreId = Shader.PropertyToID("_Centre"),
            RadiusId = Shader.PropertyToID("_Radius"), CamUvId = Shader.PropertyToID("_CamUV"),
            TexId = Shader.PropertyToID("_EchoTex"), WorldToCamId = Shader.PropertyToID("_EchoWorldToCam");

        void Reset()
        {
            detector = FindAnyObjectByType<CupDetector>();
            cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
        }

        void Start()
        {
            if (material == null || detector == null) { Debug.LogWarning("[CupEchoes] Missing material or detector; disabled"); enabled = false; return; }
            if (cameraAccess == null) cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "CupEchoes";
            _disc = q.transform;
            _disc.SetParent(transform, false);
            _mat = new Material(material);
            var r = _discRenderer = q.GetComponent<Renderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            q.SetActive(false);
            Debug.Log($"[CupEchoes] Ready: disc r {radius:0.00} m, source {(_useScreen ? "screen" : "passthrough camera")}");
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            float k = Mathf.Clamp01(forceOn ? 1f : Intensity) * maxIntensity;
            bool on = k > 0.002f && cam != null && detector.Tracking && (_useScreen || BindCamera());
            if (on && _useScreen) on = CopyView(cam);
            if (_disc.gameObject.activeSelf != on) _disc.gameObject.SetActive(on);
            if (!on) return;

            var c = detector.CupCentre;
            var toDisc = c - cam.transform.position;
            if (toDisc.sqrMagnitude < 1e-4f) toDisc = cam.transform.forward;
            _disc.SetPositionAndRotation(c, Quaternion.LookRotation(toDisc, Vector3.up));
            _disc.localScale = Vector3.one * (radius * 2f);
            _mat.SetFloat(IntensityId, k);
            _mat.SetVector(CentreId, c);
            _mat.SetFloat(RadiusId, radius);
        }

        /// <summary>Desktop: render the view without the disc into a small texture, and project into it like a camera image.</summary>
        bool CopyView(Camera cam)
        {
            if (_copyCam == null)
            {
                _copyCam = new GameObject("CupEchoesView").AddComponent<Camera>();
                _copyCam.transform.SetParent(transform, false);
                _copyCam.enabled = false;
            }
            int w = Mathf.Max(64, cam.pixelWidth / 2), h = Mathf.Max(64, cam.pixelHeight / 2);
            if (_copy == null || _copy.width != w || _copy.height != h)
            {
                if (_copy != null) _copy.Release();
                _copy = new RenderTexture(w, h, 24) { name = "CupEchoesView" };
            }
            _copyCam.CopyFrom(cam);
            _copyCam.targetTexture = _copy;
            _copyCam.enabled = false;
            _discRenderer.enabled = false;
            _copyCam.Render();
            _discRenderer.enabled = true;

            var t = cam.transform;
            float ty = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _mat.SetTexture(TexId, _copy);
            _mat.SetVector(CamUvId, new Vector4(0.5f / (ty * cam.aspect), 0.5f / ty, 0.5f, 0.5f));
            Shader.SetGlobalMatrix(WorldToCamId, Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse);
            return true;
        }

        void OnDestroy()
        {
            if (_copy != null) _copy.Release();
        }

        /// <summary>Hand the shader the latest camera image and how to project world points into it.</summary>
        bool BindCamera()
        {
            if (cameraAccess == null || !cameraAccess.IsPlaying)
            {
                if (!_warned) { _warned = true; Debug.Log("[CupEchoes] No passthrough camera image yet: echoes wait for it"); }
                return false;
            }
            var tex = cameraAccess.GetTexture();
            if (tex == null) return false;
            var pose = cameraAccess.GetCameraPose();
            // Viewport = (x/z, y/z) * A + B in camera space: read A and B off two known points.
            var b = cameraAccess.WorldToViewportPoint(pose.position + pose.rotation * Vector3.forward, pose);
            var ab = cameraAccess.WorldToViewportPoint(pose.position + pose.rotation * new Vector3(1f, 1f, 1f), pose);
            _mat.SetTexture(TexId, tex);
            _mat.SetVector(CamUvId, new Vector4(ab.x - b.x, ab.y - b.y, b.x, b.y));
            Shader.SetGlobalMatrix(WorldToCamId, Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse);
            return true;
        }
    }
}
