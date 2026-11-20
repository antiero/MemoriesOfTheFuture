using Memories.Audio;
using UnityEngine;

namespace PictureChanges
{
    /// <summary>
    /// The "unseen swap" trigger mode: the painting is never caught in the act; it is simply
    /// different each time you check (concept board 01, "look away, look back: it moved").
    ///
    /// A cycle:
    ///  1. Seen: the visitor faces the painting (ViewerPresence.IsFacing) while it is in view,
    ///     for seenDwell seconds. That proves they have looked at the current stage. Armed.
    ///  2. Unseen: every point of the painting is outside the head's view cone (with a margin past
    ///     the Quest 3S's ~96 x 90 degree field of view, so peripheral vision doesn't catch it)
    ///     for unseenTime seconds.
    ///  3. Swap: a hard cut to the next stage, with no blend, because nobody is watching. The
    ///     swap sound (the scream, by default only on the final swap) plays from the painting,
    ///     which is now behind or beside the visitor.
    ///
    /// Distance: unless the visitor has come within noticeDistance since the last swap, the
    /// painting goes no further than farMaxStage (the board's "1.5 m: it notices you"). The last
    /// stage holds until Replay.
    ///
    /// Stage 0 is PictureCanvas's still; `stages` holds stages 1..N. Only runs while PictureCanvas
    /// is in the UnseenSwap trigger mode. Logs are prefixed [Stages].
    /// The Quest 3S has no eye tracking, so "seen" and "unseen" are based on the head direction.
    /// </summary>
    public class PaintingStages : MonoBehaviour
    {
        [SerializeField] PictureCanvas canvas;
        [SerializeField] ViewerPresence presence;
        [SerializeField] Transform head;
        [Tooltip("Stages 1..N, in order. Stage 0 is the canvas's still image.")]
        [SerializeField] Texture[] stages;

        [Header("Seen")]
        [Tooltip("Seconds of facing the painting before it may change again.")]
        [SerializeField] float seenDwell = 0.75f;

        [Header("Unseen (head-space half angles, degrees)")]
        [Tooltip("Horizontal half angle beyond which a point counts as out of view. Quest 3S is about 48; the rest is margin.")]
        [SerializeField] float hiddenHalfAngleH = 60f;
        [Tooltip("Vertical half angle beyond which a point counts as out of view. Quest 3S is about 45; the rest is margin.")]
        [SerializeField] float hiddenHalfAngleV = 55f;
        [Tooltip("Seconds the whole painting must stay out of view before it swaps.")]
        [SerializeField] float unseenTime = 1.2f;

        [Header("Distance")]
        [Tooltip("The visitor must come this close (m) since the last swap for the painting to go past farMaxStage.")]
        [SerializeField] float noticeDistance = 1.5f;
        [Tooltip("Highest stage reachable without coming close.")]
        [SerializeField] int farMaxStage = 1;

        [Header("Sound")]
        [Tooltip("Played from the painting when it swaps, so it comes from behind or beside the visitor. " +
                 "Its file, level and space live in the Sound Slot (Memories > Sound Design).")]
        [SerializeField] SoundSlot swapSlot;
        [Tooltip("Play the swap sound only on the swap to the last stage (the scream). Off: on every swap.")]
        [SerializeField] bool swapSoundOnlyOnLast = true;

        public int Stage { get; private set; }
        public int StageCount => (stages != null ? stages.Length : 0) + 1;
        public bool Armed { get; private set; }
        public bool InView { get; private set; }
        public bool CameClose { get; private set; }
        public float SeenProgress => Mathf.Clamp01(_seen / Mathf.Max(0.01f, seenDwell));
        public float UnseenProgress => Mathf.Clamp01(_unseen / Mathf.Max(0.01f, unseenTime));
        public int Swaps { get; private set; }

        float _seen, _unseen;
        readonly Vector3[] _points = new Vector3[9];
        SlotVoice _swap;

        /// <summary>The swap sound's voice (desktop app: replay it from the panel).</summary>
        public SlotVoice SwapVoice => _swap;
        public float SeenDwell { get => seenDwell; set => seenDwell = Mathf.Max(0f, value); }
        public float UnseenTime { get => unseenTime; set => unseenTime = Mathf.Max(0f, value); }

        void Start()
        {
            _swap = SlotVoice.Create(swapSlot, transform, "SwapSound", false);
            _swap.Level = 1f;
        }

        /// <summary>Back to stage 0 (Replay, Rescan, mode change).</summary>
        public void ResetStages()
        {
            Stage = 0;
            Armed = CameClose = false;
            _swap?.Stop();
            _seen = _unseen = 0f;
            Swaps = 0;
        }

        void Update()
        {
            _swap?.Tick();
            if (canvas == null || presence == null || head == null) return;
            // Animate (hand menu) took over with the video: no swaps over it until Reset.
            if (canvas.Mode != PictureCanvas.TriggerMode.UnseenSwap || canvas.Animated || !canvas.Revealed || !presence.Active)
            {
                _seen = _unseen = 0f;
                InView = false;
                return;
            }

            InView = PaintingInView();
            if (presence.Distance < noticeDistance) CameClose = true;

            if (!Armed)
            {
                _seen = presence.IsFacing && InView ? _seen + Time.deltaTime : 0f;
                if (_seen >= seenDwell)
                {
                    Armed = true;
                    _seen = 0f;
                    Debug.Log($"[Stages] Seen at stage {Stage} ({presence.Distance:F2} m). Armed.");
                }
                return;
            }

            _unseen = InView ? 0f : _unseen + Time.deltaTime;
            if (_unseen >= unseenTime) Swap();
        }

        void Swap()
        {
            int last = StageCount - 1;
            int cap = CameClose ? last : Mathf.Min(farMaxStage, last);
            Armed = false;
            _unseen = 0f;
            if (Stage >= cap)
            {
                Debug.Log(Stage >= last ? $"[Stages] Out of view, holding the last stage {Stage}"
                                        : $"[Stages] Out of view, held at stage {Stage}: visitor hasn't come within {noticeDistance} m");
                return;
            }

            Stage++;
            Swaps++;
            canvas.ShowStage(stages[Stage - 1]);
            bool sound = false;
            if (_swap != null && (!swapSoundOnlyOnLast || Stage == last))
            {
                _swap.Source.transform.position = canvas.PaintingTransform.position;
                sound = _swap.Play();
            }
            Debug.Log($"[Stages] SWAP to stage {Stage}/{last} while unseen ({presence.Distance:F2} m, " +
                      $"{presence.Angle:F0} deg, came close: {CameClose}){(sound ? ", sound: " + _swap.Source.clip.name : "")}");
            CameClose = presence.Distance < noticeDistance;
        }

        /// <summary>True if any of 9 points across the painting (corners, edge midpoints, centre) is inside the view cone.</summary>
        bool PaintingInView()
        {
            var t = canvas.PaintingTransform;
            var mesh = canvas.PaintingMesh;
            if (t == null) return false;
            if (mesh != null)
            {
                var b = mesh.bounds;   // board-plane mesh: x/y extents, z ~ 0
                int k = 0;
                for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    _points[k++] = t.TransformPoint(new Vector3(Mathf.Lerp(b.min.x, b.max.x, i * 0.5f),
                                                                Mathf.Lerp(b.min.y, b.max.y, j * 0.5f), 0f));
            }
            else
            {
                for (int k = 0; k < _points.Length; k++) _points[k] = t.position;
            }

            foreach (var p in _points)
            {
                var d = head.InverseTransformPoint(p);
                if (d.z <= 0.01f) continue;   // behind the head
                float h = Mathf.Atan2(Mathf.Abs(d.x), d.z) * Mathf.Rad2Deg;
                float v = Mathf.Atan2(Mathf.Abs(d.y), d.z) * Mathf.Rad2Deg;
                if (h < hiddenHalfAngleH && v < hiddenHalfAngleV) return true;
            }
            return false;
        }
    }
}
