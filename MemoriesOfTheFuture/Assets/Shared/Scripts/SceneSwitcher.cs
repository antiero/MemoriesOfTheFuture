using UnityEngine;
using UnityEngine.SceneManagement;

namespace MemoriesOfTheFuture
{
    /// <summary>
    /// Switches between the app's scenes and remembers the last one, so the next launch
    /// (via the 00-Boot scene) reopens it. Every scene loaded on its own is recorded,
    /// including one opened straight from the Editor.
    /// </summary>
    public static class SceneSwitcher
    {
        public const string BootScene = "00-Boot";
        public const string DefaultScene = "01-PictureChanges";
        const string PrefKey = "MemoriesOfTheFuture.LastScene";

        static AsyncOperation _loading;

        /// <summary>The scene to open at launch: the last one used, or the default.</summary>
        public static string LastScene
        {
            get
            {
                var s = PlayerPrefs.GetString(PrefKey, DefaultScene);
                return IsLoadable(s) ? s : DefaultScene;
            }
        }

        public static string Current => SceneManager.GetActiveScene().name;
        public static bool IsLoading => _loading != null && !_loading.isDone;

        // SubsystemRegistration runs before the first scene loads, and again on each
        // Play in the Editor even with domain reload turned off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _loading = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.name == BootScene) return;
            PlayerPrefs.SetString(PrefKey, scene.name);
            PlayerPrefs.Save();   // the headset app is often killed rather than quit
        }

        /// <summary>Loads a scene by name, replacing the current one. Ignored while a load is
        /// in progress, for the scene that is already open, or for a scene not in the build.</summary>
        public static bool Load(string scene)
        {
            if (IsLoading || scene == Current || !IsLoadable(scene)) return false;
            Debug.Log($"[SceneSwitcher] Loading {scene}");
            _loading = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            return _loading != null;
        }

        static bool IsLoadable(string scene) =>
            !string.IsNullOrEmpty(scene) && scene != BootScene && Application.CanStreamedLevelBeLoaded(scene);
    }
}
