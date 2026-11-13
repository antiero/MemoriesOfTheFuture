using System.Text;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

namespace PictureChanges.UI
{
    /// <summary>
    /// Test mode, for when the real painting and its markers aren't available.
    ///
    ///  - Placing: point with the right hand (or controller, or just look) at a wall or other
    ///    surface from the Quest's scene model. A ghost outline of the painting follows the ray.
    ///    Pinch (or pull the trigger) to hang it. Without scene data, it falls back to about
    ///    1.2 m in front of you.
    ///  - Placed: camera detection stays off; the painting is locked exactly as if it had been
    ///    found, so the reveal, presence states and movie trigger all run for real. A readout
    ///    shows live distance and angle, Near/Far, Facing/Away, dwell progress and recent events.
    ///
    /// Toggle from the hand menu ("Test"). "Rescan" while in test mode re-places the painting.
    /// </summary>
    public class TestMode : MonoBehaviour
    {
        enum State { Off, Placing, Placed }

        [SerializeField] BoardPoseEstimator estimator;
        [SerializeField] PictureCanvas canvas;
        [SerializeField] ViewerPresence presence;
        [SerializeField] HandMenu handMenu;
        [SerializeField] Hand rightHand;
        [SerializeField] Transform head;
        [SerializeField] Transform rightControllerAnchor;
        [SerializeField] Material uiMaterial;
        [SerializeField] TMP_FontAsset font;
        [SerializeField] AudioClip placeSound;

        [SerializeField] float fallbackDistance = 1.2f;
        [SerializeField] float maxRayDistance = 8f;

        State _state;
        bool _wasPinching;
        Pose _ghostPose;
        bool _onSurface;

        Transform _ghost;
        LineRenderer _outline, _ray;
        Material _ghostMat, _rayMat;
        TextMeshPro _ghostText;
        Transform _hud;
        TextMeshPro _hudText;
        AudioSource _audio;

        public bool Active => _state != State.Off;

        public void Toggle() { if (Active) Exit(); else Enter(); }

        public void Enter()
        {
            canvas.Rescan();
            estimator.SuspendDetection = true;
            _state = State.Placing;
            _wasPinching = true;   // ignore a pinch that is already in progress
            Debug.Log("[TestMode] On: place the virtual painting");
        }

        /// <summary>Pick the painting up and place it again.</summary>
        public void Reposition()
        {
            if (!Active) return;
            canvas.Rescan();
            estimator.SuspendDetection = true;
            _state = State.Placing;
            _wasPinching = true;
        }

        public void Exit()
        {
            _state = State.Off;
            estimator.SuspendDetection = false;
            canvas.Rescan();
            Debug.Log("[TestMode] Off: back to marker detection");
        }

        void Start()
        {
            _ghost = new GameObject("TestGhost").transform;
            _ghost.SetParent(transform, false);
            _ghostMat = Flat(new Color(1f, 1f, 1f, 0.9f));
            _outline = Line("Outline", _ghost, 5, 0.003f, _ghostMat);
            _ghostText = Text(_ghost, "Pinch to hang the painting", 0.012f);

            _rayMat = Flat(new Color(1f, 1f, 1f, 0.35f));
            _ray = Line("Ray", transform, 2, 0.0015f, _rayMat);
            _ray.useWorldSpace = true;

            _hud = new GameObject("TestHud").transform;
            _hud.SetParent(transform, false);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(_hud, false);
            bg.transform.localScale = new Vector3(0.3f, 0.17f, 1f);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            var bgMat = new Material(uiMaterial);
            bgMat.SetVector("_Size", new Vector4(0.3f, 0.17f, 0, 0));
            bgMat.SetColor("_Color", new Color(0.05f, 0.05f, 0.07f, 0.7f));
            bgMat.SetColor("_BorderColor", new Color(0.95f, 0.78f, 0.45f, 0.6f));
            bgMat.SetFloat("_Radius", 0.01f);
            bg.GetComponent<Renderer>().material = bgMat;
            _hudText = Text(_hud, "", 0.0095f);
            _hudText.alignment = TextAlignmentOptions.TopLeft;
            _hudText.rectTransform.sizeDelta = new Vector2(0.28f, 0.155f);
            _hudText.textWrappingMode = TextWrappingModes.Normal;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;

            SetVisible(false, false);
        }

        Material Flat(Color c)
        {
            var m = new Material(uiMaterial);
            m.SetColor("_Color", c);
            m.SetColor("_BorderColor", new Color(0, 0, 0, 0));
            m.SetFloat("_Radius", 0f);
            m.SetFloat("_Border", 0f);
            m.SetVector("_Size", new Vector4(1, 1, 0, 0));
            return m;
        }

        static LineRenderer Line(string name, Transform parent, int points, float width, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.widthMultiplier = width;
            lr.numCapVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        TextMeshPro Text(Transform parent, string s, float capHeight)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.text = s;
            t.fontSize = TextSize.ForCapHeight(capHeight);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(0.5f, 0.05f);
            t.color = new Color(1f, 0.97f, 0.92f, 1f);
            return t;
        }

        void SetVisible(bool ghost, bool hud)
        {
            _ghost.gameObject.SetActive(ghost);
            _ray.gameObject.SetActive(ghost);
            _hud.gameObject.SetActive(hud);
        }

        void Update()
        {
            switch (_state)
            {
                case State.Off: SetVisible(false, false); break;
                case State.Placing: UpdatePlacing(); break;
                case State.Placed: SetVisible(false, true); UpdateHud(); break;
            }
        }

        void UpdatePlacing()
        {
            SetVisible(true, true);
            var ray = AimRay(out bool fromHand);
            _onSurface = false;
            Vector3 point, normal;
            var room = MRUK.Instance != null && MRUK.Instance.IsInitialized ? MRUK.Instance.GetCurrentRoom() : null;
            if (room != null && room.Raycast(ray, maxRayDistance, out RaycastHit hit, out MRUKAnchor _))
            {
                point = hit.point;
                normal = hit.normal;
                _onSurface = true;
            }
            else
            {
                var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                point = head.position + flat * fallbackDistance + Vector3.down * 0.1f;
                normal = -flat;
            }

            var forward = -normal;   // painting +Z points into the surface
            var up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(head.forward, forward);   // floor / table
            _ghostPose = new Pose(point + normal * 0.003f, Quaternion.LookRotation(forward, up.normalized));

            _ghost.SetPositionAndRotation(_ghostPose.position, _ghostPose.rotation);
            var c = estimator.layout.PaintingCorners;
            for (int i = 0; i < 5; i++) _outline.SetPosition(i, new Vector3(c[i % 4].x, c[i % 4].y, -0.002f));
            _ghostMat.SetColor("_Color", _onSurface ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.95f, 0.78f, 0.45f, 0.9f));
            _ghostText.rectTransform.localPosition = new Vector3(0f, Mathf.Min(c[2].y, c[3].y) - 0.03f, -0.002f);
            _ghostText.text = _onSurface ? "Pinch to hang the painting" : "No surface: pinch to place it here";

            _ray.SetPosition(0, ray.origin);
            _ray.SetPosition(1, _onSurface ? point : ray.origin + ray.direction * 0.4f);
            _ray.gameObject.SetActive(fromHand);

            PlaceHud();
            _hudText.text = "<b>TEST MODE</b>\nPoint at a wall and pinch to hang the virtual painting.\n" +
                            (room == null ? "<color=#F2C873>No scene model: run Space Setup, or place in front of you.</color>" : "");

            bool pinching = SelectPressed();
            if (pinching && !_wasPinching && (handMenu == null || !handMenu.IsShown)) PlaceAt(_ghostPose);
            _wasPinching = pinching;
        }

        /// <summary>Hang the virtual painting at a pose (board origin, +Z into the surface).</summary>
        public void PlaceAt(Pose pose)
        {
            if (!Active) Enter();
            presence.ResetState();
            estimator.ForceLock(pose);
            _state = State.Placed;
            if (placeSound != null) { _audio.transform.position = pose.position; _audio.PlayOneShot(placeSound); }
            Debug.Log($"[TestMode] Painting placed at {pose.position:F2}");
        }

        Ray AimRay(out bool fromHand)
        {
            fromHand = false;
            if (rightHand != null && rightHand.IsTrackedDataValid && rightHand.IsPointerPoseValid &&
                rightHand.GetPointerPose(out var p))
            {
                fromHand = true;
                return new Ray(p.position, p.forward);
            }
            if (rightControllerAnchor != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            {
                fromHand = true;
                return new Ray(rightControllerAnchor.position, rightControllerAnchor.forward);
            }
            return new Ray(head.position, head.forward);   // gaze fallback
        }

        bool SelectPressed()
        {
            if (rightHand != null && rightHand.IsTrackedDataValid && rightHand.GetIndexFingerIsPinching()) return true;
            return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        }

        void PlaceHud()
        {
            // Lower right of view, lazy-follow so it stays readable without being head-locked.
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            var target = head.position + yaw * new Vector3(0.34f, -0.26f, 0.7f);
            var rot = Quaternion.LookRotation(target - head.position, Vector3.up);
            float t = 1f - Mathf.Exp(-4f * Time.deltaTime);
            if (!_hud.gameObject.activeSelf || (_hud.position - target).sqrMagnitude > 1f) _hud.SetPositionAndRotation(target, rot);
            else _hud.SetPositionAndRotation(Vector3.Lerp(_hud.position, target, t), Quaternion.Slerp(_hud.rotation, rot, t));
            _hudText.rectTransform.localPosition = new Vector3(0f, 0f, -0.001f);
        }

        void UpdateHud()
        {
            PlaceHud();
            var sb = new StringBuilder();
            sb.Append("<b>TEST MODE</b>  virtual painting\n");
            if (!presence.Active) { sb.Append("waiting for placement..."); _hudText.text = sb.ToString(); return; }

            string near = presence.IsNear ? "<color=#7CFC9A>NEAR</color>" : "<color=#9AA0A6>far</color>";
            string face = presence.IsFacing ? "<color=#7CFC9A>FACING</color>" : "<color=#9AA0A6>away</color>";
            sb.Append($"{presence.Distance:F2} m  ·  {presence.Angle:F0}°   {near}  {face}\n");
            int bars = Mathf.RoundToInt(presence.DwellProgress * 12);
            string trig = presence.HasTriggered ? "<color=#F2C873>TRIGGERED</color>" : $"dwell [{new string('|', bars).PadRight(12, '.')}]";
            sb.Append($"{trig}   movie: {(canvas.IsPlaying ? "playing" : canvas.HasPlayed ? "done" : "ready")}\n");
            var h = presence.History;
            for (int i = h.Count - 1; i >= 0 && i >= h.Count - 5; i--)
                sb.Append($"<size=85%>{Time.time - h[i].time,5:F1}s ago  {h[i].evt}  ({h[i].distance:F2} m, {h[i].angle:F0}°)</size>\n");
            _hudText.text = sb.ToString();
        }
    }
}
