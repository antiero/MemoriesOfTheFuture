using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Memories.Audio.EditorTools
{
    /// <summary>
    /// The drop folder (Assets/Audio/Drop). Sound files put there:
    ///  1. get import settings that suit the headset, once (mono for spatial sounds, Vorbis, and
    ///     compressed-in-memory for anything over a few seconds); change them by hand after that if you like;
    ///  2. are added to the top of the matching Sound Slot, so they play next. A file matches a slot
    ///     when its name starts with the slot's Drop Name (scream_take3.wav -> Scream) or when it sits in
    ///     a folder with that name (Drop/scream/take3.wav). The longest match wins, so
    ///     background_left_v2.wav goes to Background Left, not Background.
    /// Files that match nothing are left alone, with a message in the Console saying how to name them.
    /// </summary>
    public class AudioDropImporter : AssetPostprocessor
    {
        const string Marker = "memories-drop-v1";
        static readonly string[] Extensions = { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac" };

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var paths = new List<string>();
            foreach (var p in imported) if (InDrop(p)) paths.Add(p);
            foreach (var p in moved) if (InDrop(p) && !paths.Contains(p)) paths.Add(p);
            if (paths.Count == 0) return;

            var slots = AudioPaths.AllSlots();
            var reimport = new List<string>();
            bool changed = false;
            foreach (var path in paths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                var slot = Match(path, slots);

                bool firstImport = AssetImporter.GetAtPath(path) is AudioImporter ai && ai.userData != Marker;
                if (firstImport) reimport.Add(path);

                if (slot == null)
                {
                    if (firstImport) continue;   // warn once, after the settings reimport

                    var names = string.Join(", ", slots.ConvertAll(s => s.dropName).FindAll(n => !string.IsNullOrEmpty(n)));
                    Debug.LogWarning($"[Sound drop] '{Path.GetFileName(path)}' doesn't match a Sound Slot, so nothing plays it yet. " +
                                     $"Start its name with one of: {names} (e.g. scream_take2.wav), put it in a folder with that name, " +
                                     "or drag it onto a slot in Memories > Sound Design.");
                    continue;
                }
                if (slot.Contains(clip)) continue;
                slot.clips.RemoveAll(c => c == null);
                slot.clips.Insert(0, clip);
                slot.Touch();
                EditorUtility.SetDirty(slot);
                changed = true;
                Debug.Log($"[Sound drop] Added '{clip.name}' to {slot.name}" +
                          (slot.variation == SoundSlot.Variation.NewestFirst ? " - it plays from now on (the older files are still in the slot's list)." : "."),
                          slot);
            }
            if (changed) EditorApplication.delayCall += AssetDatabase.SaveAssets;
            if (reimport.Count > 0) EditorApplication.delayCall += () => { foreach (var p in reimport) ApplySettings(p); };
        }

        static bool InDrop(string path) =>
            path.StartsWith(AudioPaths.Drop + "/", StringComparison.OrdinalIgnoreCase) &&
            Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

        /// <summary>The slot whose drop name best matches the file name or one of its folders.</summary>
        public static SoundSlot Match(string path, List<SoundSlot> slots)
        {
            string stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
            var folders = path.Substring(AudioPaths.Drop.Length).ToLowerInvariant().Split('/');
            SoundSlot best = null;
            int bestLen = 0;
            foreach (var s in slots)
            {
                if (string.IsNullOrEmpty(s.dropName)) continue;
                string key = s.dropName.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                bool byName = stem.StartsWith(key) && (stem.Length == key.Length || !char.IsLetter(stem[key.Length]));
                bool byFolder = Array.IndexOf(folders, key) >= 0 && Array.IndexOf(folders, key) < folders.Length - 1;
                if ((byName || byFolder) && key.Length > bestLen) { best = s; bestLen = key.Length; }
            }
            return best;
        }

        /// <summary>First import only: set headset-friendly settings and reimport.</summary>
        static void ApplySettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (importer == null || clip == null || importer.userData == Marker) return;
            var slot = Match(path, AudioPaths.AllSlots());
            bool spatial = slot != null ? slot.spatial : !Path.GetFileName(path).ToLowerInvariant().Contains("stereo");
            importer.forceToMono = spatial;
            importer.loadInBackground = clip.length > 10f;
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.loadType = clip.length > 5f ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.userData = Marker;
            importer.SaveAndReimport();
            Debug.Log($"[Sound drop] {Path.GetFileName(path)}: import settings set to {(spatial ? "mono (spatial)" : "stereo")}, Vorbis, {settings.loadType}");
        }
    }
}
