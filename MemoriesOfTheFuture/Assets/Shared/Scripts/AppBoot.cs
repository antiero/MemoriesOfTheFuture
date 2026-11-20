using UnityEngine;

namespace MemoriesOfTheFuture
{
    /// <summary>Lives in 00-Boot (build index 0): opens the scene that was used last.</summary>
    public class AppBoot : MonoBehaviour
    {
        void Start()
        {
            // The splat benchmark (adb ... --es splatbench 1) always opens the cup scene.
            var scene = Mug.SplatBench.Requested ? Mug.SplatBench.CupScene : SceneSwitcher.LastScene;
            Debug.Log($"[AppBoot] Opening last scene: {scene}");
            if (!SceneSwitcher.Load(scene)) SceneSwitcher.Load(SceneSwitcher.DefaultScene);
        }
    }
}
