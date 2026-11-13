using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace PictureChanges
{
    /// <summary>
    /// The virtual painting. Waits for BoardPoseEstimator to lock, places and sizes the
    /// quad over the painted area, pins it with a spatial anchor, then plays the
    /// transformation video once when the wearer walks up and faces it.
    ///
    /// Controls (demo fallbacks):
    ///   Hold left grip + left thumbstick : nudge the painting 1 mm per click in the board plane.
    ///   B button / left index pinch held 1 s : forget the placement and scan again.
    /// </summary>
    public class PictureCanvas : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] BoardPoseEstimator estimator;
        [Tooltip("Gets the world pose and the spatial anchor.")]
        [SerializeField] Transform canvasRoot;
        [Tooltip("Unit quad, child of canvasRoot. Scaled to the painted area.")]
        [SerializeField] Renderer canvasQuad;
        [SerializeField] Transform head;
        [SerializeField] VideoPlayer video;

        [Header("Content")]
        [Tooltip("Still image of the painting. If empty, an alignment grid is shown instead.")]
        [SerializeField] Texture stillImage;
        [Tooltip("Show a 10 mm grid with a red border instead of the still, to measure registration error.")]
        [SerializeField] bool showAlignmentGrid;

        [Header("Trigger")]
        [Tooltip("Approach / face / dwell states. The movie plays on its Triggered event.")]
        [SerializeField] ViewerPresence presence;

        [Header("Soundtrack")]
        [Tooltip("Looping audio that fades in when the transformation starts. Lives on the painting (spatial).")]
        [SerializeField] AudioSource soundtrack;
        [SerializeField, Range(0f, 1f)] float soundtrackVolume = 0.8f;
        [SerializeField] float soundtrackFadeIn = 2f;
        [SerializeField] float soundtrackFadeOut = 1.5f;

        [Header("Reveal")]
        [Tooltip("Seconds for the watercolour-bleed reveal after the painting is found.")]
        [SerializeField] float revealDuration = 2.8f;

        [Header("Nudge")]
        [SerializeField] float nudgeStep = 0.001f;

        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        Coroutine _reveal, _soundFade;

        public bool HasPlayed => _played;
        public bool IsPlaying => video != null && video.isPlaying;
        public float TriggerDistance => presence != null ? presence.NearEnter : 1.5f;

        /// <summary>Show the 10 mm alignment grid instead of the painting (for measuring registration).</summary>
        public bool ShowAlignmentGrid
        {
            get => showAlignmentGrid;
            set
            {
                showAlignmentGrid = value;
                if (!IsPlaying) _material.mainTexture = StillTexture();
            }
        }

        OVRSpatialAnchor _anchor;
        Material _material;
        Vector2 _nudge;
        bool _played;

        void Awake()
        {
            _material = canvasQuad.material;   // instance, so the shared asset is untouched
            _material.mainTexture = StillTexture();
            _material.SetFloat(RevealId, 0f);
            canvasRoot.gameObject.SetActive(false);
            if (video != null) video.loopPointReached += _ => Debug.Log("[PictureCanvas] Transformation finished (holding last frame)");
            if (soundtrack != null)
            {
                soundtrack.playOnAwake = false;
                soundtrack.loop = true;
                soundtrack.volume = 0f;
                soundtrack.Stop();
                if (soundtrack.clip != null) soundtrack.clip.LoadAudioData();   // ready before the trigger
            }
        }

        void OnEnable() => estimator.Locked += OnLocked;
        void OnDisable() => estimator.Locked -= OnLocked;

        void Start()
        {
            if (video != null && (video.clip != null || !string.IsNullOrEmpty(video.url)))
                video.Prepare();   // decode early so the trigger does not hitch
        }

        void OnLocked(BoardPoseEstimator.BoardPose pose)
        {
            // The root sits at the board origin; the painting mesh is built in board coordinates,
            // so its four corners land exactly on the measured painted area (not a rectangle).
            // +Z points into the wall, so the mesh's visible side faces the viewer.
            canvasRoot.SetPositionAndRotation(pose.centre, pose.rotation);
            // The soundtrack sits on its own object (so hiding the painting doesn't cut it off mid-fade).
            if (soundtrack != null) soundtrack.transform.position = pose.centre;
            canvasQuad.GetComponent<MeshFilter>().sharedMesh = BuildPaintingMesh(estimator.layout.PaintingCorners);
            canvasQuad.transform.localScale = Vector3.one;
            canvasQuad.transform.localRotation = Quaternion.identity;
            ApplyNudge();

            canvasRoot.gameObject.SetActive(true);
            StartCoroutine(CreateAnchor());
            if (_reveal != null) StopCoroutine(_reveal);
            _reveal = StartCoroutine(Reveal());
        }

        IEnumerator Reveal()
        {
            // Ease-in-out so the wash starts slowly, spreads, then settles.
            for (float t = 0f; t < revealDuration; t += Time.deltaTime)
            {
                float x = t / revealDuration;
                _material.SetFloat(RevealId, x * x * (3f - 2f * x));
                yield return null;
            }
            _material.SetFloat(RevealId, 1f);
            _reveal = null;
        }

        /// <summary>Back to the still, ready to transform again (the board stays locked).</summary>
        public void Replay()
        {
            if (video != null) { video.Stop(); video.Prepare(); }
            _played = false;
            _material.mainTexture = StillTexture();
            FadeSoundtrack(false);
            presence?.Rearm();
            Debug.Log("[PictureCanvas] Replay armed");
        }

        /// <summary>
        /// Grid mesh over a four-corner (non-rectangular) area with bilinear positions, so the image
        /// stretches smoothly with no fold along a diagonal. Corners TL, TR, BR, BL in board metres.
        /// </summary>
        static Mesh BuildPaintingMesh(Vector2[] c, int divisions = 8)
        {
            int n = divisions + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)          // j = 0 at the top
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)divisions, v = j / (float)divisions;
                var top = Vector2.Lerp(c[0], c[1], u);
                var bottom = Vector2.Lerp(c[3], c[2], u);
                var p = Vector2.Lerp(top, bottom, v);
                verts[j * n + i] = new Vector3(p.x, p.y, 0f);
                uvs[j * n + i] = new Vector2(u, 1f - v);
            }
            var tris = new int[divisions * divisions * 6];
            int t = 0;
            for (int j = 0; j < divisions; j++)
            for (int i = 0; i < divisions; i++)
            {
                int a = j * n + i, b = a + 1, d = a + n, e = d + 1;
                // Clockwise as seen from the viewer (on the -Z side): top-left, top-right, bottom-right.
                tris[t++] = a; tris[t++] = b; tris[t++] = e;
                tris[t++] = a; tris[t++] = e; tris[t++] = d;
            }
            var mesh = new Mesh { name = "PaintingMesh", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        IEnumerator CreateAnchor()
        {
            if (_anchor != null) Destroy(_anchor);
            yield return null;   // let any destroyed anchor component go first
            _anchor = canvasRoot.gameObject.AddComponent<OVRSpatialAnchor>();
            yield return new WaitUntil(() => _anchor == null || _anchor.Created);
            if (_anchor != null) Debug.Log($"[PictureCanvas] Spatial anchor created {_anchor.Uuid}");
        }

        void Update()
        {
            HandleInput();
            if (estimator.IsLocked && !_played && _reveal == null) CheckTrigger();
        }

        void HandleInput()
        {
            var input = PictureChangesInput.Instance;
            if (input != null && input.RescanRequested)
            {
                Rescan();
                input.Say("Rescanning");
                return;
            }
            if (!estimator.IsLocked || !OVRInput.Get(OVRInput.Button.PrimaryHandTrigger)) return;

            var step = Vector2.zero;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickLeft)) step.x -= 1;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickRight)) step.x += 1;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickUp)) step.y += 1;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickDown)) step.y -= 1;
            if (step == Vector2.zero) return;

            _nudge += step * nudgeStep;
            ApplyNudge();
            Debug.Log($"[PictureCanvas] nudge now {_nudge.x * 1000f:F0}, {_nudge.y * 1000f:F0} mm");
        }

        // The nudge moves the quad inside the anchored root, so the anchor never has to be rebuilt.
        void ApplyNudge() => canvasQuad.transform.localPosition = new Vector3(_nudge.x, _nudge.y, 0f);

        public void Rescan()
        {
            if (_anchor != null) { Destroy(_anchor); _anchor = null; }
            if (_reveal != null) { StopCoroutine(_reveal); _reveal = null; }
            _material.SetFloat(RevealId, 0f);
            canvasRoot.gameObject.SetActive(false);
            _played = false;
            if (video != null) { video.Stop(); video.Prepare(); }
            _material.mainTexture = StillTexture();
            FadeSoundtrack(false);
            presence?.ResetState();
            estimator.Restart();
        }

        void CheckTrigger()
        {
            if (video == null || presence == null || !presence.HasTriggered || !video.isPrepared) return;

            _played = true;
            Debug.Log("[PictureCanvas] Transformation started");
            video.isLooping = false;
            video.sendFrameReadyEvents = true;
            video.frameReady += OnFirstFrame;
            video.Play();
        }

        void OnFirstFrame(VideoPlayer source, long frame)
        {
            // Swap only once a real frame exists, so the still never flashes to black.
            // The player holds its last frame when it finishes (Loop off).
            _material.mainTexture = source.texture;
            source.sendFrameReadyEvents = false;
            source.frameReady -= OnFirstFrame;
            FadeSoundtrack(true);
        }

        /// <summary>Fade the looping soundtrack in (starting it) or out (stopping it when silent).</summary>
        void FadeSoundtrack(bool fadeIn)
        {
            if (soundtrack == null || soundtrack.clip == null) return;
            if (!fadeIn && !soundtrack.isPlaying) return;
            if (_soundFade != null) StopCoroutine(_soundFade);
            _soundFade = StartCoroutine(FadeRoutine(fadeIn));
        }

        IEnumerator FadeRoutine(bool fadeIn)
        {
            if (fadeIn && !soundtrack.isPlaying)
            {
                soundtrack.volume = 0f;
                soundtrack.time = 0f;
                soundtrack.Play();
                Debug.Log("[PictureCanvas] Soundtrack fading in");
            }
            float from = soundtrack.volume, to = fadeIn ? soundtrackVolume : 0f;
            float duration = Mathf.Max(0.01f, fadeIn ? soundtrackFadeIn : soundtrackFadeOut);
            // Scale the time by how far there is to go, so a fade-out mid-fade-in doesn't take the full duration.
            duration *= Mathf.Abs(to - from) / Mathf.Max(0.01f, soundtrackVolume);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float x = t / duration;
                soundtrack.volume = Mathf.Lerp(from, to, x * x * (3f - 2f * x));
                yield return null;
            }
            soundtrack.volume = to;
            if (!fadeIn) { soundtrack.Stop(); Debug.Log("[PictureCanvas] Soundtrack faded out"); }
            _soundFade = null;
        }

        Texture StillTexture() => showAlignmentGrid || stillImage == null ? MakeAlignmentGrid() : stillImage;

        /// <summary>White grid with a red border, 10 mm cells on an A4 canvas: shows registration error at a glance.</summary>
        static Texture2D MakeAlignmentGrid()
        {
            const int w = 420, h = 594;   // 2 px per mm at A4
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "AlignmentGrid", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool border = x < 4 || y < 4 || x >= w - 4 || y >= h - 4;
                bool line = x % 20 == 0 || y % 20 == 0;
                bool centre = Mathf.Abs(x - w / 2) < 2 || Mathf.Abs(y - h / 2) < 2;
                px[y * w + x] = border ? new Color32(255, 30, 30, 255)
                              : centre ? new Color32(30, 120, 255, 255)
                              : line ? new Color32(60, 60, 60, 255)
                              : new Color32(235, 235, 235, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
