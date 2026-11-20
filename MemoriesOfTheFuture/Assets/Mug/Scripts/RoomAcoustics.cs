using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// Gives Meta XR Audio a room model (early reflections and reverb) matched to the real room, so
    /// spatialised sounds feel like they're in the room rather than inside your head. Without it the
    /// SDK logs "No Meta XR Audio Room found, setting default room".
    ///
    /// Uses MRUK's current room bounds when the scene model loads; until then (or with no scanned
    /// room) a default shoebox centred on the listener.
    /// </summary>
    [RequireComponent(typeof(MetaXRAudioRoomAcousticProperties))]
    public class RoomAcoustics : MonoBehaviour
    {
        [Tooltip("Used until MRUK has a room, or if there isn't one (m).")]
        [SerializeField] Vector3 fallbackSize = new Vector3(8f, 3f, 6f);
        [Tooltip("Clamp the scanned room to this, so a huge open-plan scan doesn't wash everything in reverb (m).")]
        [SerializeField] Vector3 maxSize = new Vector3(15f, 5f, 15f);
        [SerializeField, Range(0f, 1f)] float clutter = 0.6f;

        MetaXRAudioRoomAcousticProperties _room;

        void Awake()
        {
            _room = GetComponent<MetaXRAudioRoomAcousticProperties>();
            _room.lockPositionToListener = true;
            SetSize(fallbackSize);
            _room.clutterFactor = clutter;
        }

        void Start()
        {
            if (MRUK.Instance != null) MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
            else Debug.Log($"[RoomAcoustics] No MRUK; default room {fallbackSize}");
        }

        void OnSceneLoaded()
        {
            var room = MRUK.Instance.GetCurrentRoom();
            if (room == null) { Debug.Log($"[RoomAcoustics] No scanned room; default room {fallbackSize}"); return; }
            var b = room.GetRoomBounds();
            var size = Vector3.Min(b.size, maxSize);
            if (size.x < 1f || size.y < 1f || size.z < 1f) { Debug.Log($"[RoomAcoustics] Room bounds too small ({b.size}); keeping default"); return; }
            transform.position = b.center;
            _room.lockPositionToListener = false;
            SetSize(size);
            Debug.Log($"[RoomAcoustics] Room model from the scanned room: {size.x:0.0} x {size.y:0.0} x {size.z:0.0} m " +
                      $"(scan {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0}), centre {b.center:F1}");
        }

        void SetSize(Vector3 s)
        {
            _room.width = s.x;
            _room.height = s.y;
            _room.depth = s.z;
        }
    }
}
