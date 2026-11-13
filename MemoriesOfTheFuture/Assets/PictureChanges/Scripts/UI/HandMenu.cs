using System;
using System.Collections.Generic;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

namespace PictureChanges.UI
{
    /// <summary>
    /// Palm-up hand menu, following Meta's hand-menu conventions (and the Pezzi WebXR menu):
    ///  - Turn the LEFT palm towards your face and the menu fades in, floating just off the palm.
    ///    Hysteresis (enter/exit thresholds) stops it flickering at the boundary.
    ///  - Press buttons with the RIGHT index finger (poke). Only an extended index finger counts,
    ///    so a relaxed hand brushing past does nothing.
    ///  - Hover glow, a press that pushes the button in, a click sound, and press-then-release
    ///    (with a short cooldown) so one poke is one action.
    /// </summary>
    public class HandMenu : MonoBehaviour
    {
        [Header("Hands (Interaction SDK hand data sources)")]
        [SerializeField] Hand leftHand;
        [SerializeField] Hand rightHand;
        [SerializeField] Transform head;

        [Header("Look")]
        [Tooltip("Material using the PictureChanges/UI shader.")]
        [SerializeField] Material uiMaterial;
        [SerializeField] TMP_FontAsset font;
        [SerializeField] AudioClip clickSound;
        [SerializeField] AudioClip hoverSound;

        [Header("Behaviour (same values as Pezzi)")]
        [SerializeField] float faceUserEnterDot = 0.55f;
        [SerializeField] float faceUserExitDot = 0.35f;
        [SerializeField] float fadeSpeed = 9f;
        [SerializeField] float followSpeed = 14f;
        [Tooltip("Menu distance off the palm, towards the face (m).")]
        [SerializeField] float palmOffset = 0.045f;
        [Tooltip("Shift along the hand towards the fingers (m).")]
        [SerializeField] float fingerwardOffset = 0.03f;
        [SerializeField] float buttonSize = 0.045f;
        [SerializeField] float buttonGap = 0.008f;

        const float IndexEnterDot = 0.68f, IndexExitDot = 0.56f;
        const float HoverDepth = 0.05f;      // hover zone in front of a button
        const float PressDepth = 0.002f;     // fingertip this close (or through) = press
        const float ReleaseDepth = 0.012f;   // must pull back this far before pressing again
        const float MaxPush = 0.008f;        // how far the plate visibly travels
        const float Cooldown = 0.35f;

        class Button
        {
            public string label;
            public Action onPress;
            public Func<bool> isOn;
            public Transform root, plate;
            public Material mat;
            public TextMeshPro text;
            public bool hovered, pressed;
            public float hoverGlow, lastPress = -99f;
        }

        readonly List<Button> _buttons = new List<Button>();
        Transform _menu;
        Material _panelMat;
        TextMeshPro _title;
        AudioSource _audio;
        bool _shown, _indexExtended;
        float _alpha;
        Vector3 _vel;

        public bool IsShown => _shown;

        /// <summary>Adds a button. Call before or after Start; the grid re-lays itself out.</summary>
        public void AddButton(string label, Action onPress, Func<bool> isOn = null)
        {
            var b = new Button { label = label, onPress = onPress, isOn = isOn };
            _buttons.Add(b);
            if (_menu != null) { BuildButton(b); Layout(); }
        }

        void Start()
        {
            BuildMenu();
            foreach (var b in _buttons) BuildButton(b);
            Layout();
            SetAlpha(0f);
        }

        // ---------------------------------------------------------------- build

        void BuildMenu()
        {
            _menu = new GameObject("HandMenu").transform;
            _menu.SetParent(transform, false);

            var panel = Quad("Panel", _menu);
            _panelMat = panel.GetComponent<Renderer>().material = new Material(uiMaterial);
            _panelMat.SetColor("_Color", new Color(0.06f, 0.06f, 0.08f, 0.78f));
            _panelMat.SetColor("_BorderColor", new Color(1f, 1f, 1f, 0.18f));
            _panelMat.SetFloat("_Radius", 0.014f);

            _title = Text("Title", _menu, "Memories of the Future", 0.0075f, new Color(1f, 1f, 1f, 0.7f));

            _audio = _menu.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 0.3f;
            _audio.volume = 0.6f;
        }

        void BuildButton(Button b)
        {
            b.root = new GameObject("Button_" + b.label).transform;
            b.root.SetParent(_menu, false);
            b.plate = Quad("Plate", b.root);
            b.plate.localScale = new Vector3(buttonSize, buttonSize, 1f);
            b.mat = b.plate.GetComponent<Renderer>().material = new Material(uiMaterial);
            b.mat.SetVector("_Size", new Vector4(buttonSize, buttonSize, 0, 0));
            b.mat.SetFloat("_Radius", buttonSize * 0.22f);
            b.mat.SetFloat("_Border", 0.0012f);
            b.text = Text("Label", b.plate, b.label, 0.0085f, Color.white);
            b.text.rectTransform.localScale = new Vector3(1f / buttonSize, 1f / buttonSize, 1f);   // undo plate scale
        }

        void Layout()
        {
            int cols = _buttons.Count > 4 ? 3 : 2, rows = Mathf.CeilToInt(_buttons.Count / (float)cols);
            float step = buttonSize + buttonGap;
            float w = cols * step + buttonGap, h = rows * step + buttonGap + 0.014f;   // room for the title
            var panel = _menu.Find("Panel");
            panel.localScale = new Vector3(w, h, 1f);
            panel.localPosition = new Vector3(0f, -0.007f, 0.002f);
            _panelMat.SetVector("_Size", new Vector4(w, h, 0, 0));
            _title.rectTransform.localPosition = new Vector3(0f, h * 0.5f - 0.014f, -0.001f);

            for (int i = 0; i < _buttons.Count; i++)
            {
                int r = i / cols, c = i % cols;
                float x = (c - (cols - 1) * 0.5f) * step;
                float y = ((rows - 1) * 0.5f - r) * step - 0.012f;
                _buttons[i].root.localPosition = new Vector3(x, y, 0f);
            }
        }

        static Transform Quad(string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        TextMeshPro Text(string name, Transform parent, string content, float size, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.text = content;
            t.fontSize = TextSize.ForCapHeight(size);
            t.rectTransform.sizeDelta = new Vector2(0.1f, 0.02f);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = colour;
            t.rectTransform.localPosition = new Vector3(0f, 0f, -0.001f);
            return t;
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            bool hasPose = TryGetMenuPose(out var pose);
            float target = hasPose && _shown ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, fadeSpeed * Time.deltaTime);
            SetAlpha(_alpha);
            if (!hasPose && _alpha <= 0f) return;

            if (hasPose)
            {
                float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
                if (_alpha <= 0.01f) _menu.SetPositionAndRotation(pose.position, pose.rotation);   // appear in place
                else _menu.SetPositionAndRotation(Vector3.Lerp(_menu.position, pose.position, t),
                                                  Quaternion.Slerp(_menu.rotation, pose.rotation, t));
            }
            UpdateButtons(_alpha > 0.8f);
        }

        /// <summary>Pezzi's palm test, ported: palm normal from knuckles and wrist, signed per hand.</summary>
        bool TryGetMenuPose(out Pose pose)
        {
            pose = default;
            if (leftHand == null || head == null || !leftHand.IsTrackedDataValid) { _shown = false; return false; }
            if (!leftHand.GetJointPose(HandJointId.HandWristRoot, out var wrist) ||
                !leftHand.GetJointPose(HandJointId.HandIndex1, out var index) ||
                !leftHand.GetJointPose(HandJointId.HandMiddle1, out var middle) ||
                !leftHand.GetJointPose(HandJointId.HandPinky1, out var pinky)) { _shown = false; return false; }

            var across = (index.position - pinky.position).normalized;      // towards the thumb side
            var along = (middle.position - wrist.position).normalized;      // wrist -> fingers
            // Palm normal of the LEFT hand (out of the palm, not the back of the hand).
            // Verified on device: Cross(across, along) points out of the palm for the left hand.
            var palmNormal = Vector3.Cross(across, along).normalized;
            var toHead = (head.position - wrist.position).normalized;

            float dot = Vector3.Dot(palmNormal, toHead);
            _shown = _shown ? dot > faceUserExitDot : dot > faceUserEnterDot;

            var up = Vector3.ProjectOnPlane(along, palmNormal).normalized;
            var knuckles = (index.position + middle.position + pinky.position) / 3f;
            var palmCentre = Vector3.Lerp(wrist.position, knuckles, 0.7f);
            var pos = palmCentre + palmNormal * palmOffset + up * fingerwardOffset;
            // Face the user: the menu's visible side (-Z) points along the palm normal. Keep it
            // upright relative to the head so text reads naturally even with a tilted wrist.
            var forward = -palmNormal;
            var headUp = Vector3.ProjectOnPlane(head.up, forward).normalized;
            pose = new Pose(pos, Quaternion.LookRotation(forward, Vector3.Slerp(up, headUp, 0.6f)));
            return true;
        }

        void UpdateButtons(bool interactive)
        {
            bool hasTip = TryGetPokeTip(out var tip);
            foreach (var b in _buttons)
            {
                bool on = b.isOn != null && b.isOn();
                float depth = 0f;
                bool hover = false;
                if (interactive && hasTip)
                {
                    var local = b.root.InverseTransformPoint(tip);   // -z = towards the user
                    float half = buttonSize * 0.5f + 0.004f;
                    bool inside = Mathf.Abs(local.x) < half && Mathf.Abs(local.y) < half;
                    hover = inside && local.z > -HoverDepth && local.z < 0.03f;
                    if (hover) depth = Mathf.Clamp(local.z + MaxPush, 0f, MaxPush);

                    if (!b.pressed && hover && local.z > -PressDepth && Time.time - b.lastPress > Cooldown)
                    {
                        b.pressed = true;
                        b.lastPress = Time.time;
                        if (clickSound != null) _audio.PlayOneShot(clickSound);
                        try { b.onPress?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    }
                    else if (b.pressed && (!inside || local.z < -ReleaseDepth)) b.pressed = false;
                }
                else b.pressed = false;

                if (hover && !b.hovered && hoverSound != null) _audio.PlayOneShot(hoverSound, 0.4f);
                b.hovered = hover;
                b.hoverGlow = Mathf.MoveTowards(b.hoverGlow, hover ? 1f : 0f, Time.deltaTime * 8f);

                b.plate.localPosition = new Vector3(0f, 0f, depth);
                var fill = on ? new Color(0.95f, 0.78f, 0.45f, 0.95f) : new Color(1f, 1f, 1f, 0.12f + 0.16f * b.hoverGlow);
                var border = new Color(1f, 1f, 1f, 0.25f + 0.6f * b.hoverGlow);
                b.mat.SetColor("_Color", fill);
                b.mat.SetColor("_BorderColor", border);
                b.text.color = on ? new Color(0.1f, 0.08f, 0.05f, 1f) : Color.white;
            }
        }

        /// <summary>Right index fingertip, only while the finger is deliberately extended.</summary>
        bool TryGetPokeTip(out Vector3 tip)
        {
            tip = default;
            if (rightHand == null || !rightHand.IsTrackedDataValid) { _indexExtended = false; return false; }
            if (!rightHand.GetJointPose(HandJointId.HandIndexTip, out var t) ||
                !rightHand.GetJointPose(HandJointId.HandIndex2, out var mid) ||
                !rightHand.GetJointPose(HandJointId.HandIndex1, out var prox)) { _indexExtended = false; return false; }
            var dir = t.position - prox.position;
            var baseDir = mid.position - prox.position;
            if (dir.sqrMagnitude < 1e-6f || baseDir.sqrMagnitude < 1e-6f) return false;
            float d = Vector3.Dot(dir.normalized, baseDir.normalized);
            _indexExtended = d >= (_indexExtended ? IndexExitDot : IndexEnterDot);
            tip = t.position;
            return _indexExtended;
        }

        void SetAlpha(float a)
        {
            if (_menu == null) return;
            bool visible = a > 0.001f;
            if (_menu.gameObject.activeSelf != visible) _menu.gameObject.SetActive(visible);
            float eased = a * a * (3f - 2f * a);
            _menu.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, eased);
            _panelMat.SetFloat("_Opacity", eased);
            foreach (var b in _buttons) { if (b.mat != null) b.mat.SetFloat("_Opacity", eased); if (b.text != null) b.text.alpha = eased; }
            _title.alpha = eased * 0.7f;
        }
    }
}
