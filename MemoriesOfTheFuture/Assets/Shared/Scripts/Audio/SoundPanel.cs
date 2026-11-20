using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Memories.Audio
{
    /// <summary>
    /// What the in-game Sound panel works on (the desktop testing app and Play mode; the panel itself is
    /// drawn by Mug.DesktopUI): every Sound Slot in the scene, edits to them, saving the mix, and
    /// loading files from ~/MemoriesOfTheFuture/Sounds.
    ///
    /// Edits go straight to the Sound Slot assets, so in the Editor they are kept after Play mode.
    /// The built app can't change its assets, so there "Save mix" writes SoundMix.json, which the app
    /// applies on every launch and Memories > Audio > Import Mix From Desktop App brings into Unity.
    ///
    /// Files: a file in the Sounds folder whose name starts with a slot's drop name (scream_take3.wav
    /// for "scream") replaces that slot's files while the app runs (<see cref="SoundSlot.RuntimeOverride"/>).
    /// The longest drop name wins, so background_left.wav goes to Background Left, not Background.
    /// </summary>
    public class SoundPanel
    {
        static readonly string[] Extensions = { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };

        readonly MonoBehaviour _host;
        List<SoundSlot> _slots = new List<SoundSlot>();
        float _nextScan;
        Vector2 _scroll;
        readonly HashSet<SoundSlot> _expanded = new HashSet<SoundSlot>();
        readonly Dictionary<SoundSlot, AudioClip> _fromDisk = new Dictionary<SoundSlot, AudioClip>();
        readonly List<string> _loaded = new List<string>();
        string _status = "";
        void SetStatus(string s) { _status = s; StatusVersion++; }
        bool _unsaved;
        bool _loading;

        /// <summary>Called after the Sounds folder has been (re)loaded, e.g. to restart the background bed.</summary>
        public event Action FilesReloaded;

        /// <param name="host">Runs the file-loading coroutines.</param>
        public SoundPanel(MonoBehaviour host) { _host = host; }

        /// <summary>Every Sound Slot loaded (the scene's), by name. Skips the silent stand-ins for unassigned slots.</summary>
        public IReadOnlyList<SoundSlot> Slots { get { Scan(false); return _slots; } }
        public bool Loading => _loading;
        public string Status => _status;
        /// <summary>Changes since the last Save mix (only matters in the built app, where the assets can't change).</summary>
        public bool Unsaved => _unsaved;
        /// <summary>One line per file in the Sounds folder: which slot it went to, or why not.</summary>
        public IReadOnlyList<string> LoadedFiles => _loaded;
        /// <summary>Changes when the status line changes (for toasts).</summary>
        public int StatusVersion { get; private set; }

        void Scan(bool force)
        {
            if (!force && Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            var list = new List<SoundSlot>();
            foreach (var s in Resources.FindObjectsOfTypeAll<SoundSlot>())
                if (s != null && (s.hideFlags & HideFlags.DontSave) == 0) list.Add(s);
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            _slots = list;
        }

        // ------------------------------------------------------------ start-up

        /// <summary>Make the folders, apply the saved mix (built app only) and load the Sounds folder.</summary>
        public void Begin()
        {
            Scan(true);
            try
            {
                Directory.CreateDirectory(SoundMixFile.SoundsFolder);
                File.WriteAllText(Path.Combine(SoundMixFile.SoundsFolder, "README.txt"), ReadmeText());
            }
            catch (Exception e) { SetStatus("Couldn't create the sounds folder: " + e.Message); }

            // In the Editor the slot assets themselves hold the mix; the built app can't save into them,
            // so it keeps the designers' mix in SoundMix.json and applies it at every launch.
            if (!Application.isEditor) ApplySavedMix();
            ReloadFiles();
            Debug.Log($"[SoundPanel] {_slots.Count} slots. Sounds folder: {SoundMixFile.SoundsFolder}");
        }

        string ReadmeText()
        {
            var lines = new List<string>
            {
                "Drop sound files here to replace a sound in the app, then press L (or Reload files in the",
                "Tab > Sound panel). wav, mp3, ogg or aiff.",
                "",
                "The file name must start with the sound's name, e.g. scream_take3.wav or breath.mp3.",
                "If several files start with the same name, the newest one plays.",
                "Remove the file and reload to get the built-in sound back.",
                "",
                "Sound names:",
            };
            foreach (var s in _slots)
                if (!string.IsNullOrEmpty(s.dropName)) lines.Add($"  {s.dropName,-22} {s.name}");
            lines.Add("");
            lines.Add("A 'background' file (stereo) replaces the whole bed, left and right.");
            lines.Add("");
            lines.Add("Levels, pitch and space are set in the app (Tab > Sound) and saved with Save mix to");
            lines.Add(SoundMixFile.MixPath);
            return string.Join("\n", lines) + "\n";
        }

        void ApplySavedMix()
        {
            var mix = SoundMixFile.Load();
            if (mix == null || mix.slots == null) return;
            int n = 0;
            foreach (var v in mix.slots)
            {
                var slot = v != null ? _slots.Find(s => s.name == v.name) : null;
                if (slot == null) continue;
                var changed = SoundMixFile.Apply(v, slot);
                if (changed != "") { n++; Debug.Log($"[SoundPanel] Saved mix: {slot.name}: {changed}"); }
            }
            SetStatus($"Loaded your saved mix ({mix.savedAt})");
            Debug.Log($"[SoundPanel] Applied {SoundMixFile.MixPath} ({n} slot(s) differ from the built-in mix)");
        }

        // ------------------------------------------------------------ the Sounds folder

        /// <summary>Reload ~/MemoriesOfTheFuture/Sounds: each slot plays its newest matching file, or its own files if none.</summary>
        public void ReloadFiles()
        {
            if (_loading || _host == null) return;
            _host.StartCoroutine(LoadFiles());
        }

        IEnumerator LoadFiles()
        {
            _loading = true;
            Scan(true);
            _loaded.Clear();
            var pick = new Dictionary<SoundSlot, string>();
            string[] files = Directory.Exists(SoundMixFile.SoundsFolder) ? Directory.GetFiles(SoundMixFile.SoundsFolder) : new string[0];
            foreach (var f in files)
            {
                if (Array.IndexOf(Extensions, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                SoundSlot match = null;
                foreach (var s in _slots)
                {
                    if (string.IsNullOrEmpty(s.dropName)) continue;
                    var d = s.dropName.ToLowerInvariant();
                    if (name.StartsWith(d) && (match == null || d.Length > match.dropName.Length)) match = s;
                }
                if (match == null) { _loaded.Add($"{Path.GetFileName(f)}: no sound by that name"); continue; }
                if (!pick.TryGetValue(match, out var other) || File.GetLastWriteTime(f) > File.GetLastWriteTime(other)) pick[match] = f;
            }

            // Slots whose file was removed go back to their own files.
            foreach (var s in _slots)
            {
                if (pick.ContainsKey(s) || !_fromDisk.TryGetValue(s, out var old)) continue;
                if (s.RuntimeOverride == old) s.RuntimeOverride = null;
                UnityEngine.Object.Destroy(old);
                _fromDisk.Remove(s);
            }

            foreach (var kv in pick)
            {
                var slot = kv.Key;
                var path = kv.Value;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var type = ext == ".wav" ? AudioType.WAV : ext == ".mp3" ? AudioType.MPEG : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.AIFF;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { _loaded.Add($"{slot.name}: FAILED ({req.error})"); continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null || clip.length <= 0f) { _loaded.Add($"{slot.name}: FAILED (couldn't read {Path.GetFileName(path)})"); continue; }
                    clip.name = Path.GetFileName(path);
                    var old = _fromDisk.TryGetValue(slot, out var o) ? o : null;
                    _fromDisk[slot] = clip;
                    slot.RuntimeOverride = clip;
                    if (old != null) UnityEngine.Object.Destroy(old);
                    _loaded.Add($"{slot.name}: {clip.name} ({clip.length:0.0} s)");
                    Debug.Log($"[SoundPanel] {slot.name} now plays {path} ({clip.length:0.0} s, {clip.channels} ch)");
                }
            }
            SetStatus(pick.Count == 0 ? "No files in the Sounds folder: playing the built-in sounds" : $"Loaded {_fromDisk.Count} file(s) from the Sounds folder");
            _loading = false;
            FilesReloaded?.Invoke();
        }

        public void OpenFolder()
        {
            try { Directory.CreateDirectory(SoundMixFile.SoundsFolder); } catch (Exception) { }
            Application.OpenURL(new Uri(SoundMixFile.SoundsFolder + Path.DirectorySeparatorChar).AbsoluteUri);
        }

        public void SaveMix()
        {
            try
            {
                SoundMixFile.Save(Slots);
                _unsaved = false;
                SetStatus($"Saved to {SoundMixFile.MixPath}" + (Application.isEditor ? "" : " (the app loads it at every launch)"));
            }
            catch (Exception e) { SetStatus("Couldn't save: " + e.Message); Debug.LogWarning("[SoundPanel] " + _status); }
        }

        // ------------------------------------------------------------ edits (from the panel)

        /// <summary>Call after changing a slot's settings: playing voices pick it up, and it's kept.</summary>
        public void Edited(SoundSlot slot) => Changed(slot);

        /// <summary>Play the next (+1) or previous (-1) of the slot's own files.</summary>
        public void StepFile(SoundSlot slot, int dir)
        {
            var valid = slot.clips.FindAll(x => x != null);
            if (valid.Count < 2) return;
            if (dir > 0)
            {
                // Rotate: the current file goes to the end, the next one to the top.
                slot.clips.Remove(valid[0]);
                slot.clips.Add(valid[0]);
                slot.MakeFirst(valid[1]);
            }
            else slot.MakeFirst(valid[valid.Count - 1]);
            MarkDirty(slot);
        }

        /// <summary>Stop playing the Sounds-folder file for this slot (until the next reload).</summary>
        public void UseBuiltIn(SoundSlot slot) => slot.RuntimeOverride = null;

        void Changed(SoundSlot slot)
        {
            slot.Touch();
            MarkDirty(slot);
        }

        void MarkDirty(SoundSlot slot)
        {
            _unsaved = true;
#if UNITY_EDITOR
            // Play-mode edits to an asset are kept, but only written to disk if it's marked dirty.
            UnityEditor.EditorUtility.SetDirty(slot);
#endif
        }
    }
}
