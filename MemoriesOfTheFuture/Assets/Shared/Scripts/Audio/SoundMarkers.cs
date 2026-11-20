using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Memories.Audio
{
    /// <summary>
    /// Shows where every spatial sound is, in the game view (the desktop app has no Scene view): a
    /// ball at each sound with its name and level above it. The ball lights up and swells while the
    /// sound is playing, and is a dim grey dot while it's silent. Sounds that play in your ears
    /// (not spatial) have no position, so they get no marker. Toggled by <see cref="Visible"/>
    /// (O in the desktop app). Logs are prefixed [SoundMarkers].
    /// </summary>
    public class SoundMarkers : MonoBehaviour
    {
        [Tooltip("Unlit material for the balls (a copy is made per marker). Empty: URP Unlit.")]
        [SerializeField] Material material;
        [Tooltip("Font for the labels. Empty: TextMesh Pro's default.")]
        [SerializeField] TMP_FontAsset font;
        [SerializeField] bool visible;
        [SerializeField] Color playingColour = new Color(0.25f, 0.85f, 1f);
        [SerializeField] Color silentColour = new Color(0.45f, 0.45f, 0.5f);

        public bool Visible
        {
            get => visible;
            set { visible = value; Debug.Log($"[SoundMarkers] {(value ? "Shown" : "Hidden")}"); }
        }

        class Marker
        {
            public Transform root, ball;
            public Material mat;
            public TextMeshPro label;
        }

        readonly Dictionary<SlotVoice, Marker> _markers = new Dictionary<SlotVoice, Marker>();
        readonly List<SlotVoice> _gone = new List<SlotVoice>();
        Transform _parent;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            _parent = new GameObject("Sound Markers").transform;
            _parent.SetParent(transform, false);
        }

        readonly List<(Marker m, Vector3 pos, float k)> _shown = new List<(Marker, Vector3, float)>();

        void LateUpdate()
        {
            _parent.gameObject.SetActive(visible);
            var cam = Camera.main;
            if (!visible || cam == null) return;
            var eye = cam.transform.position;

            _shown.Clear();
            foreach (var v in SlotVoice.All)
            {
                var src = v.Source;
                bool spatial = src.spatialBlend > 0.5f;
                if (!_markers.TryGetValue(v, out var m))
                {
                    if (!spatial) continue;
                    m = _markers[v] = Make(v);
                }
                var pos = src.transform.position;
                float dist = Vector3.Distance(eye, pos);
                bool show = spatial && src.gameObject.activeInHierarchy && dist > 0.3f;
                if (m.root.gameObject.activeSelf != show) m.root.gameObject.SetActive(show);
                if (!show) continue;

                bool playing = src.isPlaying && src.volume > 0.001f;
                float loud = playing ? Mathf.Sqrt(src.volume) : 0f;
                // Balls and labels keep a readable size on screen however far away the sound is.
                float k = Mathf.Clamp(dist * 0.6f, 0.4f, 3f);
                m.root.position = pos;
                m.ball.localScale = Vector3.one * (0.035f + 0.07f * loud) * Mathf.Max(1f, k * 0.6f);
                var c = playing ? playingColour : silentColour;
                m.mat.SetColor(BaseColorId, c);
                m.mat.color = c;

                // Silent built-in sounds are just a dot (they cluster in the mug); your own emitters always say their name.
                bool emitter = src.name.StartsWith("Sound - ");
                bool labelled = playing || emitter;
                if (m.label.gameObject.activeSelf != labelled) m.label.gameObject.SetActive(labelled);
                if (!labelled) continue;
                m.label.text = $"{LabelOf(v)}\n<size=70%>{(playing ? $"{SoundSlot.LinearToDb(src.volume):0} dB" : "silent")}</size>";
                m.label.color = playing ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                var lt = m.label.transform;
                lt.localScale = Vector3.one * k;
                lt.rotation = Quaternion.LookRotation(lt.position - eye, Vector3.up);

                // Stack labels of sounds in the same place (the mug holds several) instead of overprinting.
                int stack = 0;
                foreach (var o in _shown) if ((o.pos - pos).sqrMagnitude < 0.08f * 0.08f) stack++;
                lt.localPosition = Vector3.up * (0.05f + 0.04f * loud + 0.03f * k + stack * 0.2f * k);
                _shown.Add((m, pos, k));
            }

            // Voices whose object was destroyed.
            _gone.Clear();
            foreach (var kv in _markers) if (kv.Key.Source == null) _gone.Add(kv.Key);
            foreach (var v in _gone) { if (_markers[v].root != null) Destroy(_markers[v].root.gameObject); _markers.Remove(v); }
        }

        /// <summary>An emitter's own name; otherwise the slot's, with a number where it plays from several places (Whisper 0, Whisper 1).</summary>
        static string LabelOf(SlotVoice v)
        {
            string voice = v.Source.name;
            if (voice.StartsWith("Sound - ")) return voice.Substring(8);
            char last = voice.Length > 0 ? voice[voice.Length - 1] : ' ';
            return char.IsDigit(last) ? $"{v.Slot.name} {last}" : v.Slot.name;
        }

        Marker Make(SlotVoice v)
        {
            var m = new Marker { root = new GameObject("Marker " + v.Slot.name).transform };
            m.root.SetParent(_parent, false);

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.name = "Ball";
            m.ball = ball.transform;
            m.ball.SetParent(m.root, false);
            m.mat = material != null ? new Material(material) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var r = ball.GetComponent<Renderer>();
            r.sharedMaterial = m.mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var lg = new GameObject("Label");
            lg.transform.SetParent(m.root, false);
            m.label = lg.AddComponent<TextMeshPro>();
            if (font != null) m.label.font = font;
            m.label.fontSize = 0.5f;
            m.label.alignment = TextAlignmentOptions.Bottom;
            m.label.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
            m.label.rectTransform.pivot = new Vector2(0.5f, 0f);
            m.label.textWrappingMode = TextWrappingModes.NoWrap;
            m.label.overflowMode = TextOverflowModes.Overflow;
            m.label.outlineWidth = 0.2f;
            m.label.outlineColor = new Color32(0, 0, 0, 200);
            return m;
        }
    }
}
