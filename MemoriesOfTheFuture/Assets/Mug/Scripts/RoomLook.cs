using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// One place to change how the real room looks (brightness, contrast, saturation, each -1..1).
    /// On the headset it drives the passthrough layer. It also sets a global shader value
    /// (_CupRoomLook) that the desktop debug room's shader reads, so the same code works with no headset.
    /// </summary>
    public static class RoomLook
    {
        static readonly int Id = Shader.PropertyToID("_CupRoomLook");

        public static void Set(OVRPassthroughLayer layer, float brightness, float contrast, float saturation)
        {
            if (layer != null) layer.SetBrightnessContrastSaturation(brightness, contrast, saturation);
            Shader.SetGlobalVector(Id, new Vector4(brightness, contrast, saturation, 1f));
        }

        public static void Clear(OVRPassthroughLayer layer)
        {
            if (layer != null) layer.DisableColorMap();
            Shader.SetGlobalVector(Id, Vector4.zero);
        }

        static readonly int EdgeId = Shader.PropertyToID("_CupRoomEdge");

        /// <summary>
        /// Glowing outlines on the real room's edges (the passthrough layer's edge rendering), in
        /// <paramref name="colour"/>; its alpha is the strength. Also sets _CupRoomEdge for the desktop room.
        /// </summary>
        public static void SetEdges(OVRPassthroughLayer layer, Color colour)
        {
            if (layer != null)
            {
                if (!layer.edgeRenderingEnabled) layer.edgeRenderingEnabled = true;
                layer.edgeColor = colour;
            }
            Shader.SetGlobalColor(EdgeId, colour);
        }

        public static void ClearEdges(OVRPassthroughLayer layer)
        {
            if (layer != null) layer.edgeRenderingEnabled = false;
            Shader.SetGlobalColor(EdgeId, Color.clear);
        }
    }
}
