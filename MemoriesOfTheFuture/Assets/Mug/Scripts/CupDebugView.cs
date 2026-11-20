using TMPro;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// In-headset debugging for CupDetector (on by default; toggle with Debug in the hand menu).
    ///
    ///  - Panel below your line of sight: the camera frame the model last analysed, the best
    ///    cup box (green, or orange when below the score threshold) and the best box of any
    ///    class (grey), plus a status readout: model, rate, scores, depth and size distances.
    ///  - World markers: yellow = estimated cup centre (from the camera or the holding hand),
    ///    cyan = depth hit on the cup's surface, a line from the camera at capture to the centre
    ///    (camera tracking only).
    /// </summary>
    public class CupDebugView : MonoBehaviour
    {
        [SerializeField] CupDetector detector;
        [SerializeField] MugScream mugScream;
        [SerializeField] MugInterior interior;
        [SerializeField] RimTracker rimTracker;
        [SerializeField] RuneCapture runeCapture;
        [SerializeField] DreamState dream;
        [SerializeField] DreamSplatRoom splatRoom;
        [SerializeField] Transform head;
        [Tooltip("URP Unlit material asset, referenced so the shader is kept in builds.")]
        [SerializeField] Material unlitMaterial;
        [SerializeField] TMP_FontAsset font;
        [SerializeField] bool visible = false;

        [Header("Panel")]
        [SerializeField] Vector3 panelOffset = new Vector3(0f, -0.2f, 0.6f);
        [SerializeField] float panelWidth = 0.28f;
        [SerializeField] float followSpeed = 4f;
        [SerializeField] float lineWidth = 0.0015f;

        static readonly Color CupColour = new Color(0.2f, 1f, 0.3f);
        static readonly Color WeakColour = new Color(1f, 0.55f, 0f);
        static readonly Color BestColour = new Color(0.75f, 0.75f, 0.75f);
        static readonly Color CentreColour = new Color(1f, 0.9f, 0.1f);
        static readonly Color DepthColour = new Color(0f, 0.9f, 1f);

        Transform _panelRoot, _worldRoot;
        Renderer _frameQuad;
        TextMeshPro _text;
        LineRenderer _cupBox, _bestBox, _rayLine, _gripX, _gripY, _gripZ;
        Transform _centreMarker, _depthMarker;
        float _panelHeight;
        int _seenVersion = -1;

        public bool Visible
        {
            get => visible;
            set { visible = value; ApplyVisibility(); }
        }

        public void Toggle() => Visible = !Visible;

        void Start()
        {
            _panelHeight = panelWidth * 0.75f;   // 4:3 camera
            BuildPanel();
            BuildWorldMarkers();
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            if (detector != null) detector.KeepDebugFrame = visible;
            if (mugScream != null) mugScream.ShowZone = visible;
            if (_panelRoot != null) _panelRoot.gameObject.SetActive(visible);
            if (_worldRoot != null) _worldRoot.gameObject.SetActive(visible);
        }

        // ------------------------------------------------------------ build

        void BuildPanel()
        {
            _panelRoot = new GameObject("CupDebugPanel").transform;
            _panelRoot.SetParent(transform, false);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "AnalysedFrame";
            quad.transform.SetParent(_panelRoot, false);
            quad.transform.localScale = new Vector3(panelWidth, _panelHeight, 1f);
            _frameQuad = quad.GetComponent<Renderer>();
            _frameQuad.material = new Material(unlitMaterial);
            _frameQuad.material.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.15f));

            _cupBox = Line("CupBox", _panelRoot, 5, CupColour, lineWidth, true);
            _bestBox = Line("BestBox", _panelRoot, 5, BestColour, lineWidth * 0.6f, true);

            var textGo = new GameObject("Status");
            textGo.transform.SetParent(_panelRoot, false);
            _text = textGo.AddComponent<TextMeshPro>();
            if (font != null) _text.font = font;
            _text.fontSize = PictureChanges.UI.TextSize.ForCapHeight(0.0065f);
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.rectTransform.pivot = new Vector2(0f, 1f);
            _text.rectTransform.sizeDelta = new Vector2(panelWidth, 0.1f);
            _text.rectTransform.localPosition = new Vector3(-panelWidth * 0.5f, -_panelHeight * 0.5f - 0.008f, -0.001f);
            _text.color = Color.white;
            _text.outlineWidth = 0.2f;
            _text.outlineColor = Color.black;

            if (head != null) _panelRoot.SetPositionAndRotation(PanelTargetPosition(), PanelTargetRotation());
        }

        void BuildWorldMarkers()
        {
            _worldRoot = new GameObject("CupDebugWorld").transform;
            _worldRoot.SetParent(transform, false);
            _centreMarker = Marker("CupCentre", CentreColour, 0.012f);
            _depthMarker = Marker("DepthHit", DepthColour, 0.008f);
            _rayLine = Line("CameraRay", _worldRoot, 2, CentreColour, 0.001f, false);
            // Held-mug gizmo at the mug centre: red = handle -> body (+X), green = mug up (+Y), blue = +Z (logo).
            _gripX = Line("GripX", _worldRoot, 2, new Color(1f, 0.2f, 0.2f), 0.002f, false);
            _gripY = Line("GripY", _worldRoot, 2, new Color(0.2f, 1f, 0.2f), 0.002f, false);
            _gripZ = Line("GripZ", _worldRoot, 2, new Color(0.3f, 0.5f, 1f), 0.002f, false);
        }

        Transform Marker(string name, Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(_worldRoot, false);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.material = new Material(unlitMaterial);
            r.material.SetColor("_BaseColor", c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        LineRenderer Line(string name, Transform parent, int points, Color c, float width, bool local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = !local;
            lr.positionCount = points;
            lr.widthMultiplier = width;
            lr.material = new Material(unlitMaterial);
            lr.material.SetColor("_BaseColor", c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        // ------------------------------------------------------------ per frame

        void Update()
        {
            if (!visible || detector == null) return;
            FollowHead();
            UpdatePanel();
            UpdateWorld();
        }

        void UpdatePanel()
        {
            if (detector.DebugFrame != null && _frameQuad.material.mainTexture != detector.DebugFrame)
            {
                _frameQuad.material.SetTexture("_BaseMap", detector.DebugFrame);
                _frameQuad.material.SetColor("_BaseColor", Color.white);
            }

            if (detector.ResultVersion != _seenVersion)
            {
                _seenVersion = detector.ResultVersion;
                bool any = detector.FramesAnalysed > 0;
                SetBox(_cupBox, detector.CupViewportRect, any && detector.CupScore > 0.05f);
                var colour = detector.CupInLastFrame ? CupColour : WeakColour;
                _cupBox.material.SetColor("_BaseColor", colour);
                SetBox(_bestBox, detector.BestViewportRect, any && detector.BestLabel != "cup");
            }

            _text.text = StatusText();
        }

        string StatusText()
        {
            var d = detector;
            string hint =
                d.Status.StartsWith("ERROR") ? $"<color=#FF4136>{d.Status}</color>" :
                !d.ModelReady ? d.Status :
                d.Status.StartsWith("Waiting") ? "<color=#FF851B>Camera not running: check the camera permission</color>" :
                d.TrackingSource == CupDetector.Source.Vision ? "<color=#2ECC40>Tracking cup (camera)</color>" :
                d.TrackingSource == CupDetector.Source.Hand ? $"<color=#7FDBFF>Held in {(d.HoldingHand == "L" ? "left" : "right")} hand (pose from hand joints)</color>" :
                d.CupScore > 0.05f ? $"<color=#FF851B>Cup seen but weak ({d.CupScore:0.00}): get closer or turn it</color>" :
                "Looking for a cup: look straight at it, 0.3-1.5 m away";
            string dist(float v) => float.IsNaN(v) ? "-" : $"{v:0.00} m";
            return
                $"<b>{hint}</b>\n" +
                $"<size=80%>model {(d.ModelReady ? "ready" : "not ready")}  {d.AnalysedPerSecond:0.0}/s  {d.InferenceMs:0} ms  frames {d.FramesAnalysed}\n" +
                $"cup {d.CupScore:0.00}   best: {d.BestLabel} {d.BestScore:0.00}\n" +
                $"depth {d.DepthStatus} {dist(d.DepthDistance)}   size {dist(d.SizeDistance)}   using {d.DistanceSource} ({d.Mode})\n" +
                $"source {d.TrackingSource}   held by {(d.HoldingHand == "" ? "-" : d.HoldingHand)}   {d.HandsText}" +
                $"{(d.HoldingHand != "" ? $"\nfinger model off {d.PickUpModelError * 100f:0.0} cm at pick-up   camera corrected {d.HeldCorrection * 100f:0.0} cm   glitches ignored {d.GlitchesIgnored}" : "")}" +
                $"{(d.HoldingHand != "" ? $"\ntilt {d.TiltMode}: hand {d.HandTilt:0}°  mug {d.MugTilt:0}°" : "")}" +
                $"{(d.Rejected != "" ? $"\n<color=#FF851B>ignored: {d.Rejected}</color>" : "")}" +
                $"{(mugScream != null ? $"\n<color=#FF6F61>{mugScream.StatusLine}</color>" : "")}" +
                $"{(interior != null ? $"\n<color=#B10DC9>{interior.StatusLine}</color>" : "")}" +
                $"{(rimTracker != null ? $"\n<color=#7FDBFF>{rimTracker.StatusLine}</color>" : "")}" +
                $"{(dream != null ? $"\n<color=#F012BE>{dream.StatusLine}</color>" : "")}" +
                $"{(splatRoom != null ? $"\n<color=#01FF70>{splatRoom.StatusLine}</color>" : "")}" +
                $"{(runeCapture != null && (runeCapture.Recording || runeCapture.Saved > 0) ? $"   <color=#FF4136>REC {runeCapture.Saved}{(runeCapture.Recording ? "" : " (stopped)")}</color>" : "")}" +
                $"{(d.Frozen ? "   <color=#7FDBFF>FROZEN</color>" : "")}</size>";
        }

        void SetBox(LineRenderer lr, Rect viewport, bool show)
        {
            lr.enabled = show;
            if (!show) return;
            Vector3 P(float vx, float vy) => new Vector3((vx - 0.5f) * panelWidth, (vy - 0.5f) * _panelHeight, -0.001f);
            lr.SetPosition(0, P(viewport.xMin, viewport.yMin));
            lr.SetPosition(1, P(viewport.xMax, viewport.yMin));
            lr.SetPosition(2, P(viewport.xMax, viewport.yMax));
            lr.SetPosition(3, P(viewport.xMin, viewport.yMax));
            lr.SetPosition(4, P(viewport.xMin, viewport.yMin));
        }

        void UpdateWorld()
        {
            bool held = detector.TrackingSource == CupDetector.Source.Hand;
            foreach (var (lr, axis) in new[] { (_gripX, Vector3.right), (_gripY, Vector3.up), (_gripZ, Vector3.forward) })
            {
                lr.enabled = held;
                if (!held) continue;
                var h = detector.HeldHole;
                lr.SetPosition(0, h.position);
                lr.SetPosition(1, h.position + h.rotation * axis * 0.04f);
            }

            bool tracking = detector.Tracking;
            _centreMarker.gameObject.SetActive(tracking);
            _rayLine.enabled = tracking;
            bool vision = detector.TrackingSource == CupDetector.Source.Vision;
            _rayLine.enabled = vision;
            _depthMarker.gameObject.SetActive(vision && detector.DepthStatus == Meta.XR.EnvironmentRaycastHitStatus.Hit);
            if (!tracking) return;
            _centreMarker.position = detector.CupCentre;
            _depthMarker.position = detector.DepthHitPoint;
            _rayLine.SetPosition(0, detector.FrameCameraPose.position);
            _rayLine.SetPosition(1, detector.CupCentre);
        }

        void FollowHead()
        {
            if (head == null) return;
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            _panelRoot.SetPositionAndRotation(
                Vector3.Lerp(_panelRoot.position, PanelTargetPosition(), t),
                Quaternion.Slerp(_panelRoot.rotation, PanelTargetRotation(), t));
        }

        Vector3 PanelTargetPosition()
        {
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            return head.position + yaw * panelOffset;
        }

        // The quad's visible side faces -Z, so +Z points away from the viewer.
        Quaternion PanelTargetRotation() => Quaternion.LookRotation(PanelTargetPosition() - head.position, Vector3.up);
    }
}
