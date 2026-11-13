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
    ///   B button                          : forget the placement and scan again.
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
        [SerializeField] float triggerDistance = 1.5f;
        [SerializeField] float triggerAngle = 25f;
        [SerializeField] float dwell = 0.5f;

        [Header("Nudge")]
        [SerializeField] float nudgeStep = 0.001f;

        OVRSpatialAnchor _anchor;
        Material _material;
        Vector2 _nudge;
        float _facingTime;
        bool _played;

        void Awake()
        {
            _material = canvasQuad.material;   // instance, so the shared asset is untouched
            _material.mainTexture = StillTexture();
            canvasRoot.gameObject.SetActive(false);
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
            var layout = estimator.layout;
            var right = pose.rotation * Vector3.right;
            var up = pose.rotation * Vector3.up;

            // A Unity quad faces -Z, so +Z (into the wall) keeps its visible side towards the viewer.
            canvasRoot.SetPositionAndRotation(
                pose.centre + right * layout.paintingOffset.x + up * layout.paintingOffset.y,
                pose.rotation);

            var size = layout.paintingSize - Vector2.one * (2f * layout.inset);
            canvasQuad.transform.localScale = new Vector3(size.x, size.y, 1f);
            ApplyNudge();

            canvasRoot.gameObject.SetActive(true);
            StartCoroutine(CreateAnchor());
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
            if (estimator.IsLocked && !_played) CheckTrigger();
        }

        void HandleInput()
        {
            if (OVRInput.GetDown(OVRInput.Button.Two))
            {
                Rescan();
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
            canvasRoot.gameObject.SetActive(false);
            _played = false;
            _facingTime = 0f;
            if (video != null) { video.Stop(); video.Prepare(); }
            _material.mainTexture = StillTexture();
            estimator.Restart();
        }

        void CheckTrigger()
        {
            if (video == null || head == null) return;
            var toCanvas = canvasRoot.position - head.position;
            bool facing = toCanvas.magnitude < triggerDistance &&
                          Vector3.Angle(head.forward, toCanvas) < triggerAngle;
            _facingTime = facing ? _facingTime + Time.deltaTime : 0f;
            if (_facingTime < dwell || !video.isPrepared) return;

            _played = true;
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
