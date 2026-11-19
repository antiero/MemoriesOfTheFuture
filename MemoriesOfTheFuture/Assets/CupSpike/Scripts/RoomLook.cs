using UnityEngine;

namespace CupSpike
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
    }
}
