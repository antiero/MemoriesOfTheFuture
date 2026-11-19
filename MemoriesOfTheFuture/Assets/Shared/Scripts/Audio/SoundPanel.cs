using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Memories.Audio
{
    /// <summary>
    /// The in-game Sound panel (IMGUI, Game view), for the sound designers in the desktop app and in
    /// Play mode: every Sound Slot in the scene with a playing light, solo / mute, level, pitch, which
    /// file plays, and its space settings; the mix snapshots; and buttons to save the mix and reload
    /// files from ~/MemoriesOfTheFuture/Sounds.
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
        bool _unsaved;
        bool _loading;
        GUIStyle _dot, _name, _toggle;

        /// <summary>Called after the Sounds folder has been (re)loaded, e.g. to restart the background bed.</summary>
        public event Action FilesReloaded;

        /// <param name="host">Runs the file-loading coroutines.</param>
        public SoundPanel(MonoBehaviour host) { _host = host; }

        /// <summary>Every Sound Slot loaded (the scene's), by name. Skips the silent stand-ins for unassigned slots.</summary>
        public IReadOnlyList<SoundSlot> Slots { get { Scan(false); return _slots; } }
        public bool Loading => _loading;
        public string Status => _status;

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
            catch (Exception e) { _status = "Couldn't create the sounds folder: " + e.Message; }

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
            _status = $"Loaded your saved mix ({mix.savedAt})";
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
            _status = pick.Count == 0 ? "No files in the Sounds folder: playing the built-in sounds" : $"Loaded {_fromDisk.Count} file(s) from the Sounds folder";
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
                _status = $"Saved to {SoundMixFile.MixPath}" + (Application.isEditor ? "" : " (the app loads it at every launch)");
            }
            catch (Exception e) { _status = "Couldn't save: " + e.Message; Debug.LogWarning("[SoundPanel] " + _status); }
        }

        // ------------------------------------------------------------ drawing

        /// <summary>Draw inside an existing GUILayout area.</summary>
        public void Draw(GUIStyle title, GUIStyle body, GUIStyle small)
        {
            Scan(false);
            if (_dot == null)
            {
                _dot = new GUIStyle(body) { fontSize = body.fontSize + 2, alignment = TextAnchor.MiddleCenter };
                _name = new GUIStyle(body) { fontStyle = FontStyle.Bold };
                _toggle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(9, body.fontSize - 1), padding = new RectOffset(2, 2, 1, 1) };
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Save mix", "Write every level, pitch and space setting to " + SoundMixFile.MixPath))) SaveMix();
            if (GUILayout.Button(new GUIContent(_loading ? "Loading..." : "Reload files (L)", "Load the Sounds folder again"))) ReloadFiles();
            if (GUILayout.Button(new GUIContent("Open folder", SoundMixFile.SoundsFolder))) OpenFolder();
            GUILayout.EndHorizontal();
            if (_unsaved && !Application.isEditor) GUILayout.Label("Unsaved changes: press Save mix to keep them.", small);
            if (_status != "") GUILayout.Label(_status, small);

            MixState(small);
            if (SoundSlot.Soloed != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Solo: only {SoundSlot.Soloed.name} is heard", body);
                if (GUILayout.Button("Clear", GUILayout.Width(50))) SoundSlot.Soloed = null;
                GUILayout.EndHorizontal();
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var slot in _slots) if (slot != null) SlotRow(slot, body, small);
            if (_loaded.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Sounds folder", title);
                foreach (var line in _loaded) GUILayout.Label(line, small);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(Application.isEditor
                ? "Edits change the Sound Slot assets and are kept after Play mode."
                : "Save mix keeps your changes for next time. In Unity, Memories > Audio > Import Mix From Desktop App brings them in.", small);
        }

        void MixState(GUIStyle small)
        {
            var md = CupSpike.MixDirector.Current;
            if (md == null) return;
            GUILayout.BeginHorizontal();
            md.blending = GUILayout.Toggle(md.blending, new GUIContent(" Blending", "Untick to stop the snapshots following the experience"), GUILayout.Width(80));
            var w = "";
            for (int i = 0; i < CupSpike.MixDirector.SnapshotNames.Length; i++)
                w += $"{CupSpike.MixDirector.SnapshotNames[i]} {md.Weights[i] * 100f:0}%  ";
            GUILayout.Label(w, small);
            GUILayout.EndHorizontal();
        }

        void SlotRow(SoundSlot slot, GUIStyle body, GUIStyle small)
        {
            bool playing = false;
            foreach (var v in slot.Voices) if (v.Source != null && v.Source.isPlaying && v.Source.volume > 0.0001f) { playing = true; break; }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            var c = GUI.contentColor;
            GUI.contentColor = playing ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.35f);
            GUILayout.Label(new GUIContent(playing ? "●" : "○", playing ? "Playing" : "Not playing"), _dot, GUILayout.Width(16));
            GUI.contentColor = c;
            GUILayout.Label(new GUIContent(slot.name, slot.notes), _name);
            GUILayout.FlexibleSpace();
            bool solo = GUILayout.Toggle(SoundSlot.Soloed == slot, new GUIContent("S", "Solo: hear only this"), _toggle, GUILayout.Width(22));
            if (solo != (SoundSlot.Soloed == slot)) SoundSlot.Soloed = solo ? slot : null;
            slot.Muted = GUILayout.Toggle(slot.Muted, new GUIContent("M", "Mute"), _toggle, GUILayout.Width(22));
            bool open = GUILayout.Toggle(_expanded.Contains(slot), new GUIContent("...", "Space: distances, room acoustics, reverb"), _toggle, GUILayout.Width(26));
            if (open) _expanded.Add(slot); else _expanded.Remove(slot);
            GUILayout.EndHorizontal();

            float top = slot.spatial ? 20f : 0f;
            float db = Row($"Level {(slot.volumeDb <= -60f ? "off" : $"{slot.volumeDb:+0.0;-0.0;0.0} dB")}", slot.volumeDb, -60f, top, body);
            if (Mathf.Abs(db - slot.volumeDb) > 0.0001f) { slot.volumeDb = Mathf.Round(db * 10f) / 10f; Changed(slot); }
            float pitch = Row($"Pitch x{slot.pitch:0.00}", slot.pitch, 0.1f, 3f, body);
            if (Mathf.Abs(pitch - slot.pitch) > 0.0001f) { slot.pitch = Mathf.Round(pitch * 100f) / 100f; Changed(slot); }
            FileRow(slot, small);

            if (!open) return;
            bool spatial = GUILayout.Toggle(slot.spatial, " Spatial (from a point in the room; off: stereo in the ears)");
            if (spatial != slot.spatial)
            {
                slot.spatial = spatial;
                if (!spatial) slot.volumeDb = Mathf.Min(slot.volumeDb, 0f);   // above 0 dB only works spatialised
                Changed(slot);
            }
            GUI.enabled = slot.spatial;
            float min = Row($"Full volume within {slot.minDistance:0.00} m", slot.minDistance, 0.05f, 10f, body);
            if (Mathf.Abs(min - slot.minDistance) > 0.0001f) { slot.minDistance = Mathf.Round(min * 100f) / 100f; Changed(slot); }
            float max = Row($"Quietest beyond {slot.maxDistance:0.0} m", slot.maxDistance, 1f, 100f, body);
            if (Mathf.Abs(max - slot.maxDistance) > 0.0001f) { slot.maxDistance = Mathf.Max(slot.minDistance + 0.1f, Mathf.Round(max * 10f) / 10f); Changed(slot); }
            bool room = GUILayout.Toggle(slot.roomAcoustics, " Room acoustics (reflections and reverb)");
            if (room != slot.roomAcoustics) { slot.roomAcoustics = room; Changed(slot); }
            float rev = Row($"Reverb send {slot.reverbSendDb:+0.0;-0.0;0.0} dB", slot.reverbSendDb, -60f, 20f, body);
            if (Mathf.Abs(rev - slot.reverbSendDb) > 0.0001f) { slot.reverbSendDb = Mathf.Round(rev * 10f) / 10f; Changed(slot); }
            GUI.enabled = true;
        }

        void FileRow(SoundSlot slot, GUIStyle small)
        {
            GUILayout.BeginHorizontal();
            if (slot.RuntimeOverride != null)
            {
                GUILayout.Label(new GUIContent($"File: {slot.RuntimeOverride.name} (Sounds folder)", "Loaded from " + SoundMixFile.SoundsFolder), small);
                if (GUILayout.Button(new GUIContent("Built-in", "Play the slot's own files again (until the next reload)"), _toggle, GUILayout.Width(60)))
                    slot.RuntimeOverride = null;
            }
            else
            {
                var valid = slot.clips.FindAll(x => x != null);
                GUI.enabled = valid.Count > 1;
                if (GUILayout.Button(new GUIContent("◀", "Previous file"), _toggle, GUILayout.Width(22)))
                {
                    slot.MakeFirst(valid[valid.Count - 1]);
                    MarkDirty(slot);
                }
                GUI.enabled = true;
                string order = slot.variation == SoundSlot.Variation.Random ? "  (random each time)" : slot.variation == SoundSlot.Variation.InOrder ? "  (in order)" : "";
                GUILayout.Label(valid.Count == 0 ? "No file" : $"File: {valid[0].name}{(valid.Count > 1 ? $"  (1 of {valid.Count})" : "")}{order}", small);
                GUI.enabled = valid.Count > 1;
                if (GUILayout.Button(new GUIContent("▶", "Next file"), _toggle, GUILayout.Width(22)))
                {
                    // Rotate: the current file goes to the end, the next one to the top.
                    slot.clips.Remove(valid[0]);
                    slot.clips.Add(valid[0]);
                    slot.MakeFirst(valid[1]);
                    MarkDirty(slot);
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        float Row(string label, float value, float min, float max, GUIStyle body)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, body, GUILayout.Width(170));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            return v;
        }

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
