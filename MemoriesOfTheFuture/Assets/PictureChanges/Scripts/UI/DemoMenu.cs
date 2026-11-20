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

        // Artists see Reset, Animate, Rescan, Test and Trigger. Debug, Grid and Capture are developer buttons:
        // tick Developer Buttons on the HandMenu to show them.
        void Awake()
        {
            // Reset: back to the first still (video stopped, look-away stages from the start).
            menu.AddButton("Reset", () => { canvas.Replay(); PictureChangesInput.Instance?.Say("Reset"); });
            // Animate: play the transformation video from the start now, in any trigger mode.
            menu.AddButton("Animate", () => PictureChangesInput.Instance?.Say(canvas.Animate() ? "Animating" : "Not placed yet"),
                           () => canvas.IsPlaying);
            menu.AddButton("Rescan", () => { if (testMode != null && testMode.Active) testMode.Reposition(); else canvas.Rescan(); });
            menu.AddButton("Test", () => testMode.Toggle(), () => testMode != null && testMode.Active);
            menu.AddButton("Debug", () => debugView.Toggle(), () => debugView.Visible, developer: true);
            menu.AddButton("Grid", () => canvas.ShowAlignmentGrid = !canvas.ShowAlignmentGrid, () => canvas.ShowAlignmentGrid, developer: true);
            menu.AddButton("Capture", () => FindAnyObjectByType<BoardPoseEstimator>()?.RequestCapture(), developer: true);
            // Trigger: unseen swap (default) -> proximity -> gaze dwell. Lit when not the default.
            menu.AddButton("Trigger", () =>
            {
                canvas.CycleMode();
                PictureChangesInput.Instance?.Say("Trigger: " + PictureCanvas.ModeName(canvas.Mode));
            }, () => canvas.Mode != PictureCanvas.TriggerMode.UnseenSwap);
        }
    }
}
