using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MemoriesOfTheFuture
{
    /// <summary>
    /// Switches between the app's scenes and remembers the last one, so the next launch
    /// (via the 00-Boot scene) reopens it. Every scene loaded on its own is recorded,
    /// including one opened straight from the Editor.
    ///
    /// A switch is eased so it doesn't jump: the new scene loads in the background while the hand
    /// menu fades out (<see cref="IsSwitching"/>), and only then is it switched in. Activating a scene
    /// stalls the app's frames for a moment while passthrough and the real hands keep moving, so
    /// anything virtual still on screen would hang frozen in the air and then snap; with the menu
    /// already gone, the stall shows only the real room.
    /// </summary>
    public static class SceneSwitcher
    {
        public const string BootScene = "00-Boot";
        public const string DefaultScene = "01-PictureChanges";
        const string PrefKey = "MemoriesOfTheFuture.LastScene";
        /// <summary>Time for the hand menu to fade out before the new scene is switched in (s).</summary>
        public const float FadeOutTime = 0.35f;

        static AsyncOperation _loading;
        static Runner _runner;

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
        /// <summary>A switch is under way: the hand menu hides and ignores presses until the new scene is in.</summary>
        public static bool IsSwitching { get; private set; }
        /// <summary>The scene being switched to, while <see cref="IsSwitching"/>.</summary>
        public static string Target { get; private set; }
        public static bool IsLoading => IsSwitching || (_loading != null && !_loading.isDone);

        // SubsystemRegistration runs before the first scene loads, and again on each
        // Play in the Editor even with domain reload turned off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _loading = null;
            _runner = null;
            IsSwitching = false;
            Target = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.name == BootScene) return;
            PlayerPrefs.SetString(PrefKey, scene.name);
            PlayerPrefs.Save();   // the headset app is often killed rather than quit
        }

        /// <summary>Switches to a scene by name, replacing the current one. Ignored while a switch is
        /// in progress, for the scene that is already open, or for a scene not in the build.</summary>
        public static bool Load(string scene)
        {
            if (IsLoading || scene == Current || !IsLoadable(scene)) return false;
            Debug.Log($"[SceneSwitcher] Loading {scene}");
            if (_runner == null)
            {
                var go = new GameObject("SceneSwitcher") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
            }
            // From the boot scene there is nothing on screen to fade.
            _runner.StartCoroutine(Switch(scene, Current == BootScene ? 0f : FadeOutTime));
            return true;
        }

        static IEnumerator Switch(string scene, float fadeOut)
        {
            IsSwitching = true;
            Target = scene;
            // Load while the menu fades, but hold the switch until it has gone.
            _loading = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (_loading == null) { IsSwitching = false; Target = null; yield break; }
            _loading.allowSceneActivation = false;
            float until = Time.realtimeSinceStartup + fadeOut;
            while (Time.realtimeSinceStartup < until || _loading.progress < 0.9f) yield return null;
            _loading.allowSceneActivation = true;
            while (!_loading.isDone) yield return null;
            IsSwitching = false;
            Target = null;
        }

        static bool IsLoadable(string scene) =>
            !string.IsNullOrEmpty(scene) && scene != BootScene && Application.CanStreamedLevelBeLoaded(scene);

        /// <summary>Runs the switch coroutine; lives across scene loads.</summary>
        class Runner : MonoBehaviour { }
    }
}
