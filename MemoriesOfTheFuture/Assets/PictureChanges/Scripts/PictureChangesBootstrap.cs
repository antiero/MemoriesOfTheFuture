using UnityEngine;

namespace PictureChanges
{
    /// <summary>
    /// Asks for the runtime permissions the scene needs: Scene (spatial data, for anchors and
    /// room walls) and Passthrough Camera Access (for glyph detection). PassthroughCameraAccess
    /// starts playing on its own once its permission is granted.
    /// </summary>
    public class PictureChangesBootstrap : MonoBehaviour
    {
        void Start()
        {
            OVRPermissionsRequester.Request(new[]
            {
                OVRPermissionsRequester.Permission.Scene,
                OVRPermissionsRequester.Permission.PassthroughCameraAccess,
            });
        }
    }
}
