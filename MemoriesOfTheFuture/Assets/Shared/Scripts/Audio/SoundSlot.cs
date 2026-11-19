using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Memories.Audio
{
    /// <summary>
    /// One sound in the experience (the scream, the breath, the background bed...), as an asset the
    /// sound designers own: which files it plays, how loud, which mixer group, and how it sits in
    /// space. Code asks a slot for its sound; it never hard-codes clips or levels.
    ///
    /// Edits made while the game is running are kept (it is an asset, not a scene object), and they
    /// are heard straight away: every <see cref="SlotVoice"/> playing this slot picks them up.
    /// Open Memories > Sound Design for the overview of every slot.
    /// </summary>
    [CreateAssetMenu(menuName = "Memories/Sound Slot", fileName = "New Sound Slot", order = 0)]
    public class SoundSlot : ScriptableObject
    {
        public enum Variation
        {
            [Tooltip("Always play the top file. Dropping a new file puts it at the top.")] NewestFirst,
            [Tooltip("Pick a random file each time the sound starts.")] Random,
            [Tooltip("Go through the files in order, one per play.")] InOrder,
        }

        [Tooltip("Notes for the team: what this sound is for, when it plays.")]
        [TextArea(2, 6)] public string notes;

        [Header("Sound files")]
        [Tooltip("The files this slot can play. Drag files here, or drop them into Assets/Audio/Drop.")]
        public List<AudioClip> clips = new List<AudioClip>();
        public Variation variation = Variation.NewestFirst;
        [Tooltip("Files dropped into Assets/Audio/Drop whose name starts with this (e.g. 'scream' for " +
                 "scream_v2.wav) are added to this slot automatically. A folder with this name inside Drop works too.")]
        public string dropName;

        [Header("Level")]
        [Tooltip("Loudness in dB. 0 = the file as it is. Above 0 only works on spatial sounds (up to +20 dB); " +
                 "for more on a non-spatial sound, raise its mixer group.")]
        [Range(-60f, 20f)] public float volumeDb;
        [Tooltip("1 = original speed. 0.5 = an octave down, 2 = an octave up.")]
        [Range(0.1f, 3f)] public float pitch = 1f;
        [Tooltip("Which Audio Mixer group this plays through (Window > Audio > Audio Mixer).")]
        public AudioMixerGroup output;

        [Header("Space")]
        [Tooltip("On: the sound comes from a point in the room (3D, through the Meta spatialiser). " +
                 "Off: plain stereo in your ears, does not move when you turn your head.")]
        public bool spatial = true;
        [Tooltip("Full volume within this distance (m).")]
        [Min(0.01f)] public float minDistance = 0.3f;
        [Tooltip("Stops getting quieter beyond this distance (m).")]
        [Min(0.1f)] public float maxDistance = 15f;
        [Tooltip("How the level falls off between min and max distance.")]
        public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
        [Tooltip("Meta room acoustics (reflections and reverb from the scanned room) for this sound.")]
        public bool roomAcoustics = true;
        [Tooltip("Extra (or less) room reverb for this sound only, in dB.")]
        [Range(-60f, 20f)] public float reverbSendDb;

        // ------------------------------------------------------------ live state (not saved)

        /// <summary>The slot being soloed in the Sound Design window, if any.</summary>
        public static SoundSlot Soloed;
        [NonSerialized] public bool Muted;
        public bool Audible => !Muted && (Soloed == null || Soloed == this);

        /// <summary>Changes whenever a setting changes, so playing voices know to re-apply.</summary>
        public int Version => _version;
        [NonSerialized] int _version;
        [NonSerialized] int _next;
        [NonSerialized] readonly List<SlotVoice> _voices = new List<SlotVoice>();

        /// <summary>Voices currently built from this slot (for the Sound Design window).</summary>
        public IReadOnlyList<SlotVoice> Voices
        {
            get { _voices.RemoveAll(v => v == null || v.Source == null); return _voices; }
        }

        /// <summary>
        /// A file loaded from disk at runtime (the desktop app's sounds folder) that replaces this slot's
        /// files until it's cleared. Never saved into the asset.
        /// </summary>
        public AudioClip RuntimeOverride
        {
            get => _override;
            set { _override = value; Touch(); }
        }
        [NonSerialized] AudioClip _override;

        public bool HasClips { get { if (_override != null) return true; foreach (var c in clips) if (c != null) return true; return false; } }
        public AudioClip FirstClip { get { if (_override != null) return _override; foreach (var c in clips) if (c != null) return c; return null; } }

        /// <summary>The level an AudioSource should play at, 0..1 (silent while muted or another slot is soloed).</summary>
        public float Volume => Audible ? DbToLinear(Mathf.Min(volumeDb, 0f)) : 0f;

        /// <summary>Tell playing voices that something changed (called after edits made from code).</summary>
        public void Touch() => _version++;

        void OnValidate() => _version++;
        void OnEnable() { _next = 0; _version++; }

        public AudioClip PickClip()
        {
            if (_override != null) return _override;
            var valid = clips.FindAll(c => c != null);
            if (valid.Count == 0) return null;
            switch (variation)
            {
                case Variation.Random: return valid[UnityEngine.Random.Range(0, valid.Count)];
                case Variation.InOrder: return valid[_next++ % valid.Count];
                default: return valid[0];
            }
        }

        public bool Contains(AudioClip clip) => clip != null && (clip == _override || clips.Contains(clip));

        /// <summary>Move a file to the top of the list, so it's the one that plays (Newest First).</summary>
        public void MakeFirst(AudioClip clip)
        {
            if (clip == null) return;
            clips.Remove(clip);
            clips.Insert(0, clip);
            Touch();
        }

        /// <summary>Apply this slot's routing and space settings to a source (not volume or pitch: voices set those every frame).</summary>
        public void Configure(AudioSource a)
        {
            a.outputAudioMixerGroup = output;
            a.spatialize = spatial;
            a.spatialBlend = spatial ? 1f : 0f;
            a.minDistance = minDistance;
            a.maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);
            a.rolloffMode = rolloff;

            var meta = a.GetComponent<MetaXRAudioSource>();
            if (meta == null && spatial) meta = a.gameObject.AddComponent<MetaXRAudioSource>();
            if (meta != null)
            {
                meta.EnableSpatialization = spatial;
                meta.GainBoostDb = spatial ? Mathf.Max(0f, volumeDb) : 0f;
                meta.EnableAcoustics = roomAcoustics;
                meta.ReverbSendDb = reverbSendDb;
            }
        }

        internal void Register(SlotVoice v) { if (!_voices.Contains(v)) _voices.Add(v); }

        /// <summary>A silent stand-in for a slot that wasn't assigned, so code never has to null-check.</summary>
        public static SoundSlot CreateEmpty(string name)
        {
            var s = CreateInstance<SoundSlot>();
            s.name = name + " (no slot assigned)";
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        public static float DbToLinear(float db) => db <= -60f ? 0f : Mathf.Pow(10f, db / 20f);
        public static float LinearToDb(float lin) => lin <= 0.001f ? -60f : 20f * Mathf.Log10(lin);
    }
}
