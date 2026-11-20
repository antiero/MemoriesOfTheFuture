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
        [Tooltip("Button height (m).")]
        [SerializeField] float buttonSize = 0.045f;
        [Tooltip("Button width (m). Wider than tall so labels like \"Painting\" fit.")]
        [SerializeField] float buttonWidth = 0.054f;
        [SerializeField] float buttonGap = 0.008f;

        [Header("Buttons")]
        [Tooltip("Also show the developer buttons (detector debugging, tuning, captures). " +
                 "Off for artists and visitors: they see only the buttons that change the experience.")]
        [SerializeField] bool developerButtons = false;

        // Palette: a calm dark panel, solid slate buttons with white labels, warm amber when on.
        // Everything is opaque enough that the room seen through passthrough never shows through the text.
        static readonly Color PanelFill = new Color(0.09f, 0.09f, 0.11f, 0.94f);
        static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color ButtonOff = new Color(0.20f, 0.21f, 0.25f, 1f);
        static readonly Color ButtonOffHover = new Color(0.30f, 0.31f, 0.36f, 1f);
        static readonly Color ButtonOn = new Color(1.00f, 0.79f, 0.50f, 1f);
        static readonly Color ButtonOnHover = new Color(1.00f, 0.86f, 0.64f, 1f);
        static readonly Color BorderIdle = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color BorderHover = new Color(1f, 0.86f, 0.62f, 0.95f);
        static readonly Color LabelOff = new Color(0.96f, 0.96f, 0.98f, 1f);
        static readonly Color LabelOn = new Color(0.10f, 0.06f, 0.02f, 1f);
        static readonly Color Divider = new Color(1f, 1f, 1f, 0.12f);
        // Explicit draw order, back to front. Left to distance sorting, the panel was sometimes drawn over
        // the buttons (dulling the lit ones) and the plates over their labels.
        const int PanelQueue = 3006, DividerQueue = 3008, PlateQueue = 3010, TextQueue = 3020;
        const float TitleSpace = 0.020f;     // room above the buttons for the title
        const float GroupSpace = 0.008f;     // extra space (with a divider) between button groups

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
            public bool hovered, pressed, newRow, developer;
            public bool rowBreak;   // newRow as laid out, once hidden buttons are skipped
            public float hoverGlow, lastPress = -99f;
        }

        readonly List<Button> _buttons = new List<Button>();
        readonly List<Material> _dividerMats = new List<Material>();
        readonly List<Transform> _dividers = new List<Transform>();
        Transform _menu;
        Material _panelMat;
        TextMeshPro _title;
        AudioSource _audio;
        bool _shown, _indexExtended, _breakBeforeNext;
        float _alpha;
        Vector3 _vel;

        public bool IsShown => _shown;

        /// <summary>Show the developer buttons too (also the "Developer Buttons" box in the Inspector).</summary>
        public bool DeveloperButtons
        {
            get => developerButtons;
            set { developerButtons = value; if (_menu != null) Layout(); }
        }

        /// <summary>
        /// Adds a button. Call before or after Start; the grid re-lays itself out.
        /// <paramref name="developer"/> buttons are hidden unless <see cref="DeveloperButtons"/> is on.
        /// </summary>
        public void AddButton(string label, Action onPress, Func<bool> isOn = null, bool developer = false)
        {
            var b = new Button { label = label, onPress = onPress, isOn = isOn, newRow = _breakBeforeNext, developer = developer };
            _breakBeforeNext = false;
            _buttons.Add(b);
            if (_menu != null) { BuildButton(b); Layout(); }
        }

        bool IsVisible(Button b) => developerButtons || !b.developer;

        /// <summary>The next button added starts a new row (e.g. to keep a group of buttons together).</summary>
        public void AddRowBreak() => _breakBeforeNext = true;

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
            _panelMat.SetColor("_Color", PanelFill);
            _panelMat.SetColor("_BorderColor", PanelBorder);
            _panelMat.SetFloat("_Radius", 0.018f);
            _panelMat.renderQueue = PanelQueue;

            _title = Text("Title", _menu, "Memories of the Future", 0.0062f, new Color(1f, 1f, 1f, 0.8f));
            _title.characterSpacing = 4f;

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
            b.plate.localScale = new Vector3(buttonWidth, buttonSize, 1f);
            b.mat = b.plate.GetComponent<Renderer>().material = new Material(uiMaterial);
            b.mat.SetVector("_Size", new Vector4(buttonWidth, buttonSize, 0, 0));
            b.mat.SetFloat("_Radius", buttonSize * 0.28f);
            b.mat.SetFloat("_Border", 0.0012f);
            b.mat.SetColor("_Color", ButtonOff);
            b.mat.SetColor("_BorderColor", BorderIdle);
            b.mat.renderQueue = PlateQueue;
            b.text = Text("Label", b.plate, b.label, 0.0080f, LabelOff);
            b.text.fontStyle = FontStyles.Bold;
            b.text.rectTransform.localScale = new Vector3(1f / buttonWidth, 1f / buttonSize, 1f);   // undo plate scale
            b.text.rectTransform.sizeDelta = new Vector2(buttonWidth - 0.007f, buttonSize);
            // Long labels shrink to fit rather than spilling past the plate.
            b.text.enableAutoSizing = true;
            b.text.fontSizeMax = b.text.fontSize;
            b.text.fontSizeMin = TextSize.ForCapHeight(0.0050f);
        }

        Transform GetDivider(int i)
        {
            while (_dividers.Count <= i)
            {
                var d = Quad("Divider", _menu);
                var m = d.GetComponent<Renderer>().material = new Material(uiMaterial);
                m.SetColor("_Color", Divider);
                m.SetColor("_BorderColor", Color.clear);
                m.SetFloat("_Border", 0f);
                m.SetFloat("_Radius", 0.0004f);
                m.SetFloat("_Opacity", _alpha);
                m.renderQueue = DividerQueue;
                _dividers.Add(d);
                _dividerMats.Add(m);
            }
            return _dividers[i];
        }

        void Layout()
        {
            // Only the visible buttons are laid out. A row break on a hidden button carries over to the
            // next visible one, so a group whose buttons are all hidden leaves no gap.
            var shown = new List<Button>();
            bool pendingBreak = false;
            foreach (var b in _buttons)
            {
                bool visible = IsVisible(b);
                if (b.root != null) b.root.gameObject.SetActive(visible);
                pendingBreak |= b.newRow;
                if (!visible) continue;
                b.rowBreak = pendingBreak && shown.Count > 0;
                pendingBreak = false;
                shown.Add(b);
            }

            // Split into rows: wrap at the column count, and start a new row at each row break.
            int largestGroup = 0, run = 0;
            foreach (var b in shown) { run = b.rowBreak ? 1 : run + 1; largestGroup = Mathf.Max(largestGroup, run); }
            int cols = largestGroup > 4 ? 3 : 2;
            var rowOf = new int[shown.Count];
            var rowCount = new List<int>();
            for (int i = 0; i < shown.Count; i++)
            {
                if (i == 0 || shown[i].rowBreak || rowCount[rowCount.Count - 1] == cols) rowCount.Add(0);
                rowOf[i] = rowCount.Count - 1;
                rowCount[rowCount.Count - 1]++;
            }
            int rows = rowCount.Count;

            // Rows that start a new group (a row break, e.g. the scene buttons) get extra space and a divider.
            var groupStart = new bool[rows];
            int groupBreaks = 0;
            for (int i = 1; i < shown.Count; i++)
                if (shown[i].rowBreak && !groupStart[rowOf[i]]) { groupStart[rowOf[i]] = true; groupBreaks++; }

            float stepX = buttonWidth + buttonGap, stepY = buttonSize + buttonGap;
            float w = cols * stepX + buttonGap;
            float h = TitleSpace + rows * stepY + buttonGap + groupBreaks * GroupSpace;
            const float centreY = -0.006f;
            float top = centreY + h * 0.5f;

            var panel = _menu.Find("Panel");
            panel.localScale = new Vector3(w, h, 1f);
            panel.localPosition = new Vector3(0f, centreY, 0.002f);
            _panelMat.SetVector("_Size", new Vector4(w, h, 0, 0));
            _title.rectTransform.sizeDelta = new Vector2(w - 0.01f, TitleSpace);
            _title.rectTransform.localPosition = new Vector3(0f, top - TitleSpace * 0.55f, -0.001f);

            // Top edge of each row, walking down from under the title.
            var rowTop = new float[rows];
            float y = top - TitleSpace;
            int dividerCount = 0;
            for (int r = 0; r < rows; r++)
            {
                if (groupStart[r])
                {
                    var d = GetDivider(dividerCount++);
                    float dw = w - 0.024f, dh = 0.0008f;
                    d.localScale = new Vector3(dw, dh, 1f);
                    d.localPosition = new Vector3(0f, y - (GroupSpace + buttonGap) * 0.5f, 0.001f);   // midway between the groups
                    _dividerMats[dividerCount - 1].SetVector("_Size", new Vector4(dw, dh, 0, 0));
                    y -= GroupSpace;
                }
                rowTop[r] = y;
                y -= stepY;
            }
            for (int i = 0; i < _dividers.Count; i++) _dividers[i].gameObject.SetActive(i < dividerCount);

            for (int i = 0, c = 0; i < shown.Count; i++, c++)
            {
                int r = rowOf[i];
                if (i > 0 && rowOf[i - 1] != r) c = 0;
                float x = (c - (rowCount[r] - 1) * 0.5f) * stepX;   // short rows are centred
                shown[i].root.localPosition = new Vector3(x, rowTop[r] - buttonGap - buttonSize * 0.5f, 0f);
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
            // Own material instance, drawn after the plates: with the font's default queue (Transparent)
            // the plates (Transparent+10) were drawn over the labels and washed them out.
            t.fontMaterial.renderQueue = TextQueue;
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
                if (!IsVisible(b)) { b.pressed = b.hovered = false; continue; }
                bool on = b.isOn != null && b.isOn();
                float depth = 0f;
                bool hover = false;
                if (interactive && hasTip)
                {
                    var local = b.root.InverseTransformPoint(tip);   // -z = towards the user
                    float halfX = buttonWidth * 0.5f + 0.004f, halfY = buttonSize * 0.5f + 0.004f;
                    bool inside = Mathf.Abs(local.x) < halfX && Mathf.Abs(local.y) < halfY;
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
                var fill = on ? Color.Lerp(ButtonOn, ButtonOnHover, b.hoverGlow)
                              : Color.Lerp(ButtonOff, ButtonOffHover, b.hoverGlow);
                b.mat.SetColor("_Color", fill);
                b.mat.SetColor("_BorderColor", Color.Lerp(BorderIdle, BorderHover, b.hoverGlow));
                var label = on ? LabelOn : LabelOff;
                label.a = b.text.alpha;   // keep the fade
                b.text.color = label;
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
            foreach (var m in _dividerMats) m.SetFloat("_Opacity", eased);
            _title.alpha = eased * 0.8f;
        }
    }
}
