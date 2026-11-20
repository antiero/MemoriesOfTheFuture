using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// Frame-rate benchmark for the dream's Gaussian splat, run on the headset from a Mac:
    ///   adb shell am start -S -n com.fiveoftwelve.memoriesofthefuture/com.unity3d.player.UnityPlayerGameActivity --es splatbench 1
    /// The app opens 02-Mug, waits for it to settle, measures a stretch with no dream, then forces
    /// the dream, holds it at full depth and measures each <see cref="DreamSplatRoom.SplatQuality"/>
    /// in turn (plus Full with the cup model still running, as the build was before). Wear the headset
    /// (it pauses when it's off your head), sit still and look towards the mug or table.
    /// Results are logged as "[SplatBench] RESULT ..."; read them with: adb logcat -s Unity | grep SplatBench
    /// </summary>
    public class SplatBench : MonoBehaviour
    {
        public const string CupScene = "02-Mug";
        const float Settle = 3f, Measure = 12f;

        /// <summary>The app was started with the splatbench extra.</summary>
        public static bool Requested { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Requested = ReadIntent();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (!Requested) return;
            Debug.Log("[SplatBench] Requested: will open " + CupScene + " and run");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static bool ReadIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                return !string.IsNullOrEmpty(intent.Call<string>("getStringExtra", "splatbench"));
            }
            catch (System.Exception e) { Debug.LogWarning("[SplatBench] Couldn't read the launch intent: " + e.Message); }
#endif
            return false;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != CupScene || FindAnyObjectByType<SplatBench>() != null) return;
            new GameObject("SplatBench").AddComponent<SplatBench>();
        }

        readonly List<float> _frames = new List<float>();
        readonly List<string> _results = new List<string>();

        IEnumerator Start()
        {
            var dream = FindAnyObjectByType<DreamState>();
            var splat = FindAnyObjectByType<DreamSplatRoom>();
            if (dream == null || splat == null) { Debug.LogError("[SplatBench] No DreamState / DreamSplatRoom in the scene"); yield break; }
            Debug.Log($"[SplatBench] Starting: device {SystemInfo.deviceModel}, display {Screen.currentResolution.refreshRateRatio.value:0} Hz, " +
                      $"eye texture {UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}");
            yield return new WaitForSecondsRealtime(10f);
            yield return Run("no dream (camera + cup model)", Measure);

            var start = splat.Quality;
            dream.KeepDreaming = true;
            dream.ForceDream = true;
            float t0 = Time.realtimeSinceStartup;
            while (dream.Depth < 1f && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            Debug.Log($"[SplatBench] Dream at depth {dream.Depth:0.00} after {Time.realtimeSinceStartup - t0:0} s");

            // As built before: everything at full, the cup model running alongside.
            splat.PauseVision = false;
            splat.Quality = DreamSplatRoom.SplatQuality.Full;
            yield return Run("Full, cup model running (before)", Settle + Measure, Settle);
            splat.PauseVision = true;
            foreach (var q in new[] { DreamSplatRoom.SplatQuality.Full, DreamSplatRoom.SplatQuality.Balanced, DreamSplatRoom.SplatQuality.Fast })
            {
                splat.Quality = q;
                yield return Run($"{q}, cup model paused ({DreamSplatRoom.Describe(q)})", Settle + Measure, Settle);
            }

            Debug.Log("[SplatBench] SUMMARY\n" + string.Join("\n", _results));
            splat.Quality = start;
            dream.KeepDreaming = false;
            dream.WakeNow();
            Debug.Log("[SplatBench] Done");
        }

        IEnumerator Run(string label, float duration, float skip = 0f)
        {
            Debug.Log($"[SplatBench] PHASE {label}");
            _frames.Clear();
            float t = 0f;
            while (t < duration)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                if (t > skip) _frames.Add(dt);
                yield return null;
            }
            if (_frames.Count == 0) yield break;
            float sum = 0f; int slow = 0;
            foreach (var f in _frames) { sum += f; if (f > 1.5f / 72f) slow++; }
            _frames.Sort();
            float p99 = _frames[Mathf.Min(_frames.Count - 1, (int)(_frames.Count * 0.99f))];
            string r = $"{label}: {_frames.Count / sum:0.0} fps avg, {sum / _frames.Count * 1000f:0.0} ms mean, " +
                       $"{p99 * 1000f:0.0} ms 99th, {slow * 100f / _frames.Count:0}% frames slower than 1.5 vsync";
            _results.Add(r);
            Debug.Log("[SplatBench] RESULT " + r);
        }
    }
}
