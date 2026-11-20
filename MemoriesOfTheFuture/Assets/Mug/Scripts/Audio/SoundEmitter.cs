using Memories.Audio;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// Plays a Sound Slot from a point in the scene, at a moment you pick, with no code. Add one
    /// with Memories > Sound Design > "Add sound to scene", move it where the sound should come
    /// from, pick a slot and when it plays.
    ///
    /// Continuous moments (Always, While...) loop the sound and fade it with the moment: e.g.
    /// While Hand Approaches gets louder as the hand nears the mug. One-shot moments (When...)
    /// play the sound once each time it happens.
    ///
    /// Examples in 03-DesktopTesting, under "Sound Examples"; the step-by-step guide is
    /// Assets/Audio/README - Adding Spatial Sound.md.
    /// </summary>
    public class SoundEmitter : MonoBehaviour
    {
        public enum When
        {
            [Tooltip("Loops from the start.")] Always,
            [Tooltip("Loops, louder as a hand nears the mug.")] WhileHandApproaches,
            [Tooltip("Loops, fades in as the mug opens when you look into it.")] WhileLookingIn,
            [Tooltip("Loops, fades in as the dream deepens.")] WhileDreaming,
            [Tooltip("Plays once each time the mug screams.")] WhenMugScreams,
            [Tooltip("Plays once each time you wake from the dream.")] WhenWaking,
            [Tooltip("Loops, fades in as the mug is held (the dread before the dream) and stays through the dream.")] WhileHoldingMug,
            [Tooltip("Loops, fades in as you walk within Near Distance of this sound.")] WhileNear,
        }

        public enum Where
        {
            [Tooltip("Stays where this object is in the scene.")] HereInTheScene,
            [Tooltip("Follows the real mug (this object's position is ignored; use Offset).")] OnTheMug,
            [Tooltip("Follows your head, at Offset from it (turns with you).")] OnTheListener,
        }

        [Tooltip("The sound to play (Assets/Audio/Slots).")]
        public SoundSlot slot;
        public When when = When.Always;
        public Where where = Where.HereInTheScene;
        [Tooltip("Offset from the mug or the listener (m). For OnTheMug, +Y is up the mug.")]
        public Vector3 offset;
        [Tooltip("Fade time for the continuous moments (s).")]
        [Min(0f)] public float fade = 0.5f;
        [Tooltip("While Near only: full level within half this distance, silent beyond it (m).")]
        [Min(0.1f)] public float nearDistance = 1.5f;

        SlotVoice _voice;
        MugScream _scream;
        MugInterior _interior;
        DreamState _dream;
        CupDetector _detector;
        bool _wasWaking;

        bool Continuous => when <= When.WhileDreaming || when == When.WhileHoldingMug || when == When.WhileNear;

        void Start()
        {
            _scream = FindAnyObjectByType<MugScream>();
            _interior = FindAnyObjectByType<MugInterior>();
            _dream = FindAnyObjectByType<DreamState>();
            _detector = FindAnyObjectByType<CupDetector>();
            // Named after this object, so the sound markers and logs say which sound it is.
            _voice = SlotVoice.Create(slot, transform, name, Continuous);
            if (Continuous) _voice.Play();
            else _voice.Level = 1f;
            if (when == When.WhenMugScreams && _scream != null) _scream.onScream.AddListener(PlayOnce);
        }

        void OnDestroy()
        {
            if (_scream != null) _scream.onScream.RemoveListener(PlayOnce);
        }

        public void PlayOnce()
        {
            if (_voice == null) return;
            _voice.Stop();
            _voice.Play();
        }

        void Update()
        {
            if (_voice == null) return;
            Place();

            if (Continuous)
            {
                float target = when switch
                {
                    When.WhileHandApproaches => _scream != null ? _scream.Approach : 0f,
                    When.WhileLookingIn => _interior != null ? _interior.Reveal : 0f,
                    When.WhileDreaming => _dream != null ? _dream.Depth : 0f,
                    When.WhileHoldingMug => _dream != null ? _dream.Dread : 0f,
                    When.WhileNear => Near(),
                    _ => 1f,
                };
                _voice.Level = fade > 0f ? Mathf.MoveTowards(_voice.Level, target, Time.deltaTime / fade) : target;
                if (!_voice.Source.isPlaying && _voice.HasSound) _voice.Play();
            }
            else if (when == When.WhenWaking && _dream != null)
            {
                if (_dream.Waking && !_wasWaking) PlayOnce();
                _wasWaking = _dream.Waking;
            }
            _voice.Tick();
        }

        float Near()
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            float d = Vector3.Distance(cam.transform.position, _voice.Source.transform.position);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(nearDistance, nearDistance * 0.5f, d));
        }

        void Place()
        {
            var t = _voice.Source.transform;
            switch (where)
            {
                case Where.OnTheMug:
                    if (_detector != null && _detector.Tracking)
                        t.position = _detector.CupPose.position + _detector.CupPose.rotation * offset;
                    break;
                case Where.OnTheListener:
                    var cam = Camera.main;
                    if (cam != null) t.position = cam.transform.TransformPoint(offset);
                    break;
                default:
                    t.localPosition = Vector3.zero;
                    break;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            Gizmos.DrawSphere(transform.position, 0.03f);
        }

        void OnDrawGizmosSelected()
        {
            if (slot == null || !slot.spatial) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, slot.minDistance);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, slot.maxDistance);
            if (when == When.WhileNear)
            {
                Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.6f);
                Gizmos.DrawWireSphere(transform.position, nearDistance);
            }
        }
    }
}
