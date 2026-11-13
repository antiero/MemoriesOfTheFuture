using UnityEngine;

namespace PictureChanges.UI
{
    /// <summary>The hand-menu buttons for the artist demo (left palm up, poke with right index).</summary>
    public class DemoMenu : MonoBehaviour
    {
        [SerializeField] HandMenu menu;
        [SerializeField] PictureCanvas canvas;
        [SerializeField] BoardDebugView debugView;
        [SerializeField] TestMode testMode;

        void Awake()
        {
            menu.AddButton("Replay", () => canvas.Replay());
            menu.AddButton("Rescan", () => { if (testMode != null && testMode.Active) testMode.Reposition(); else canvas.Rescan(); });
            menu.AddButton("Test", () => testMode.Toggle(), () => testMode != null && testMode.Active);
            menu.AddButton("Debug", () => debugView.Toggle(), () => debugView.Visible);
            menu.AddButton("Grid", () => canvas.ShowAlignmentGrid = !canvas.ShowAlignmentGrid, () => canvas.ShowAlignmentGrid);
            menu.AddButton("Capture", () => FindAnyObjectByType<BoardPoseEstimator>()?.RequestCapture());
        }
    }
}
