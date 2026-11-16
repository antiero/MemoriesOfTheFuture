using PictureChanges.UI;
using UnityEngine;

namespace CupSpike
{
    /// <summary>The Cup scene's own hand-menu buttons (the scene row is added by SceneMenu).</summary>
    public class CupSpikeMenu : MonoBehaviour
    {
        [SerializeField] HandMenu menu;
        [SerializeField] CupDetector detector;
        [SerializeField] CupDebugView debugView;
        [SerializeField] MugScream mugScream;
        [Tooltip("Renderers of the proxy mug, hidden by the Proxy button (detection keeps running).")]
        [SerializeField] Renderer[] proxyRenderers;

        bool _proxyVisible = true;

        void Awake()
        {
            menu.AddButton("Debug", () => debugView.Toggle(), () => debugView.Visible);
            menu.AddButton("Proxy", ToggleProxy, () => _proxyVisible);
            menu.AddButton("Freeze", () => detector.Frozen = !detector.Frozen, () => detector.Frozen);
            // Cycles Auto -> depth only -> size only; lit when not Auto. The debug panel shows which.
            menu.AddButton("Dist", () => detector.Mode = (CupDetector.DistanceMode)(((int)detector.Mode + 1) % 3),
                           () => detector.Mode != CupDetector.DistanceMode.Auto);
            if (mugScream == null) return;
            menu.AddRowBreak();
            // Zone: 10 cm <-> 3 cm (lit at 3 cm). Pin: lit while pinned / placing; pinch at the mug to place.
            menu.AddButton("Zone", mugScream.CycleZone, () => mugScream.ZoneSize < 0.05f);
            menu.AddButton("Pin", mugScream.TogglePin, () => mugScream.ZoneMode != MugScream.Mode.Tracked);
            menu.AddButton("Miss", mugScream.LogMiss);
            menu.AddButton("False", mugScream.LogFalse);
        }

        void ToggleProxy()
        {
            _proxyVisible = !_proxyVisible;
            foreach (var r in proxyRenderers) if (r != null) r.enabled = _proxyVisible;
        }
    }
}
