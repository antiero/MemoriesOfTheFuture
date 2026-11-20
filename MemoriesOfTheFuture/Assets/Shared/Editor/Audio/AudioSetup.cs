using System;
using System.Collections.Generic;
using System.Reflection;
using CupSpike;
using Memories.Audio;
using PictureChanges;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Memories.Audio.EditorTools
{
    public static class AudioPaths
    {
        public const string Root = "Assets/Audio";
        public const string Slots = Root + "/Slots";
        public const string Drop = Root + "/Drop";
        public const string Mixer = Root + "/Memories Mixer.mixer";
        public const string Readme = Root + "/README - Sound Design.md";
        public const string SpatialGuide = Root + "/README - Adding Spatial Sound.md";
        public const string HeadsetScene = "Assets/Scenes/02-CupSpike.unity";
        public const string DesktopScene = "Assets/Scenes/03-DesktopTesting.unity";
        public const string PaintingScene = "Assets/Scenes/01-PictureChanges.unity";
        public static readonly string[] Groups = { "Ambience", "Mug", "Dream", "Painting", "UI" };

        public static List<SoundSlot> AllSlots()
        {
            var list = new List<SoundSlot>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SoundSlot)))
            {
                var s = AssetDatabase.LoadAssetAtPath<SoundSlot>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) list.Add(s);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static AudioMixerGroup Group(AudioMixer mixer, string name)
        {
            if (mixer == null) return null;
            foreach (var g in mixer.FindMatchingGroups(name)) if (g.name == name) return g;
            return null;
        }
    }

    /// <summary>
    /// One-off setup: the mixer (groups and phase snapshots), a Sound Slot per sound in the mug
    /// experience (holding the files and levels the scenes used before), and the scenes pointed at
    /// them. Safe to run again: it only creates what is missing and never overwrites a slot.
    /// </summary>
    public static class AudioSetup
    {
        struct SlotDef
        {
            public string name, drop, group, notes;
            public string[] clips;
            public float db, pitch, min, max;
            public bool spatial;
        }

        // Levels carried over from the scenes' old per-script values (linear -> dB).
        static readonly SlotDef[] Defs =
        {
            new SlotDef { name = "Scream", drop = "scream", group = "Mug", clips = new[] { "Assets/Audio/scream.mp3" },
                db = 0f, pitch = 1f, spatial = true, min = 0.3f, max = 15f,
                notes = "The payoff: plays from inside the mug when it's touched (after a beat of silence), and when you wake from the dream. Restarts rather than overlapping." },
            new SlotDef { name = "Breath", drop = "breath", group = "Mug", clips = new[] { "Assets/CupSpike/Audio/Breath_placeholder.wav" },
                db = -0.9f, pitch = 1f, spatial = true, min = 0.3f, max = 15f,
                notes = "Loops from inside the mug as a hand approaches: silent at 30 cm, full level at the touch zone, rising in pitch (x0.9 to x1.15). Cuts dead if the hand pulls away." },
            new SlotDef { name = "Drone", drop = "drone", group = "Mug", clips = new[] { "Assets/CupSpike/Audio/Breath_placeholder.wav" },
                db = -3.1f, pitch = 0.55f, spatial = true, min = 0.3f, max = 500f,
                notes = "Loops from inside the mug while you look into it; fades in with the opening. Placeholder: the breath, pitched down." },
            new SlotDef { name = "Whisper", drop = "whisper", group = "Dream", clips = new[] { "Assets/CupSpike/Audio/Breath_placeholder.wav" },
                db = -4.4f, pitch = 0.5f, spatial = true, min = 0.5f, max = 500f,
                notes = "Two copies circle your head 1.3 m away during the dream (the second x1.24 higher), fading in as it deepens. Placeholder: the breath, pitched down." },
            new SlotDef { name = "Background", drop = "background", group = "Ambience", clips = new[] { "Assets/Audio/mugAudio.mp3" },
                db = -9.1f, pitch = 1f, spatial = false, min = 1f, max = 30f,
                notes = "Stereo bed in your ears. Only used if Background Left / Right have no files. Drains as a hand approaches, cuts in the silence, ducks under the scream, muffles in the dream (all in code; shape it further with the Ambience group)." },
            new SlotDef { name = "Background Left", drop = "background_left", group = "Ambience", clips = new[] { "Assets/Audio/mugAudio_L.mp3" },
                db = -9.1f, pitch = 1f, spatial = true, min = 2f, max = 30f,
                notes = "Left channel of the bed, played from a fixed point 3 m left and 1 m in front of where you start, so the bed stays put when you turn. Starts in sync with Background Right." },
            new SlotDef { name = "Background Right", drop = "background_right", group = "Ambience", clips = new[] { "Assets/Audio/mugAudio_R.mp3" },
                db = -9.1f, pitch = 1f, spatial = true, min = 2f, max = 30f,
                notes = "Right channel of the bed (see Background Left)." },
            new SlotDef { name = "Painting Scream", drop = "painting_scream", group = "Painting", clips = new[] { "Assets/Audio/mono-scream.mp3" },
                db = 0f, pitch = 1f, spatial = true, min = 1.5f, max = 15f,
                notes = "Plays from the painting when it swaps to its last stage while you're looking away, so it comes from behind or beside you. (PaintingStages: untick 'Swap Sound Only On Last' to hear it on every swap.)" },
            new SlotDef { name = "Painting Soundtrack", drop = "painting_soundtrack", group = "Painting", clips = new[] { "Assets/Audio/JennyAudio.mp3" },
                db = -1.9f, pitch = 1f, spatial = true, min = 1.5f, max = 12f,
                notes = "Loops from the painting while its transformation video plays (Proximity and Gaze Dwell modes): fades in over 2 s when the video starts, out over 1.5 s on Replay or Rescan." },
        };

        [MenuItem("Memories/Audio/Set Up Sound Slots and Mixer", priority = 100)]
        public static void SetUpInteractive()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try { SetUp(); }
            finally { if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup); }
            EditorUtility.DisplayDialog("Sound design set up",
                "Created the mixer, the Sound Slots and wired up the mug scenes.\n\nOpen Memories > Sound Design to start.", "OK");
        }

        /// <summary>Entry point for batch mode: -executeMethod Memories.Audio.EditorTools.AudioSetup.SetUpBatch</summary>
        public static void SetUpBatch()
        {
            SetUp();
            Debug.Log("[AudioSetup] Batch setup finished");
        }

        public static void SetUp()
        {
            Folder(AudioPaths.Root);
            Folder(AudioPaths.Slots);
            Folder(AudioPaths.Drop);

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioPaths.Mixer) ?? CreateMixer(AudioPaths.Mixer);
            EnsureGroups(mixer);

            var slots = new Dictionary<string, SoundSlot>();
            foreach (var d in Defs) slots[d.name] = GetOrCreateSlot(d, mixer);
            AssetDatabase.SaveAssets();

            WireScene(AudioPaths.PaintingScene, slots, mixer);
            WireScene(AudioPaths.HeadsetScene, slots, mixer);
            WireScene(AudioPaths.DesktopScene, slots, mixer);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AudioSetup] Done: mixer {AudioPaths.Mixer}, {slots.Count} slots in {AudioPaths.Slots}");
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }

        static SoundSlot GetOrCreateSlot(SlotDef d, AudioMixer mixer)
        {
            string path = $"{AudioPaths.Slots}/{d.name}.asset";
            var slot = AssetDatabase.LoadAssetAtPath<SoundSlot>(path);
            if (slot != null) return slot;
            slot = ScriptableObject.CreateInstance<SoundSlot>();
            slot.notes = d.notes;
            slot.dropName = d.drop;
            foreach (var c in d.clips)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(c);
                if (clip != null) slot.clips.Add(clip);
                else Debug.LogWarning($"[AudioSetup] {d.name}: couldn't find {c}");
            }
            slot.volumeDb = d.db;
            slot.pitch = d.pitch;
            slot.output = AudioPaths.Group(mixer, d.group);
            slot.spatial = d.spatial;
            slot.minDistance = d.min;
            slot.maxDistance = d.max;
            AssetDatabase.CreateAsset(slot, path);
            Debug.Log($"[AudioSetup] Created slot {path}");
            return slot;
        }

        // ------------------------------------------------------------ mix made in the desktop app

        [MenuItem("Memories/Audio/Import Mix From Desktop App", priority = 101)]
        public static void ImportMixInteractive() => ImportMix(true);

        /// <summary>Apply ~/MemoriesOfTheFuture/SoundMix.json to the Sound Slot assets. Returns the change summary (null: no mix).</summary>
        public static string ImportMix(bool dialogs)
        {
            var mix = SoundMixFile.Load();
            if (mix == null || mix.slots == null || mix.slots.Count == 0)
            {
                Debug.LogWarning($"[SoundMix] No saved mix at {SoundMixFile.MixPath}");
                if (dialogs) EditorUtility.DisplayDialog("No saved mix", $"There's no mix to import at\n{SoundMixFile.MixPath}\n\n" +
                    "Save one from the desktop app first: Tab > Sound > Save mix.", "OK");
                return null;
            }
            var bySlot = new Dictionary<string, SoundSlot>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in AudioPaths.AllSlots()) bySlot[s.name] = s;
            var lines = new List<string>();
            var unknown = new List<string>();
            foreach (var v in mix.slots)
            {
                if (v == null || string.IsNullOrEmpty(v.name)) continue;
                if (!bySlot.TryGetValue(v.name, out var slot)) { unknown.Add(v.name); continue; }
                Undo.RecordObject(slot, "Import sound mix");
                var changed = SoundMixFile.Apply(v, slot);
                if (changed == "") continue;
                EditorUtility.SetDirty(slot);
                lines.Add($"{slot.name}: {changed}");
            }
            AssetDatabase.SaveAssets();
            string summary = lines.Count == 0 ? "Nothing changed: the slots already match the saved mix." : string.Join("\n", lines);
            if (unknown.Count > 0) summary += $"\n\nNo slot named: {string.Join(", ", unknown)}";
            Debug.Log($"[SoundMix] Imported {SoundMixFile.MixPath} (saved {mix.savedAt} from the {mix.savedBy}):\n{summary}");
            if (dialogs)
                EditorUtility.DisplayDialog($"Imported mix ({lines.Count} slot{(lines.Count == 1 ? "" : "s")} changed)",
                    $"From {SoundMixFile.MixPath}, saved {mix.savedAt}.\n\n{summary}", "OK");
            return summary;
        }

        // ------------------------------------------------------------ mixer (Unity has no public API for building one)

        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static AudioMixer CreateMixer(string path)
        {
            var t = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
            if (t == null) throw new Exception("AudioMixerController not found: create the mixer by hand (Assets > Create > Audio Mixer)");
            var mixer = (AudioMixer)t.GetMethod("CreateMixerControllerAtPath", All).Invoke(null, new object[] { path });
            var master = t.GetProperty("masterGroup", All).GetValue(mixer);
            var newGroup = t.GetMethod("CreateNewGroup", All);
            var addChild = t.GetMethod("AddChildToParent", All);
            var addToView = t.GetMethod("AddGroupToCurrentView", All);
            EnsureView(t, mixer, master);
            foreach (var name in AudioPaths.Groups)
            {
                var g = newGroup.Invoke(mixer, new object[] { name, false });
                addChild.Invoke(mixer, new[] { g, master });
                addToView.Invoke(mixer, new[] { g });
            }

            var snapshots = t.GetProperty("snapshots", All);
            var clone = t.GetMethod("CloneNewSnapshotFromTarget", All);
            ((Object)((Array)snapshots.GetValue(mixer)).GetValue(0)).name = MixDirector.SnapshotNames[0];
            for (int i = 1; i < MixDirector.SnapshotNames.Length; i++)
            {
                clone.Invoke(mixer, new object[] { false });
                var arr = (Array)snapshots.GetValue(mixer);
                ((Object)arr.GetValue(arr.Length - 1)).name = MixDirector.SnapshotNames[i];
            }
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AudioSetup] Created mixer {path}: groups {string.Join(", ", AudioPaths.Groups)}; snapshots {string.Join(", ", MixDirector.SnapshotNames)}");
            return mixer;
        }

        /// <summary>Add any group in <see cref="AudioPaths.Groups"/> the mixer doesn't have yet (e.g. Painting, added later).</summary>
        static void EnsureGroups(AudioMixer mixer)
        {
            var missing = new List<string>();
            foreach (var name in AudioPaths.Groups) if (AudioPaths.Group(mixer, name) == null) missing.Add(name);
            if (missing.Count == 0) return;
            var t = mixer.GetType();
            var master = t.GetProperty("masterGroup", All).GetValue(mixer);
            var newGroup = t.GetMethod("CreateNewGroup", All);
            var addChild = t.GetMethod("AddChildToParent", All);
            var addToView = t.GetMethod("AddGroupToCurrentView", All);
            EnsureView(t, mixer, master);
            foreach (var name in missing)
            {
                var g = newGroup.Invoke(mixer, new object[] { name, false });
                addChild.Invoke(mixer, new[] { g, master });
                addToView.Invoke(mixer, new[] { g });
            }
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AudioSetup] Added mixer group(s) {string.Join(", ", missing)} to {AssetDatabase.GetAssetPath(mixer)}");
        }

        /// <summary>A mixer made from code has no group view (the window adds one later); groups need one to be added to.</summary>
        static void EnsureView(Type t, AudioMixer mixer, object master)
        {
            var viewsProp = t.GetProperty("views", All);
            var views = (Array)viewsProp.GetValue(mixer);
            if (views != null && views.Length > 0) return;
            var viewType = t.Assembly.GetType("UnityEditor.Audio.MixerGroupView");
            object view = Activator.CreateInstance(viewType);
            var masterId = (GUID)master.GetType().GetProperty("groupID", All).GetValue(master);
            SetMember(viewType, ref view, "name", "View");
            SetMember(viewType, ref view, "guids", new[] { masterId });
            var arr = Array.CreateInstance(viewType, 1);
            arr.SetValue(view, 0);
            viewsProp.SetValue(mixer, arr);
            t.GetProperty("currentViewIndex", All).SetValue(mixer, 0);
        }

        static void SetMember(Type type, ref object boxed, string name, object value)
        {
            var f = type.GetField(name, All);
            if (f != null) { f.SetValue(boxed, value); return; }
            var p = type.GetProperty(name, All);
            if (p == null) throw new Exception($"{type.Name} has no member {name}");
            p.SetValue(boxed, value);
        }

        // ------------------------------------------------------------ scenes

        /// <summary>
        /// Point one scene's sound fields at the Sound Slots and add the "Sound Mix" MixDirector.
        /// Opens the scene (replacing the open ones) and saves it. Used by CupSpike > Create Desktop Testing Scene.
        /// </summary>
        public static void WireScene(string path)
        {
            var slots = new Dictionary<string, SoundSlot>();
            foreach (var d in Defs) slots[d.name] = null;
            WireScene(path, slots, null);
        }

        static void WireScene(string path, Dictionary<string, SoundSlot> slots, AudioMixer mixer)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) { Debug.LogWarning($"[AudioSetup] No scene {path}"); return; }
            var scene = EditorSceneManager.GetActiveScene().path == path ? EditorSceneManager.GetActiveScene()
                      : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            // Opening a scene unloads assets nothing references yet, so load the slots and mixer again.
            foreach (var key in new List<string>(slots.Keys))
                slots[key] = AssetDatabase.LoadAssetAtPath<SoundSlot>($"{AudioPaths.Slots}/{key}.asset");
            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioPaths.Mixer);
            int n = 0;
            n += Assign<MugScream>(slots, ("screamSlot", "Scream"), ("breathSlot", "Breath"), ("backgroundSlot", "Background"),
                                   ("backgroundLeftSlot", "Background Left"), ("backgroundRightSlot", "Background Right"));
            n += Assign<MugInterior>(slots, ("droneSlot", "Drone"));
            n += Assign<DreamState>(slots, ("whisperSlot", "Whisper"));
            n += Assign<PaintingStages>(slots, ("swapSlot", "Painting Scream"));
            n += Assign<PictureCanvas>(slots, ("soundtrackSlot", "Painting Soundtrack"));
            n += RemoveOldPaintingSources(scene);

            if (Object.FindFirstObjectByType<MixDirector>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject("Sound Mix");
                var so = new SerializedObject(go.AddComponent<MixDirector>());
                so.FindProperty("mixer").objectReferenceValue = mixer;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            if (n == 0) { Debug.Log($"[AudioSetup] {path}: already wired"); return; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AudioSetup] {path}: wired {n} reference(s)");
        }

        /// <summary>
        /// 01-PictureChanges played the painting's sounds from two AudioSources (SwapScream, Soundtrack);
        /// their clips and levels now live in the Painting slots, so the unused objects go.
        /// </summary>
        static int RemoveOldPaintingSources(UnityEngine.SceneManagement.Scene scene)
        {
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if ((root.name != "SwapScream" && root.name != "Soundtrack") || root.GetComponent<AudioSource>() == null ||
                    root.transform.childCount > 0 || root.GetComponents<Component>().Length != 2) continue;
                Debug.Log($"[AudioSetup] {scene.path}: removed the old '{root.name}' AudioSource (now a Sound Slot)");
                Object.DestroyImmediate(root);
                n++;
            }
            return n;
        }

        /// <summary>Point empty slot fields at the slots; leaves any field already set alone.</summary>
        static int Assign<T>(Dictionary<string, SoundSlot> slots, params (string field, string slot)[] map) where T : Component
        {
            int n = 0;
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(c);
                foreach (var (field, slot) in map)
                {
                    var p = so.FindProperty(field);
                    if (p == null) { Debug.LogWarning($"[AudioSetup] {typeof(T).Name} has no field {field}"); continue; }
                    if (p.objectReferenceValue != null) continue;
                    if (slots[slot] == null) { Debug.LogWarning($"[AudioSetup] Slot {slot} missing"); continue; }
                    p.objectReferenceValue = slots[slot];
                    n++;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return n;
        }
    }
}
