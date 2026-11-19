using UnityEngine;
using UnityEngine.Audio;

namespace CupSpike
{
    /// <summary>
    /// Blends the Audio Mixer between one snapshot per phase of the experience, so the sound
    /// designers can shape each phase in the Audio Mixer window without code:
    ///  - Room: nothing happening.
    ///  - Approach: a hand nearing the mug (follows MugScream.Approach).
    ///  - LookingIn: looking down into the open mug (follows MugInterior.Reveal).
    ///  - Dream: the dream (follows DreamState.Depth).
    /// Weights are mixed smoothly, so e.g. half-way into the dream is half Dream, half LookingIn.
    /// A snapshot missing from the mixer is skipped. Untick Blending to mix a single snapshot by hand.
    /// </summary>
    public class MixDirector : MonoBehaviour
    {
        public static readonly string[] SnapshotNames = { "Room", "Approach", "LookingIn", "Dream" };

        [SerializeField] AudioMixer mixer;
        [Tooltip("Untick while you adjust a snapshot in the Audio Mixer window, so the game doesn't keep blending away from it.")]
        public bool blending = true;
        [Tooltip("How quickly the mix follows the experience (s).")]
        [SerializeField] float smoothing = 0.1f;

        /// <summary>The director in the running scene, if any (for the Sound Design window).</summary>
        public static MixDirector Current { get; private set; }
        public AudioMixer Mixer => mixer;
        /// <summary>Current weight of each snapshot in <see cref="SnapshotNames"/> order (0 where the snapshot is missing).</summary>
        public float[] Weights { get; } = new float[SnapshotNames.Length];

        MugScream _scream;
        MugInterior _interior;
        DreamState _dream;
        AudioMixerSnapshot[] _snapshots;
        int[] _index;
        float[] _sent;

        void OnEnable() => Current = this;
        void OnDisable() { if (Current == this) Current = null; }

        void Start()
        {
            _scream = FindFirstObjectByType<MugScream>();
            _interior = FindFirstObjectByType<MugInterior>();
            _dream = FindFirstObjectByType<DreamState>();
            if (mixer == null) { Debug.LogWarning("[MixDirector] No mixer assigned"); enabled = false; return; }

            var found = new System.Collections.Generic.List<AudioMixerSnapshot>();
            var index = new System.Collections.Generic.List<int>();
            for (int i = 0; i < SnapshotNames.Length; i++)
            {
                var s = mixer.FindSnapshot(SnapshotNames[i]);
                if (s == null) { Debug.LogWarning($"[MixDirector] Mixer has no '{SnapshotNames[i]}' snapshot; skipping it"); continue; }
                found.Add(s);
                index.Add(i);
            }
            _snapshots = found.ToArray();
            _index = index.ToArray();
            _sent = new float[_snapshots.Length];
            Debug.Log($"[MixDirector] Blending {_snapshots.Length} snapshots on {mixer.name}");
        }

        void Update()
        {
            if (_snapshots == null || _snapshots.Length == 0) return;

            float dream = _dream != null ? _dream.Depth : 0f;
            float look = _interior != null ? _interior.Reveal : 0f;
            float approach = _scream != null ? _scream.Approach : 0f;
            float rest = 1f - dream;
            Weights[3] = dream;
            Weights[2] = rest * look;
            rest *= 1f - look;
            Weights[1] = rest * approach;
            Weights[0] = rest * (1f - approach);

            if (!blending) return;
            var w = new float[_snapshots.Length];
            bool changed = false;
            float total = 0f;
            for (int i = 0; i < w.Length; i++) { w[i] = Weights[_index[i]]; total += w[i]; }
            if (total <= 0f) w[0] = total = 1f;   // e.g. only Dream exists and we're awake
            for (int i = 0; i < w.Length; i++) { w[i] /= total; changed |= Mathf.Abs(w[i] - _sent[i]) > 0.005f; }
            if (!changed) return;
            mixer.TransitionToSnapshots(_snapshots, w, smoothing);
            _sent = w;
        }
    }
}
