using UnityEngine;

namespace Memories.Audio
{
    /// <summary>
    /// An AudioSource playing a <see cref="SoundSlot"/>. Gameplay code sets <see cref="Level"/> (0..1,
    /// e.g. how close the hand is) and <see cref="PitchScale"/>, and calls <see cref="Tick"/> each frame;
    /// the slot supplies the clip, loudness, pitch, mixer group and spatial settings, so the sound
    /// designers' edits apply live while the game runs.
    /// </summary>
    public sealed class SlotVoice
    {
        public SoundSlot Slot { get; }
        public AudioSource Source { get; }
        /// <summary>0..1, set by gameplay code. Multiplied by the slot's volume.</summary>
        public float Level;
        /// <summary>Multiplied by the slot's pitch.</summary>
        public float PitchScale = 1f;

        public bool HasSound => Slot.HasClips;
        public static bool SlotHasSound(SoundSlot s) => s != null && s.HasClips;

        int _version = int.MinValue;

        SlotVoice(SoundSlot slot, AudioSource source) { Slot = slot; Source = source; }

        /// <summary>Make a child GameObject with an AudioSource set up from the slot. Silent until played.</summary>
        public static SlotVoice Create(SoundSlot slot, Transform parent, string name, bool loop)
        {
            if (slot == null)
            {
                Debug.LogWarning($"[Sound] '{name}' on {parent.name} has no Sound Slot assigned, so it will be silent. " +
                                 "Assign one in the Inspector (Assets/Audio/Slots).");
                slot = SoundSlot.CreateEmpty(name);
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.volume = 0f;
            a.clip = slot.PickClip();
            var v = new SlotVoice(slot, a);
            v.Tick();
            slot.Register(v);
            return v;
        }

        /// <summary>Start playing (a one-shot picks its next file each time). Returns false if there is nothing to play.</summary>
        public bool Play()
        {
            if (!Source.loop || Source.clip == null || !Slot.Contains(Source.clip) ||
                (Slot.RuntimeOverride != null && Source.clip != Slot.RuntimeOverride))
                Source.clip = Slot.PickClip();
            if (Source.clip == null) return false;
            Tick();
            Source.Play();
            return true;
        }

        public void Stop() { if (Source != null) Source.Stop(); }

        /// <summary>Apply the slot's settings and this voice's level. Call once per frame.</summary>
        public void Tick()
        {
            if (Source == null) return;
            if (_version != Slot.Version)
            {
                _version = Slot.Version;
                Slot.Configure(Source);
                SwapLoopIfFilesChanged();
            }
            Source.volume = Slot.Volume * Mathf.Clamp01(Level);
            Source.pitch = Slot.pitch * PitchScale;
        }

        /// <summary>A loop keeps playing its file; if the designers drop a new one in (or remove it, or load one from
        /// the desktop sounds folder), switch over live.</summary>
        void SwapLoopIfFilesChanged()
        {
            if (!Source.loop) return;
            var want = Slot.RuntimeOverride != null || Slot.variation == SoundSlot.Variation.NewestFirst ? Slot.FirstClip :
                       Slot.Contains(Source.clip) ? Source.clip : Slot.PickClip();
            if (want == Source.clip) return;
            bool playing = Source.isPlaying;
            Source.clip = want;
            if (playing && want != null) Source.Play();
        }
    }
}
