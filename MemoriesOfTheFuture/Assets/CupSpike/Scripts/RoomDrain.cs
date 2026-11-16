using System.Collections;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// "Drain and crush" (concepts PDF, Vision slips): for a moment the room loses its colour and the
    /// contrast goes harsh, like a photocopy, then recovers. Uses the passthrough layer's built-in
    /// colour adjustment (no camera reprocessing). Hooked to MugScream.onScream.
    /// </summary>
    public class RoomDrain : MonoBehaviour
    {
        [SerializeField] OVRPassthroughLayer passthrough;
        [Tooltip("Peak adjustment, each -1..1: brightness, contrast, saturation.")]
        [SerializeField] Vector3 peak = new Vector3(-0.15f, 0.65f, -1f);
        [SerializeField] float attack = 0.12f;
        [SerializeField] float hold = 0.9f;
        [SerializeField] float release = 1.6f;

        Coroutine _running;

        void Reset() => passthrough = FindAnyObjectByType<OVRPassthroughLayer>();

        public void Drain()
        {
            if (passthrough == null) { Debug.LogWarning("[RoomDrain] No OVRPassthroughLayer"); return; }
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Debug.Log("[RoomDrain] Drain");
            for (float t = 0f; t < attack; t += Time.deltaTime) { Apply(t / attack); yield return null; }
            Apply(1f);
            yield return new WaitForSeconds(hold);
            for (float t = 0f; t < release; t += Time.deltaTime) { Apply(1f - Mathf.SmoothStep(0f, 1f, t / release)); yield return null; }
            passthrough.DisableColorMap();
            _running = null;
        }

        void Apply(float k) => passthrough.SetBrightnessContrastSaturation(peak.x * k, peak.y * k, peak.z * k);

        void OnDisable()
        {
            if (passthrough != null) passthrough.DisableColorMap();
        }
    }
}
