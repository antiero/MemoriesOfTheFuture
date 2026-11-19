using System.IO;
using PictureChanges;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace CupSpike.EditorTools
{
    /// <summary>
    /// Builds the desktop debug version of the experiences (no headset): one stand-in room with the
    /// mug on the table and the painting from 01-PictureChanges on the east wall.
    ///  - CupSpike > Create Desktop Testing Scene: (re)creates Assets/Scenes/03-DesktopTesting.unity, with
    ///    the artists' on-screen guide (DesktopUI), then points its sounds at the Sound Slots
    ///    (Memories > Audio > Set Up Sound Slots and Mixer first).
    ///  - CupSpike > Build macOS Desktop Testing App: builds only that scene to
    ///    Builds/macOS/MemoriesOfTheFuture-DesktopTesting.app.
    ///    This switches the editor to the macOS platform; switch back with
    ///    CupSpike > Switch Back To Meta Quest (or File > Build Profiles).
    /// </summary>
    public static class DesktopBuild
    {
        public const string ScenePath = "Assets/Scenes/03-DesktopTesting.unity";
        public const string AppPath = "Builds/macOS/MemoriesOfTheFuture-DesktopTesting.app";
        const string UiFolder = "Assets/CupSpike/UI";
        const string UiStyle = UiFolder + "/DesktopTesting.uss";
        const string UiTheme = UiFolder + "/DesktopTestingTheme.tss";
        const string UiPanel = UiFolder + "/DesktopTestingPanel.asset";
        const string QuestProfile = "Assets/Settings/Build Profiles/Meta Quest.asset";
        const string PaintingScene = "Assets/Scenes/01-PictureChanges.unity";
        /// <summary>The painting's board centre on the east wall (wall face at x = 3.0), and "into the wall".</summary>
        static readonly Vector3 PaintingCentre = new Vector3(2.99f, 1.45f, 1.2f);
        static readonly Vector3 PaintingInto = Vector3.right;

        [MenuItem("CupSpike/Create Desktop Testing Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 68f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.32f, -0.62f);

            // The stand-in room.
            var room = new GameObject("Room").transform;
            Box(room, "Floor", new Vector3(0, -0.05f, 0), new Vector3(6, 0.1f, 6), RoomMat("DesktopRoom_Floor", new Color(0.42f, 0.36f, 0.3f), 0.1f));
            var wall = RoomMat("DesktopRoom_Wall", new Color(0.78f, 0.76f, 0.68f), 0.05f);
            var dado = RoomMat("DesktopRoom_Dado", new Color(0.34f, 0.44f, 0.4f), 0.05f);
            Box(room, "Ceiling", new Vector3(0, 3.05f, 0), new Vector3(6, 0.1f, 6), wall);
            foreach (var (n, p, sz) in new[] { ("WallN", new Vector3(0, 0, 3.05f), new Vector3(6, 1, 0.1f)), ("WallS", new Vector3(0, 0, -3.05f), new Vector3(6, 1, 0.1f)),
                                               ("WallE", new Vector3(3.05f, 0, 0), new Vector3(0.1f, 1, 6)), ("WallW", new Vector3(-3.05f, 0, 0), new Vector3(0.1f, 1, 6)) })
            {
                Box(room, n + "_Lower", p + Vector3.up * 0.55f, new Vector3(sz.x, 1.1f, sz.z), dado);
                Box(room, n + "_Upper", p + Vector3.up * 2.05f, new Vector3(sz.x, 1.9f, sz.z), wall);
            }
            var wood = RoomMat("DesktopRoom_Table", new Color(0.66f, 0.54f, 0.38f), 0f);
            Box(room, "TableTop", new Vector3(0, 0.73f, 0.1f), new Vector3(1.5f, 0.04f, 0.75f), wood);
            foreach (var (x, z) in new[] { (-0.68f, -0.2f), (0.68f, -0.2f), (-0.68f, 0.4f), (0.68f, 0.4f) })
                Box(room, "TableLeg", new Vector3(x, 0.355f, z), new Vector3(0.05f, 0.71f, 0.05f), wood);

            // The mug.
            var root = new GameObject("CupSpikeRoot").transform;
            var proxy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CupSpike/Prefabs/ProxyMug.prefab"), scene);
            proxy.transform.SetParent(root, false);
            proxy.transform.position = new Vector3(0f, 0.75f, 0f);

            var go = new GameObject("CupDesktop");
            go.transform.SetParent(root, false);
            var det = go.AddComponent<CupDetector>();
            Set(det, ("simulate", true), ("target", proxy.transform), ("head", camGo.transform));

            var ms = go.AddComponent<MugScream>();
            Set(ms, ("detector", det), ("unlitMaterial", Mat("Assets/PictureChanges/Materials/DebugUnlit.mat")), ("showZone", false));
            var zones = new SerializedObject(ms);
            var zp = zones.FindProperty("zonePresets"); zp.arraySize = 2; zp.GetArrayElementAtIndex(0).floatValue = 0.03f; zp.GetArrayElementAtIndex(1).floatValue = 0.10f;
            zones.ApplyModifiedPropertiesWithoutUndo();

            var drain = go.AddComponent<RoomDrain>();
            UnityEventTools.AddPersistentListener(ms.onScream, drain.Drain);

            var mi = go.AddComponent<MugInterior>();
            Set(mi, ("detector", det), ("proxy", proxy.transform), ("proxyMesh", proxy.GetComponentInChildren<MeshFilter>(true)),
                ("occluderMaterial", Mat("Assets/CupSpike/Materials/MugOccluder.mat")),
                // On desktop the mug model is visible, so the tunnel must draw after it (queue > 2000).
                ("portalMaterial", After("MugPortalMask", 2010)), ("wellMaterial", After("MugWell", 2020)), ("runeMaterial", After("MugRuneGlow", 2030)));

            var ds = go.AddComponent<DreamState>();
            Set(ds, ("detector", det), ("interior", mi), ("mugScream", ms), ("domeMaterial", Mat("Assets/CupSpike/Materials/DreamDome.mat")),
                ("emberMaterial", Mat("Assets/CupSpike/Materials/DreamEmber.mat")), ("runeFloatMaterial", Mat("Assets/CupSpike/Materials/RuneFloat.mat")));
            var splatRoom = go.AddComponent<DreamSplatRoom>();
            Set(splatRoom, ("dream", ds), ("interior", mi), ("splat", AssetDatabase.LoadAssetAtPath<ScriptableObject>(DreamSplatRoom.DefaultSplatPath)));

            var handMat = Mat("Assets/CupSpike/Materials/DesktopHand.mat");
            if (handMat == null)
            {
                handMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                handMat.SetColor("_BaseColor", new Color(0.86f, 0.66f, 0.55f));
                AssetDatabase.CreateAsset(handMat, "Assets/CupSpike/Materials/DesktopHand.mat");
            }
            var (canvas, board) = AddPainting(scene, camGo.transform);

            var sim = go.AddComponent<DesktopSim>();
            Set(sim, ("detector", det), ("mugScream", ms), ("interior", mi), ("dream", ds), ("cam", cam), ("handMaterial", handMat), ("room", room),
                ("painting", canvas), ("paintingBoard", board));
            var simSo = new SerializedObject(sim);
            simSo.FindProperty("paintingCentre").vector3Value = PaintingCentre;
            simSo.FindProperty("paintingInto").vector3Value = PaintingInto;
            simSo.ApplyModifiedPropertiesWithoutUndo();

            // The artists' guide: welcome card, status, keys and the Tab panel (UI Toolkit).
            var uiGo = new GameObject("Desktop UI");
            uiGo.AddComponent<UIDocument>().panelSettings = PanelSettingsAsset();
            Set(uiGo.AddComponent<DesktopUI>(), ("sim", sim), ("style", AssetDatabase.LoadAssetAtPath<StyleSheet>(UiStyle)));
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>();

            var audioRoom = new GameObject("AudioRoom");
            audioRoom.AddComponent<MetaXRAudioRoomAcousticProperties>();
            audioRoom.AddComponent<RoomAcoustics>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[DesktopBuild] Created " + ScenePath);

            // Sound: point the mug's and the painting's sound fields at the Sound Slots, add the mix director.
            Memories.Audio.EditorTools.AudioSetup.WireScene(ScenePath);
        }

        /// <summary>The UI's panel settings: scales with the window (designed at 1600 x 900), default runtime theme.</summary>
        static PanelSettings PanelSettingsAsset()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(UiPanel);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, UiPanel);
            }
            AssetDatabase.ImportAsset(UiTheme);
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiTheme);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1600, 900);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;   // scale with the window's height
            ps.sortingOrder = 10;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        /// <summary>
        /// The painting from 01-PictureChanges, with its tuned values (board layout, trigger, stages,
        /// presence), on the east wall. DesktopSim force-locks it there at Start (no camera on a Mac).
        /// </summary>
        static (PictureCanvas, BoardPoseEstimator) AddPainting(Scene scene, Transform head)
        {
            var src = EditorSceneManager.OpenScene(PaintingScene, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);   // new objects go into the desktop scene
            try
            {
                T Find<T>() where T : Component
                {
                    foreach (var r in src.GetRootGameObjects())
                    {
                        var c = r.GetComponentInChildren<T>(true);
                        if (c != null) return c;
                    }
                    throw new System.Exception($"{PaintingScene} has no {typeof(T).Name}");
                }
                var srcBoard = Find<BoardPoseEstimator>();
                var srcCanvas = Find<PictureCanvas>();
                var srcStages = Find<PaintingStages>();
                var srcPresence = Find<ViewerPresence>();
                var srcVideo = srcCanvas.GetComponent<VideoPlayer>();
                Renderer srcQuad = null;
                foreach (var r in src.GetRootGameObjects())
                    foreach (var rend in r.GetComponentsInChildren<Renderer>(true))
                        if (rend.name == "PaintingQuad") srcQuad = rend;

                // The painting: a root the canvas moves to the board pose, and the quad it reshapes.
                var root = new GameObject("PaintingRoot").transform;
                root.position = PaintingCentre;
                root.rotation = Quaternion.LookRotation(PaintingInto, Vector3.up);
                var quad = new GameObject("PaintingQuad");
                quad.transform.SetParent(root, false);
                quad.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var quadRenderer = quad.AddComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = srcQuad != null ? srcQuad.sharedMaterial : Mat("Assets/PictureChanges/Materials/PaintingCanvas.mat");
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var go = new GameObject("PictureChanges");
                var board = go.AddComponent<BoardPoseEstimator>();
                var video = go.AddComponent<VideoPlayer>();
                var presence = go.AddComponent<ViewerPresence>();
                var canvas = go.AddComponent<PictureCanvas>();
                var stages = go.AddComponent<PaintingStages>();

                Copy(srcBoard, board, src);
                Copy(srcPresence, presence, src);
                Copy(srcCanvas, canvas, src);
                Copy(srcStages, stages, src);

                video.playOnAwake = false;
                video.renderMode = VideoRenderMode.APIOnly;
                video.audioOutputMode = VideoAudioOutputMode.None;
                video.isLooping = false;
                video.source = VideoSource.VideoClip;
                video.clip = srcVideo != null && srcVideo.clip != null ? srcVideo.clip : AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Artwork/Video/painting.mp4");

                // Every scene reference now points at the desktop objects (assets came across as they were).
                // No camera on a Mac: no detection, and no "no camera" status line every 2 s.
                Set(board, ("cameraAccess", null), ("statusInterval", 0f));
                Set(presence, ("estimator", board), ("painting", root), ("head", head));
                Set(canvas, ("estimator", board), ("canvasRoot", root), ("canvasQuad", quadRenderer), ("head", head), ("video", video),
                    ("presence", presence), ("stages", stages),
                    ("stillImage", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Artwork/Video/painting_frame0.png")));
                Set(stages, ("canvas", canvas), ("presence", presence), ("head", head));
                var so = new SerializedObject(stages);
                var list = so.FindProperty("stages");
                list.arraySize = 4;
                for (int i = 0; i < 4; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture>($"Assets/Artwork/Stages/stage{i + 1}.jpg");
                so.ApplyModifiedPropertiesWithoutUndo();

                Debug.Log($"[DesktopBuild] Painting copied from {PaintingScene} onto the east wall at {PaintingCentre}");
                return (canvas, board);
            }
            finally
            {
                EditorSceneManager.CloseScene(src, true);
            }
        }

        /// <summary>Copy a component's values from another scene, then clear any reference into that scene (set again by the caller).</summary>
        static void Copy(Component from, Component to, Scene fromScene)
        {
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(from), to);
            var so = new SerializedObject(to);
            var p = so.GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                var o = p.objectReferenceValue;
                bool inOtherScene = (o is GameObject g && g.scene == fromScene) || (o is Component c && c.gameObject.scene == fromScene);
                if (inOtherScene) p.objectReferenceValue = null;
            }
            // Event listeners pointed at objects in the other scene: drop them.
            var e = so.GetIterator();
            while (e.Next(true))
                if (e.name == "m_Calls" && e.isArray) e.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("CupSpike/Build macOS Desktop Testing App")]
        public static void BuildMac()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            // Only the Mono variant of Mac standalone support is installed.
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            // The Meta SDK references the Microphone class, so macOS requires a usage description (the app never records).
            if (string.IsNullOrEmpty(PlayerSettings.macOS.microphoneUsageDescription))
                PlayerSettings.macOS.microphoneUsageDescription = "Not used. Required by an included library.";
            Directory.CreateDirectory(Path.GetDirectoryName(AppPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = AppPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[DesktopBuild] macOS build {s.result}: {s.totalErrors} errors, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0} s -> {AppPath}");
        }

        [MenuItem("CupSpike/Switch Back To Meta Quest")]
        public static void SwitchToQuest()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Profile.BuildProfile>(QuestProfile);
            if (profile != null) UnityEditor.Build.Profile.BuildProfile.SetActiveBuildProfile(profile);
            else EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log("[DesktopBuild] Active build target: " + EditorUserBuildSettings.activeBuildTarget);
        }

        // ------------------------------------------------------------ helpers

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Material RoomMat(string name, Color colour, float grid)
        {
            var path = "Assets/CupSpike/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("CupSpike/DesktopRoom")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("CupSpike/DesktopRoom");
            m.SetColor("_Color", colour);
            m.SetFloat("_Grid", grid);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A copy of a mug-interior material that draws after the visible mug model.</summary>
        static Material After(string name, int queue)
        {
            var path = "Assets/CupSpike/Materials/" + name + "_Desktop.mat";
            var src = Mat("Assets/CupSpike/Materials/" + name + ".mat");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Mat(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

        static void Set(Object target, params (string name, object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                var p = so.FindProperty(name);
                if (p == null) { Debug.LogError($"[DesktopBuild] {target.GetType().Name} has no field '{name}'"); continue; }
                if (value == null) p.objectReferenceValue = null;
                else if (value is bool b) p.boolValue = b;
                else if (value is float f) p.floatValue = f;
                else p.objectReferenceValue = (Object)value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
