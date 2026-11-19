using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Memories.Audio
{
    /// <summary>
    /// The sound mix as a file outside the project, so a mix made in the desktop app (no Unity) can
    /// be brought back into the Sound Slot assets: Memories > Audio > Import Mix From Desktop App.
    ///  - ~/MemoriesOfTheFuture/SoundMix.json : every slot's level, pitch and space settings.
    ///  - ~/MemoriesOfTheFuture/Sounds/       : sound files that replace a slot's files in the app
    ///    (a file whose name starts with the slot's drop name, e.g. scream_take3.wav).
    /// </summary>
    public static class SoundMixFile
    {
        [Serializable]
        public class SlotValues
        {
            public string name;
            public float volumeDb;
            public float pitch = 1f;
            public bool spatial = true;
            public float minDistance = 0.3f;
            public float maxDistance = 15f;
            public bool roomAcoustics = true;
            public float reverbSendDb;
            [Tooltip("Name of the file the slot plays first (empty: leave the order alone).")]
            public string firstClip;
        }

        [Serializable]
        public class Mix
        {
            public string savedAt;
            public string savedBy;
            public List<SlotValues> slots = new List<SlotValues>();
        }

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MemoriesOfTheFuture");
        public static string MixPath => Path.Combine(Folder, "SoundMix.json");
        public static string SoundsFolder => Path.Combine(Folder, "Sounds");

        public static SlotValues Capture(SoundSlot slot)
        {
            var first = slot.FirstClip;
            return new SlotValues
            {
                name = slot.name,
                volumeDb = slot.volumeDb,
                pitch = slot.pitch,
                spatial = slot.spatial,
                minDistance = slot.minDistance,
                maxDistance = slot.maxDistance,
                roomAcoustics = slot.roomAcoustics,
                reverbSendDb = slot.reverbSendDb,
                // A file loaded from the Sounds folder isn't in the asset, so don't ask the Editor to reorder by it.
                firstClip = first != null && first != slot.RuntimeOverride ? first.name : "",
            };
        }

        /// <summary>Copy saved values onto a slot. Returns what changed ("" if nothing).</summary>
        public static string Apply(SlotValues v, SoundSlot slot)
        {
            var changes = new List<string>();
            void F(string label, ref float field, float value, string unit = "")
            {
                if (Mathf.Abs(field - value) < 0.001f) return;
                changes.Add($"{label} {field:0.##}{unit} -> {value:0.##}{unit}");
                field = value;
            }
            void B(string label, ref bool field, bool value)
            {
                if (field == value) return;
                changes.Add($"{label} {(value ? "on" : "off")}");
                field = value;
            }
            F("level", ref slot.volumeDb, Mathf.Clamp(v.volumeDb, -60f, 20f), " dB");
            F("pitch", ref slot.pitch, Mathf.Clamp(v.pitch, 0.1f, 3f));
            B("spatial", ref slot.spatial, v.spatial);
            F("min distance", ref slot.minDistance, Mathf.Max(0.01f, v.minDistance), " m");
            F("max distance", ref slot.maxDistance, Mathf.Max(0.1f, v.maxDistance), " m");
            B("room acoustics", ref slot.roomAcoustics, v.roomAcoustics);
            F("reverb send", ref slot.reverbSendDb, Mathf.Clamp(v.reverbSendDb, -60f, 20f), " dB");
            if (!string.IsNullOrEmpty(v.firstClip))
            {
                var current = slot.clips.Find(c => c != null);
                if (current == null || current.name != v.firstClip)
                {
                    var clip = slot.clips.Find(c => c != null && c.name == v.firstClip);
                    if (clip != null) { slot.MakeFirst(clip); changes.Add($"plays {clip.name} first"); }
                }
            }
            if (changes.Count > 0) slot.Touch();
            return string.Join(", ", changes);
        }

        /// <summary>Write every slot's values to <see cref="MixPath"/>. Returns the path.</summary>
        public static string Save(IEnumerable<SoundSlot> slots)
        {
            var mix = new Mix { savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), savedBy = Application.isEditor ? "Unity Editor" : "desktop app" };
            foreach (var s in slots) if (s != null && (s.hideFlags & HideFlags.DontSave) == 0) mix.slots.Add(Capture(s));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(MixPath, JsonUtility.ToJson(mix, true));
            Debug.Log($"[SoundMix] Saved {mix.slots.Count} slots to {MixPath}");
            return MixPath;
        }

        /// <summary>The saved mix, or null if there is none (or it can't be read).</summary>
        public static Mix Load()
        {
            if (!File.Exists(MixPath)) return null;
            try { return JsonUtility.FromJson<Mix>(File.ReadAllText(MixPath)); }
            catch (Exception e) { Debug.LogWarning($"[SoundMix] Couldn't read {MixPath}: {e.Message}"); return null; }
        }
    }
}
