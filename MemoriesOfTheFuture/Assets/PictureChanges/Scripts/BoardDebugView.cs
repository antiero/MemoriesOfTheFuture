using TMPro;
using UnityEngine;

namespace PictureChanges
{
    /// <summary>
    /// In-headset debugging for board detection.
    ///
    ///  - Detector panel (floats below your line of sight): the greyscale frame the detector
    ///    sees, yellow boxes on glyph candidates, and the chosen quad (TL red, TR green,
    ///    BR blue, BL magenta). If the picture is upside down here, flip
    ///    BoardPoseEstimator "Image Rows Top First".
    ///  - Status text with a plain-language hint.
    ///  - World overlay on the detected board: glyph rectangle, painting outline, a grid on
    ///    the detected plane and a normal arrow. Orange = seen but rejected (too far or
    ///    oblique), cyan = sample accepted, green = locked.
    ///
    /// Toggle: Y, or right middle-finger pinch held. Capture: A, or right index pinch held.
    /// Rescan: B, or left index pinch held. See PictureChangesInput.
    /// </summary>
    public class BoardDebugView : MonoBehaviour
    {
        [SerializeField] BoardPoseEstimator estimator;
        [SerializeField] Transform head;
        [Tooltip("URP Unlit material asset, referenced so the shader is kept in builds.")]
        [SerializeField] Material unlitMaterial;
        [SerializeField] bool visible = false;

        [Header("Panel")]
        [SerializeField] Vector3 panelOffset = new Vector3(0f, -0.22f, 0.65f);
        [SerializeField] float panelWidth = 0.34f;
        [SerializeField, Range(1, 4)] int panelDownsample = 2;
        [SerializeField] float followSpeed = 4f;

        [Header("World overlay")]
        [SerializeField] float lineWidth = 0.003f;
        [SerializeField] float staleAfter = 0.5f;

        static readonly Color Rejected = new Color(1f, 0.55f, 0f);
        static readonly Color Accepted = new Color(0f, 0.9f, 1f);
        static readonly Color LockedColour = new Color(0.2f, 1f, 0.3f);
        static readonly Color32[] CornerColours =
        {
            new Color32(255, 40, 40, 255), new Color32(40, 255, 40, 255),
            new Color32(60, 140, 255, 255), new Color32(255, 40, 255, 255),
        };

        Transform _panelRoot;
        Renderer _panel;
        Texture2D _panelTex;
        Color32[] _panelPixels;
        TextMeshPro _text;
        int _seenVersion = -1;

        Transform _overlayRoot;
        LineRenderer _glyphRect, _paintingRect, _normal;
        MeshFilter _grid;
        Material _gridMat;

        void Start()
        {
            BuildPanel();
            BuildOverlay();
            ApplyVisibility();
        }

        void Update()
        {
            var input = PictureChangesInput.Instance;
            if (input != null && input.ToggleDebugRequested) { visible = !visible; ApplyVisibility(); }
            if (!visible || estimator == null) return;

            FollowHead();
            string extra = "";
            if (input != null && input.HoldLabel != null)
                extra = $"\n<color=#7FDBFF>{input.HoldLabel}: {new string('|', Mathf.RoundToInt(input.HoldProgress * 20)).PadRight(20, '.')}</color>";
            else if (input != null && Time.time - input.FeedbackTime < 1.5f)
                extra = $"\n<color=#2ECC40>{input.Feedback}</color>";
            _text.text = $"<b>{estimator.Hint}</b>\n<size=70%>{estimator.StatusText}  captures {estimator.CaptureCount}</size>{extra}";
            if (estimator.FrameVersion != _seenVersion) DrawPanel();
            DrawOverlay();
        }

        /// <summary>Debug panel + world overlay. Off by default; toggled from the hand menu.</summary>
        public bool Visible
        {
            get => visible;
            set { visible = value; ApplyVisibility(); }
        }

        public void Toggle() => Visible = !Visible;

        void ApplyVisibility()
        {
            if (_panelRoot != null) _panelRoot.gameObject.SetActive(visible);
            if (_overlayRoot != null) _overlayRoot.gameObject.SetActive(visible);
        }

        // ---------- Panel ----------

        void BuildPanel()
        {
            _panelRoot = new GameObject("DebugPanel").transform;
            _panelRoot.SetParent(transform, false);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "DetectorView";
            quad.transform.SetParent(_panelRoot, false);
            _panel = quad.GetComponent<Renderer>();
            _panel.material = new Material(unlitMaterial);

            var textGo = new GameObject("Status");
            textGo.transform.SetParent(_panelRoot, false);
            _text = textGo.AddComponent<TextMeshPro>();
            _text.fontSize = UI.TextSize.ForCapHeight(0.0075f);
            _text.alignment = TextAlignmentOptions.BottomLeft;
            _text.rectTransform.sizeDelta = new Vector2(panelWidth * 1.6f, 0.12f);
            _text.rectTransform.pivot = new Vector2(0f, 0f);
            _text.color = Color.white;
            _text.outlineWidth = 0.2f;
            _text.outlineColor = Color.black;

            if (head != null) _panelRoot.SetPositionAndRotation(TargetPosition(), TargetRotation());
        }

        Vector3 TargetPosition()
        {
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            return head.position + yaw * panelOffset;
        }

        Quaternion TargetRotation()
        {
            var toHead = head.position - TargetPosition();
            // Quad's visible side faces -Z, so +Z points away from the viewer.
            return Quaternion.LookRotation(-toHead, Vector3.up);
        }

        void FollowHead()
        {
            if (head == null) return;
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            _panelRoot.position = Vector3.Lerp(_panelRoot.position, TargetPosition(), t);
            _panelRoot.rotation = Quaternion.Slerp(_panelRoot.rotation, TargetRotation(), t);
        }

        void DrawPanel()
        {
            var frame = estimator.LastFrame;
            if (frame == null) return;
            _seenVersion = estimator.FrameVersion;

            int step = Mathf.Max(1, panelDownsample);
            int w = frame.width / step, h = frame.height / step;
            if (_panelTex == null || _panelTex.width != w || _panelTex.height != h)
            {
                if (_panelTex != null) Destroy(_panelTex);
                _panelTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                _panelPixels = new Color32[w * h];
                _panel.material.mainTexture = _panelTex;
                float aspect = h / (float)w;
                _panel.transform.localScale = new Vector3(panelWidth, panelWidth * aspect, 1f);
                _panel.transform.localPosition = Vector3.zero;
                _text.rectTransform.localPosition = new Vector3(-panelWidth * 0.5f, panelWidth * aspect * 0.5f + 0.005f, 0f);
            }

            // Texture row 0 is the bottom. Show the buffer in true orientation if we know it is top-first.
            bool flip = estimator.ImageRowsTopFirst;
            for (int y = 0; y < h; y++)
            {
                int srcRow = (y * step) * frame.width;
                int dstRow = (flip ? h - 1 - y : y) * w;
                for (int x = 0; x < w; x++)
                {
                    byte g = frame.gray[srcRow + x * step];
                    _panelPixels[dstRow + x] = new Color32(g, g, g, 255);
                }
            }

            var r = frame.result;
            foreach (var c in r.candidates)
                Box(ToPanel(c.centre, step, h, flip), c.size * 0.6f / step, new Color32(255, 210, 0, 255), w, h);
            if (r.found)
            {
                for (int k = 0; k < 4; k++)
                {
                    var a = ToPanel(r.corners[k], step, h, flip);
                    var b = ToPanel(r.corners[(k + 1) % 4], step, h, flip);
                    Line(a, b, CornerColours[k], w, h);
                    var sq = (r as Vision.SquareMarkerDetector.Result)?.markerCorners?[k];
                    if (sq != null)
                        for (int j = 0; j < 4; j++)
                            Line(ToPanel(sq[j], step, h, flip), ToPanel(sq[(j + 1) % 4], step, h, flip), CornerColours[k], w, h);
                    else
                        Box(a, r.glyphSize * 0.7f / step, CornerColours[k], w, h);
                }
            }

            _panelTex.SetPixels32(_panelPixels);
            _panelTex.Apply(false);
        }

        static Vector2 ToPanel(Vector2 p, int step, int h, bool flip)
        {
            var q = p / step;
            if (flip) q.y = h - 1 - q.y;
            return q;
        }

        void Box(Vector2 c, float half, Color32 col, int w, int h)
        {
            half = Mathf.Max(2f, half);
            var a = c + new Vector2(-half, -half); var b = c + new Vector2(half, -half);
            var d = c + new Vector2(half, half); var e = c + new Vector2(-half, half);
            Line(a, b, col, w, h); Line(b, d, col, w, h); Line(d, e, col, w, h); Line(e, a, col, w, h);
        }

        void Line(Vector2 a, Vector2 b, Color32 col, int w, int h)
        {
            int steps = Mathf.CeilToInt((b - a).magnitude);
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
                int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px >= 0 && py >= 0 && px < w && py < h) _panelPixels[py * w + px] = col;
                }
            }
        }

        // ---------- World overlay ----------

        void BuildOverlay()
        {
            _overlayRoot = new GameObject("BoardOverlay").transform;
            _overlayRoot.SetParent(transform, false);
            _glyphRect = MakeLine("GlyphRect", 5, true);
            _paintingRect = MakeLine("PaintingRect", 5, true);
            _normal = MakeLine("Normal", 2, false);

            var gridGo = new GameObject("PlaneGrid");
            gridGo.transform.SetParent(_overlayRoot, false);
            _grid = gridGo.AddComponent<MeshFilter>();
            var mr = gridGo.AddComponent<MeshRenderer>();
            _gridMat = new Material(unlitMaterial);
            mr.sharedMaterial = _gridMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        LineRenderer MakeLine(string name, int points, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_overlayRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(unlitMaterial);
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.loop = false;
            lr.widthMultiplier = lineWidth;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        void DrawOverlay()
        {
            BoardPoseEstimator.BoardPose pose;
            Color colour;
            if (estimator.IsLocked && estimator.LockedPose.HasValue)
            {
                pose = estimator.LockedPose.Value;
                colour = LockedColour;
            }
            else if (estimator.LastSample.HasValue && Time.time - estimator.LastSampleTime < staleAfter)
            {
                pose = estimator.LastSample.Value;
                colour = estimator.LastSampleAccepted ? Accepted : Rejected;
            }
            else
            {
                _overlayRoot.gameObject.SetActive(false);
                return;
            }
            _overlayRoot.gameObject.SetActive(true);
            _overlayRoot.SetPositionAndRotation(pose.centre, pose.rotation);

            var layout = estimator.layout;
            SetPolygon(_glyphRect, layout.MarkerCentres, colour);
            SetPolygon(_paintingRect, layout.PaintingCorners, colour * 0.75f);

            // Normal arrow out of the wall (towards the viewer is -Z).
            _normal.SetPosition(0, Vector3.zero);
            _normal.SetPosition(1, new Vector3(0f, 0f, -0.12f));
            _normal.material.color = colour;

            var mc = layout.MarkerCentres;
            UpdateGrid(new Vector2(Mathf.Abs(mc[1].x - mc[0].x), Mathf.Abs(mc[0].y - mc[3].y)) * 1.6f, 0.05f);
            _gridMat.color = colour * 0.6f;
        }

        static void SetPolygon(LineRenderer lr, Vector2[] p, Color colour)
        {
            for (int i = 0; i < 5; i++) lr.SetPosition(i, new Vector3(p[i % 4].x, p[i % 4].y, -0.002f));
            lr.material.color = colour;
        }

        static void SetRect(LineRenderer lr, Vector2 centre, Vector2 size, Color colour)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f;
            lr.SetPosition(0, new Vector3(centre.x - x, centre.y + y, -0.002f));
            lr.SetPosition(1, new Vector3(centre.x + x, centre.y + y, -0.002f));
            lr.SetPosition(2, new Vector3(centre.x + x, centre.y - y, -0.002f));
            lr.SetPosition(3, new Vector3(centre.x - x, centre.y - y, -0.002f));
            lr.SetPosition(4, new Vector3(centre.x - x, centre.y + y, -0.002f));
            lr.material.color = colour;
        }

        Vector2 _gridSize;

        void UpdateGrid(Vector2 size, float spacing)
        {
            if (_grid.sharedMesh != null && _gridSize == size) return;
            _gridSize = size;
            var verts = new System.Collections.Generic.List<Vector3>();
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            for (float x = -hx; x <= hx + 1e-4f; x += spacing)
            { verts.Add(new Vector3(x, -hy, -0.001f)); verts.Add(new Vector3(x, hy, -0.001f)); }
            for (float y = -hy; y <= hy + 1e-4f; y += spacing)
            { verts.Add(new Vector3(-hx, y, -0.001f)); verts.Add(new Vector3(hx, y, -0.001f)); }
            var idx = new int[verts.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            var mesh = new Mesh { name = "PlaneGrid" };
            mesh.SetVertices(verts);
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            _grid.sharedMesh = mesh;
        }
    }
}
