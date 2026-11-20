using System;
using PictureChanges.UI;
using UnityEngine;

namespace MemoriesOfTheFuture
{
    /// <summary>
    /// Adds a row of scene buttons to the left hand menu. The open scene's button is lit;
    /// pressing another one switches to it (and it becomes the scene the app opens next time).
    /// </summary>
    public class SceneMenu : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string label;
            [Tooltip("Scene name as listed in Build Settings.")]
            public string scene;
        }

        [SerializeField] HandMenu menu;
        [SerializeField] Entry[] scenes =
        {
            new Entry { label = "Painting", scene = "01-PictureChanges" },
            new Entry { label = "Mug", scene = "02-Mug" },
        };

        // Start, not Awake, so the scene's own buttons (added in Awake) come first.
        void Start()
        {
            menu.AddRowBreak();
            foreach (var e in scenes)
            {
                var scene = e.scene;
                menu.AddButton(e.label, () => SceneSwitcher.Load(scene), () => SceneSwitcher.Current == scene);
            }
        }
    }
}
