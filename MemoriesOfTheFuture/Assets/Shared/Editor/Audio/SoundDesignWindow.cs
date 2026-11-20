using System.Collections.Generic;
using System.IO;
using MemoriesOfTheFuture.Mug;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace Memories.Audio.EditorTools
{
    /// <summary>
    /// Memories > Sound Design: every sound in the experience on one page. Listen to each one,
    /// solo or mute it, set its level, pitch and mixer group, drag new files onto it, and see what
    /// is playing (and where) while the game runs. Changes are saved, including ones made in Play mode.
    /// </summary>
    public class SoundDesignWindow : EditorWindow
    {
        [MenuItem("Memories/Sound Design", priority = 0)]
        public static void Open() => GetWindow<SoundDesignWindow>("Sound Design").minSize = new Vector2(420, 300);

        List<SoundSlot> _slots = new List<SoundSlot>();
        readonly HashSet<SoundSlot> _expanded = new HashSet<SoundSlot>();
        readonly Dictionary<SoundSlot, UnityEditor.Editor> _editors = new Dictionary<SoundSlot, UnityEditor.Editor>();
        Vector2 _scroll;
        bool _showHelp = true, _showInScene = true;
        AudioSource _preview;
        SoundSlot _previewSlot;
        double _lastRepaint;
        GUIStyle _title, _small, _wrap;

        void OnEnable()
        {
            Refresh();
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnUpdate;
            EditorApplication.playModeStateChanged += OnPlayMode;
            _showHelp = EditorPrefs.GetBool("Memories.SoundDesign.Help", true);
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            StopPreview();
            foreach (var e in _editors.Values) if (e != null) DestroyImmediate(e);
            _editors.Clear();
        }

        void OnProjectChange() { Refresh(); Repaint(); }
        void OnFocus() => Refresh();

        void OnPlayMode(PlayModeStateChange _)
        {
            StopPreview();
            SoundSlot.Soloed = null;
            foreach (var s in _slots) if (s != null) s.Muted = false;
            Repaint();
        }

        void Refresh() => _slots = AudioPaths.AllSlots();

        void OnUpdate()
        {
            if (_preview != null && !_preview.isPlaying) StopPreview();
            // Live meters while playing; a gentle rate is enough.
            if ((Application.isPlaying || _preview != null) && EditorApplication.timeSinceStartup - _lastRepaint > 0.1)
            {
                _lastRepaint = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        // ------------------------------------------------------------ layout

        void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _small = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }

        void OnGUI()
        {
            Styles();
            Toolbar();
            if (_showHelp) Help();
            if (Application.isPlaying) MixState();

            if (_slots.Count == 0)
            {
                EditorGUILayout.HelpBox("No Sound Slots yet. Run Memories > Audio > Set Up Sound Slots and Mixer.", MessageType.Info);
                if (GUILayout.Button("Set up now", GUILayout.Height(28))) AudioSetup.SetUpInteractive();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                SlotRow(slot);
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent("Mixer", "Open the Audio Mixer window (group faders, effects, snapshots)"), EditorStyles.toolbarButton)) OpenMixer();
                if (GUILayout.Button(new GUIContent("Drop Folder", "Show Assets/Audio/Drop in Finder: put new sound files there"), EditorStyles.toolbarButton))
                    EditorUtility.RevealInFinder(Path.GetFullPath(AudioPaths.Drop) + "/");
                if (GUILayout.Button(new GUIContent("New Slot", "Make a new Sound Slot asset"), EditorStyles.toolbarButton)) NewSlot();
                if (GUILayout.Button(new GUIContent("Import mix", "Bring in the mix saved from the desktop app (Tab > Sound > Save mix): levels, pitch, space"), EditorStyles.toolbarButton))
                    AudioSetup.ImportMixInteractive();
                if (GUILayout.Button(new GUIContent("Add Sound to Scene", "Place a Sound Emitter: plays a slot from a point, at a moment you choose"), EditorStyles.toolbarButton)) AddEmitter();
                GUILayout.FlexibleSpace();
                _showInScene = GUILayout.Toggle(_showInScene, new GUIContent("Show in Scene", "Draw playing sounds and their ranges in the Scene view"), EditorStyles.toolbarButton);
                bool help = GUILayout.Toggle(_showHelp, "Help", EditorStyles.toolbarButton);
                if (help != _showHelp) { _showHelp = help; EditorPrefs.SetBool("Memories.SoundDesign.Help", help); }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode;
                if (GUILayout.Button(new GUIContent("▶  Test on desktop", "Open 03-DesktopTesting and press Play: mouse and keyboard, no headset"), GUILayout.Height(26)))
                    PlayScene(AudioPaths.DesktopScene);
                if (GUILayout.Button(new GUIContent("Open headset scene", "Open 02-Mug (what gets built to the Quest)"), GUILayout.Height(26)))
                    OpenScene(AudioPaths.HeadsetScene);
                GUI.enabled = true;
                if (Application.isPlaying && GUILayout.Button("■  Stop", GUILayout.Height(26), GUILayout.Width(70))) EditorApplication.isPlaying = false;
            }
        }

        void Help()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("How it works", EditorStyles.boldLabel);
                GUILayout.Label(
                    "• Each box below is one sound in the experience (a Sound Slot).\n" +
                    "• New file: drop it in the Drop Folder named after the slot (scream_v2.wav), or drag it onto a box. The newest file plays.\n" +
                    "• ▶ listens to it here. S solos it, M mutes it (while testing only, not saved).\n" +
                    "• Level and pitch save as you go, even in Play mode. ⋯ shows every setting.\n" +
                    "• Mixer: one group per family (Ambience, Mug, Dream, UI) and one snapshot per moment (Room, Approach, LookingIn, Dream). " +
                    "The game blends the snapshots; untick 'Blending' below while you adjust one.\n" +
                    "• Undo (Cmd+Z) works for everything here.", _wrap);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open the full guide", EditorStyles.miniButton, GUILayout.Width(140)))
                    {
                        var readme = AssetDatabase.LoadAssetAtPath<Object>(AudioPaths.Readme);
                        if (readme != null) AssetDatabase.OpenAsset(readme);
                    }
                    if (GUILayout.Button(new GUIContent("Adding spatial sound", "Step by step, with the three examples in 03-DesktopTesting"),
                                         EditorStyles.miniButton, GUILayout.Width(140)))
                    {
                        var guide = AssetDatabase.LoadAssetAtPath<Object>(AudioPaths.SpatialGuide);
                        if (guide != null) AssetDatabase.OpenAsset(guide);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        void MixState()
        {
            var d = MixDirector.Current;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (d == null) { GUILayout.Label("No Sound Mix (MixDirector) in this scene: snapshots aren't being blended.", _small); return; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Mix right now", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    d.blending = GUILayout.Toggle(d.blending, new GUIContent("Blending", "Untick to hold the mixer on one snapshot while you adjust it"));
                }
                for (int i = 0; i < MixDirector.SnapshotNames.Length; i++)
                {
                    var r = EditorGUILayout.GetControlRect(false, 16);
                    EditorGUI.ProgressBar(r, d.Weights[i], $"{MixDirector.SnapshotNames[i]}  {d.Weights[i] * 100f:0}%");
                }
            }
        }

        void SlotRow(SoundSlot slot)
        {
            var box = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var so = new SerializedObject(slot);
            so.Update();

            // Header: name, live state, preview, solo, mute.
            using (new EditorGUILayout.HorizontalScope())
            {
                var voices = slot.Voices;
                int playing = 0;
                foreach (var v in voices) if (v.Source != null && v.Source.isPlaying && v.Source.volume > 0.0001f) playing++;
                var dot = new GUIContent(playing > 0 ? "●" : "○", playing > 0 ? $"Playing ({playing})" : "Silent");
                var old = GUI.color;
                GUI.color = playing > 0 ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.4f);
                GUILayout.Label(dot, GUILayout.Width(14));
                GUI.color = old;

                if (GUILayout.Button(slot.name, _title)) { EditorGUIUtility.PingObject(slot); Selection.activeObject = slot; }
                GUILayout.FlexibleSpace();

                bool previewing = _previewSlot == slot && _preview != null;
                if (GUILayout.Button(new GUIContent(previewing ? "■" : "▶", "Listen (through its mixer group)"), GUILayout.Width(28))) { if (previewing) StopPreview(); else Preview(slot); }
                GUI.enabled = Application.isPlaying;
                bool solo = GUILayout.Toggle(SoundSlot.Soloed == slot, new GUIContent("S", "Solo: hear only this sound (Play mode, not saved)"), EditorStyles.miniButtonLeft, GUILayout.Width(24));
                if (solo != (SoundSlot.Soloed == slot)) SoundSlot.Soloed = solo ? slot : null;
                slot.Muted = GUILayout.Toggle(slot.Muted, new GUIContent("M", "Mute this sound (Play mode, not saved)"), EditorStyles.miniButtonRight, GUILayout.Width(24));
                GUI.enabled = true;
                bool open = _expanded.Contains(slot);
                if (GUILayout.Button(new GUIContent("⋯", "Every setting, and the notes"), GUILayout.Width(26))) { if (open) _expanded.Remove(slot); else _expanded.Add(slot); }
            }

            if (!string.IsNullOrEmpty(slot.notes) && !_expanded.Contains(slot)) GUILayout.Label(slot.notes, _small);

            // Files.
            using (new EditorGUILayout.HorizontalScope())
            {
                var first = slot.FirstClip;
                EditorGUILayout.LabelField("File", GUILayout.Width(60));
                var picked = (AudioClip)EditorGUILayout.ObjectField(first, typeof(AudioClip), false);
                if (picked != first && picked != null) AddClip(slot, picked);
                int count = slot.clips.FindAll(c => c != null).Count;
                if (count > 1) GUILayout.Label($"+{count - 1} more ({slot.variation})", EditorStyles.miniLabel, GUILayout.Width(130));
            }

            // Level, pitch, group.
            var db = so.FindProperty(nameof(SoundSlot.volumeDb));
            var pitch = so.FindProperty(nameof(SoundSlot.pitch));
            var output = so.FindProperty(nameof(SoundSlot.output));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Level dB", db.tooltip), GUILayout.Width(60));
                db.floatValue = EditorGUILayout.Slider(db.floatValue, -60f, slot.spatial ? 20f : 0f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Pitch", pitch.tooltip), GUILayout.Width(60));
                pitch.floatValue = EditorGUILayout.Slider(pitch.floatValue, 0.1f, 3f);
                GUILayout.Label($"{12f * Mathf.Log(pitch.floatValue, 2f):+0.0;-0.0} st", EditorStyles.miniLabel, GUILayout.Width(48));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Mixer", output.tooltip), GUILayout.Width(60));
                EditorGUILayout.PropertyField(output, GUIContent.none);
                GUILayout.Label(slot.spatial ? $"3D  {slot.minDistance:0.##}–{slot.maxDistance:0.#} m" : "stereo", EditorStyles.miniLabel, GUILayout.Width(110));
            }
            if (so.ApplyModifiedProperties()) slot.Touch();

            if (_expanded.Contains(slot))
            {
                EditorGUILayout.Space(4);
                if (!_editors.TryGetValue(slot, out var ed) || ed == null) _editors[slot] = ed = UnityEditor.Editor.CreateEditor(slot);
                EditorGUI.BeginChangeCheck();
                ed.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) slot.Touch();
            }
            EditorGUILayout.EndVertical();
            HandleDrop(box, slot);
            EditorGUILayout.Space(2);
        }

        // ------------------------------------------------------------ files

        void HandleDrop(Rect area, SoundSlot slot)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            var clips = new List<AudioClip>();
            foreach (var o in DragAndDrop.objectReferences) if (o is AudioClip c) clips.Add(c);
            if (clips.Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var c in clips) AddClip(slot, c);
            }
            e.Use();
        }

        static void AddClip(SoundSlot slot, AudioClip clip)
        {
            Undo.RecordObject(slot, "Add sound file");
            slot.clips.RemoveAll(c => c == null || c == clip);
            slot.clips.Insert(0, clip);
            slot.Touch();
            EditorUtility.SetDirty(slot);
            AssetDatabase.SaveAssetIfDirty(slot);
            Debug.Log($"[Sound Design] {slot.name} now plays '{clip.name}'", slot);
        }

        // ------------------------------------------------------------ preview (edit mode and play mode)

        void Preview(SoundSlot slot)
        {
            StopPreview();
            var clip = slot.PickClip();
            if (clip == null) { ShowNotification(new GUIContent($"{slot.name} has no sound file")); return; }
            var go = EditorUtility.CreateGameObjectWithHideFlags("Sound Design Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
            _preview = go.GetComponent<AudioSource>();
            _preview.clip = clip;
            _preview.outputAudioMixerGroup = slot.output;
            _preview.spatialBlend = 0f;
            _preview.volume = SoundSlot.DbToLinear(Mathf.Min(0f, slot.volumeDb));
            _preview.pitch = slot.pitch;
            _preview.loop = false;
            _preview.Play();
            _previewSlot = slot;
        }

        void StopPreview()
        {
            if (_preview != null) DestroyImmediate(_preview.gameObject);
            _preview = null;
            _previewSlot = null;
        }

        // ------------------------------------------------------------ actions

        static void OpenMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioPaths.Mixer);
            if (mixer != null) { Selection.activeObject = mixer; AssetDatabase.OpenAsset(mixer); }
            EditorApplication.ExecuteMenuItem("Window/Audio/Audio Mixer");
        }

        static void NewSlot()
        {
            if (!AssetDatabase.IsValidFolder(AudioPaths.Slots)) AudioSetup.SetUp();
            var slot = CreateInstance<SoundSlot>();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioPaths.Mixer);
            slot.output = AudioPaths.Group(mixer, "Ambience");
            slot.notes = "What is this sound for? When does it play?";
            var path = AssetDatabase.GenerateUniqueAssetPath(AudioPaths.Slots + "/New Sound.asset");
            AssetDatabase.CreateAsset(slot, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = slot;
            EditorGUIUtility.PingObject(slot);
            EditorUtility.DisplayDialog("New Sound Slot",
                $"Created {path}.\n\nRename it in the Project window, set its Drop Name, and add files. " +
                "To hear it in the game, use 'Add Sound to Scene' and pick this slot.", "OK");
        }

        static void AddEmitter()
        {
            if (Application.isPlaying) { EditorUtility.DisplayDialog("Add Sound to Scene", "Stop Play mode first, or the sound won't be saved in the scene.", "OK"); return; }
            var go = new GameObject("Sound - new");
            var e = go.AddComponent<SoundEmitter>();
            if (Selection.activeObject is SoundSlot s) { e.slot = s; go.name = "Sound - " + s.name; }
            var view = SceneView.lastActiveSceneView;
            if (view != null) go.transform.position = view.pivot;
            Undo.RegisterCreatedObjectUndo(go, "Add Sound to Scene");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static void PlayScene(string path)
        {
            if (!OpenScene(path)) return;
            EditorApplication.isPlaying = true;
        }

        static bool OpenScene(string path)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (EditorSceneManager.GetActiveScene().path != path) EditorSceneManager.OpenScene(path);
            return true;
        }

        // ------------------------------------------------------------ scene view

        void OnSceneGUI(SceneView view)
        {
            if (!_showInScene || !Application.isPlaying) return;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                foreach (var v in slot.Voices)
                {
                    var a = v.Source;
                    if (a == null || !a.isPlaying || a.spatialBlend < 0.5f) continue;
                    var p = a.transform.position;
                    float level = Mathf.Clamp01(a.volume);
                    Handles.color = new Color(0.3f, 0.8f, 1f, 0.25f + 0.75f * level);
                    Handles.DrawWireDisc(p, Vector3.up, a.minDistance);
                    Handles.color = new Color(0.3f, 0.8f, 1f, 0.15f);
                    Handles.DrawWireDisc(p, Vector3.up, Mathf.Min(a.maxDistance, 50f));
                    Handles.Label(p + Vector3.up * 0.05f, $"{slot.name}  {SoundSlot.LinearToDb(a.volume):0} dB", EditorStyles.whiteMiniLabel);
                }
            }
        }
    }
}
