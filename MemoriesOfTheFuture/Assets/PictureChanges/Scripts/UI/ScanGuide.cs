using TMPro;
using UnityEngine;

namespace PictureChanges.UI
{
    /// <summary>
    /// The visitor-facing side of finding the painting (no debug info):
    ///  1. Searching: a quiet prompt floats low in view: "Find the painting".
    ///  2. Seen: soft corner brackets appear around the real painting and close in as the
    ///     estimate settles (progress = samples collected).
    ///  3. Locked: the brackets brighten, then fade as the painting reveals itself; a gentle
    ///     invitation to step closer appears below it until the transformation starts.
    /// </summary>
    public class ScanGuide : MonoBehaviour
    {
        [SerializeField] BoardPoseEstimator estimator;
        [SerializeField] PictureCanvas canvas;
        [SerializeField] Transform head;
        [SerializeField] Material uiMaterial;
        [SerializeField] TMP_FontAsset font;
        [SerializeField] AudioClip lockSound;

        [Header("Prompt")]
        [SerializeField] string searchTitle = "Find the painting";
        [SerializeField] string searchSubtitle = "Stand about a metre away and look at it";
        [SerializeField] string approachText = "Step closer";
        [SerializeField] Vector3 promptOffset = new Vector3(0f, -0.18f, 0.9f);

        [Header("Brackets")]
        [SerializeField] Color bracketColour = new Color(1f, 0.92f, 0.78f, 1f);
        [SerializeField] float bracketWidth = 0.004f;

        Transform _prompt;
        TextMeshPro _title, _subtitle;
        float _promptAlpha;

        Transform _brackets;
        LineRenderer[] _lines = new LineRenderer[4];
        Material _lineMat;
        float _bracketAlpha, _bracketProgress, _lockFlash;
        BoardPoseEstimator.BoardPose _lastPose;
        bool _hasPose, _wasLocked;

        Transform _approach;
        TextMeshPro _approachText;
        float _approachAlpha;
        AudioSource _audio;

        void Start()
        {
            _prompt = new GameObject("ScanPrompt").transform;
            _prompt.SetParent(transform, false);
            _title = MakeText(_prompt, searchTitle, 0.026f, new Vector3(0f, 0.012f, 0f), FontStyles.Normal);
            _subtitle = MakeText(_prompt, searchSubtitle, 0.012f, new Vector3(0f, -0.022f, 0f), FontStyles.Italic);
            if (head != null) _prompt.SetPositionAndRotation(PromptPose().position, PromptPose().rotation);

            _brackets = new GameObject("ScanBrackets").transform;
            _brackets.SetParent(transform, false);
            _lineMat = new Material(uiMaterial);
            _lineMat.SetColor("_BorderColor", new Color(0, 0, 0, 0));
            _lineMat.SetFloat("_Radius", 0f);
            _lineMat.SetFloat("_Border", 0f);
            _lineMat.SetVector("_Size", new Vector4(1, 1, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Bracket" + i);
                go.transform.SetParent(_brackets, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _lineMat;
                lr.useWorldSpace = false;
                lr.positionCount = 3;
                lr.widthMultiplier = bracketWidth;
                lr.numCapVertices = 4;
                lr.numCornerVertices = 4;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _lines[i] = lr;
            }

            _approach = new GameObject("ApproachPrompt").transform;
            _approach.SetParent(transform, false);
            _approachText = MakeText(_approach, approachText, 0.024f, Vector3.zero, FontStyles.Italic);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.volume = 0.5f;
        }

        TextMeshPro MakeText(Transform parent, string s, float capHeight, Vector3 pos, FontStyles style)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.text = s;
            t.fontSize = TextSize.ForCapHeight(capHeight);
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(1f, 0.1f);
            t.color = new Color(1f, 0.97f, 0.92f, 1f);
            t.outlineWidth = 0.15f;
            t.outlineColor = new Color32(0, 0, 0, 120);
            return t;
        }

        Pose PromptPose()
        {
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            var pos = head.position + yaw * promptOffset;
            return new Pose(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
        }

        void Update()
        {
            if (estimator == null || head == null) return;
            bool locked = estimator.IsLocked;
            bool seenRecently = estimator.LastSample.HasValue && Time.time - estimator.LastSampleTime < 0.6f;

            // 1. Prompt: visible while nothing is seen and nothing is locked.
            float promptTarget = !locked && !estimator.SuspendDetection && !seenRecently && !(_hasPose && _bracketAlpha > 0.05f) ? 1f : 0f;
            _promptAlpha = Mathf.MoveTowards(_promptAlpha, promptTarget, Time.deltaTime * (promptTarget > 0 ? 0.8f : 3f));
            if (_promptAlpha > 0f)
            {
                var target = PromptPose();
                float t = 1f - Mathf.Exp(-2.5f * Time.deltaTime);   // lazy follow: calm, not head-locked
                _prompt.SetPositionAndRotation(Vector3.Lerp(_prompt.position, target.position, t),
                                               Quaternion.Slerp(_prompt.rotation, target.rotation, t));
            }
            _prompt.gameObject.SetActive(_promptAlpha > 0.001f);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.6f);
            _title.alpha = _promptAlpha * pulse;
            _subtitle.alpha = _promptAlpha * 0.75f;

            // 2. Brackets around the painting while the estimate settles.
            if (locked && estimator.LockedPose.HasValue) { _lastPose = estimator.LockedPose.Value; _hasPose = true; }
            else if (seenRecently) { _lastPose = estimator.LastSample.Value; _hasPose = true; }

            if (locked && !_wasLocked)
            {
                _lockFlash = 1f;
                if (lockSound != null) { _audio.transform.position = _lastPose.centre; _audio.PlayOneShot(lockSound); }
            }
            if (!locked && _wasLocked) { _bracketProgress = 0f; _hasPose = false; }
            _wasLocked = locked;

            float progress = locked ? 1f : estimator.SamplesNeeded > 0 ? Mathf.Clamp01(estimator.SampleCount / (float)estimator.SamplesNeeded) : 0f;
            _bracketProgress = Mathf.Lerp(_bracketProgress, progress, 1f - Mathf.Exp(-6f * Time.deltaTime));
            _lockFlash = Mathf.MoveTowards(_lockFlash, 0f, Time.deltaTime * 0.6f);

            float bracketTarget = locked ? (_lockFlash > 0.01f ? 1f : 0f) : (seenRecently ? 1f : 0f);
            _bracketAlpha = Mathf.MoveTowards(_bracketAlpha, bracketTarget, Time.deltaTime * (bracketTarget > 0 ? 2.5f : 1.2f));
            _brackets.gameObject.SetActive(_hasPose && _bracketAlpha > 0.001f);
            if (_hasPose && _bracketAlpha > 0.001f) DrawBrackets(pulse);

            // 3. After the lock, invite the visitor to step closer, until the movie starts.
            bool invite = locked && !canvas.HasPlayed && _lockFlash < 0.6f &&
                          Vector3.Distance(head.position, _lastPose.centre) > canvas.TriggerDistance * 0.9f;
            _approachAlpha = Mathf.MoveTowards(_approachAlpha, invite ? 0.85f : 0f, Time.deltaTime * (invite ? 0.7f : 2f));
            _approach.gameObject.SetActive(_approachAlpha > 0.001f);
            if (_approachAlpha > 0f)
            {
                var c = estimator.layout.PaintingCorners;
                var below = _lastPose.centre + _lastPose.rotation * new Vector3(0f, Mathf.Min(c[2].y, c[3].y) - 0.045f, -0.01f);
                _approach.SetPositionAndRotation(below, Quaternion.LookRotation(below - head.position, Vector3.up));
                _approachText.alpha = _approachAlpha * (0.8f + 0.2f * Mathf.Sin(Time.time * 1.3f));
            }
        }

        void DrawBrackets(float pulse)
        {
            _brackets.SetPositionAndRotation(_lastPose.centre, _lastPose.rotation);
            var c = estimator.layout.PaintingCorners;   // TL, TR, BR, BL
            var centre = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            // Brackets start loose (outside the painting) and close in onto its corners.
            float spread = Mathf.Lerp(0.05f, 0.008f, _bracketProgress);
            float arm = Mathf.Lerp(0.02f, 0.045f, _bracketProgress);
            for (int i = 0; i < 4; i++)
            {
                var corner = c[i] + (c[i] - centre).normalized * spread;
                var toPrev = (c[(i + 3) % 4] - c[i]).normalized;
                var toNext = (c[(i + 1) % 4] - c[i]).normalized;
                var lr = _lines[i];
                lr.SetPosition(0, V(corner + toPrev * arm));
                lr.SetPosition(1, V(corner));
                lr.SetPosition(2, V(corner + toNext * arm));
                lr.widthMultiplier = bracketWidth * (1f + _lockFlash * 0.8f);
            }
            var col = bracketColour;
            col.a = _bracketAlpha * Mathf.Lerp(0.45f + 0.25f * pulse, 1f, Mathf.Max(_bracketProgress * 0.6f, _lockFlash));
            _lineMat.SetColor("_Color", col);
        }

        static Vector3 V(Vector2 p) => new Vector3(p.x, p.y, -0.004f);
    }

    /// <summary>TextMeshPro world-text sizing: fontSize for a given cap height in metres.</summary>
    public static class TextSize
    {
        // Measured in the editor for TMP world text at scale 1 (see HandMenu / ScanGuide setup).
        public static float CapHeightPerFontSize = 0.0763f;   // Inter Regular SDF, measured
        public static float ForCapHeight(float metres) => metres / CapHeightPerFontSize;
    }
}
