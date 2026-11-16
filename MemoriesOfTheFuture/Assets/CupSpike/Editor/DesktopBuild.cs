using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CupSpike.EditorTools
{
    /// <summary>
    /// Builds the desktop debug version of the mug experience (no headset).
    ///  - CupSpike > Create Desktop Scene: (re)creates Assets/Scenes/03-CupDesktop.unity.
    ///  - CupSpike > Build macOS Debug App: builds only that scene to Builds/macOS/MugThatScreams-Debug.app.
    ///    This switches the editor to the macOS platform; switch back with
    ///    CupSpike > Switch Back To Meta Quest (or File > Build Profiles).
    /// </summary>
    public static class DesktopBuild
    {
        public const string ScenePath = "Assets/Scenes/03-CupDesktop.unity";
        public const string AppPath = "Builds/macOS/MugThatScreams-Debug.app";
        const string QuestProfile = "Assets/Settings/Build Profiles/Meta Quest.asset";

        [MenuItem("CupSpike/Create Desktop Scene")]
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
            Set(ms, ("detector", det), ("scream", Clip("Assets/Audio/scream.mp3")), ("breath", Clip("Assets/CupSpike/Audio/Breath_placeholder.wav")),
                ("background", Clip("Assets/Audio/mugAudio.mp3")), ("backgroundLeft", Clip("Assets/Audio/mugAudio_L.mp3")), ("backgroundRight", Clip("Assets/Audio/mugAudio_R.mp3")),
                ("unlitMaterial", Mat("Assets/PictureChanges/Materials/DebugUnlit.mat")), ("showZone", false));
            var zones = new SerializedObject(ms);
            var zp = zones.FindProperty("zonePresets"); zp.arraySize = 2; zp.GetArrayElementAtIndex(0).floatValue = 0.03f; zp.GetArrayElementAtIndex(1).floatValue = 0.10f;
            zones.ApplyModifiedPropertiesWithoutUndo();

            var drain = go.AddComponent<RoomDrain>();
            UnityEventTools.AddPersistentListener(ms.onScream, drain.Drain);

            var mi = go.AddComponent<MugInterior>();
            Set(mi, ("detector", det), ("proxy", proxy.transform), ("proxyMesh", proxy.GetComponentInChildren<MeshFilter>(true)),
                ("occluderMaterial", Mat("Assets/CupSpike/Materials/MugOccluder.mat")),
                // On desktop the mug model is visible, so the tunnel must draw after it (queue > 2000).
                ("portalMaterial", After("MugPortalMask", 2010)), ("wellMaterial", After("MugWell", 2020)), ("runeMaterial", After("MugRuneGlow", 2030)),
                ("drone", Clip("Assets/CupSpike/Audio/Breath_placeholder.wav")));

            var ds = go.AddComponent<DreamState>();
            Set(ds, ("detector", det), ("interior", mi), ("mugScream", ms), ("domeMaterial", Mat("Assets/CupSpike/Materials/DreamDome.mat")),
                ("emberMaterial", Mat("Assets/CupSpike/Materials/DreamEmber.mat")), ("runeFloatMaterial", Mat("Assets/CupSpike/Materials/RuneFloat.mat")),
                ("whisper", Clip("Assets/CupSpike/Audio/Breath_placeholder.wav")));

            var handMat = Mat("Assets/CupSpike/Materials/DesktopHand.mat");
            if (handMat == null)
            {
                handMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                handMat.SetColor("_BaseColor", new Color(0.86f, 0.66f, 0.55f));
                AssetDatabase.CreateAsset(handMat, "Assets/CupSpike/Materials/DesktopHand.mat");
            }
            var sim = go.AddComponent<DesktopSim>();
            Set(sim, ("detector", det), ("mugScream", ms), ("interior", mi), ("dream", ds), ("cam", cam), ("handMaterial", handMat));

            var audioRoom = new GameObject("AudioRoom");
            audioRoom.AddComponent<MetaXRAudioRoomAcousticProperties>();
            audioRoom.AddComponent<RoomAcoustics>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[DesktopBuild] Created " + ScenePath);
        }

        [MenuItem("CupSpike/Build macOS Debug App")]
        public static void BuildMac()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            // Only the Mono variant of Mac standalone support is installed.
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
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
        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        static void Set(Object target, params (string name, object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                var p = so.FindProperty(name);
                if (p == null) { Debug.LogError($"[DesktopBuild] {target.GetType().Name} has no field '{name}'"); continue; }
                if (value is bool b) p.boolValue = b;
                else if (value is float f) p.floatValue = f;
                else p.objectReferenceValue = (Object)value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
