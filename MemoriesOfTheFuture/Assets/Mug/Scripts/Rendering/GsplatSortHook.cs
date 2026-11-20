using Gsplat;
using UnityEngine;
using UnityEngine.Rendering;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// Sorts the Gaussian splats (Gsplat package) for each camera before URP renders it, outside the
    /// render graph. Stands in for the package's GsplatURPFeature, which this project can't use:
    /// both URP renderers run in Tile-Only Mode (on-tile rendering for Quest), and any compute or
    /// unsafe pass inside the render graph breaks it (the frame isn't drawn and URP logs
    /// "Render Graph Execution error"). Here the sort is queued on the render context just before
    /// the camera's graph runs, so the renderers stay untouched. The splats then draw in the
    /// transparent queue as usual.
    /// </summary>
    public static class GsplatSortHook
    {
        static CommandBuffer _cmd;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            RenderPipelineManager.beginCameraRendering -= Sort;
            RenderPipelineManager.beginCameraRendering += Sort;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit() => Init();
#endif

        static void Sort(ScriptableRenderContext context, Camera cam)
        {
            var sorter = GsplatSorter.Instance;
            if (!sorter.Valid || !sorter.GatherGsplatsForCamera(cam) || !GsplatSettings.Instance.Valid) return;
            _cmd ??= new CommandBuffer { name = GsplatSorter.k_passName };
            _cmd.Clear();
            sorter.DispatchSort(_cmd, cam);
            context.ExecuteCommandBuffer(_cmd);
        }
    }
}
