using UnityEngine;

namespace PictureChanges
{
    /// <summary>
    /// Debug/demo actions from either controllers or tracked hands.
    ///
    ///   Action          Controller   Hand
    ///   Capture frame   A            right index pinch, hold 0.6 s
    ///   Rescan          B            left index pinch, hold 1.0 s
    ///   Toggle debug    Y            right middle-finger pinch, hold 0.6 s
    ///
    /// Each action fires once per press/pinch. Read the *Requested flags in Update;
    /// they are true for exactly one frame.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PictureChangesInput : MonoBehaviour
    {
        public static PictureChangesInput Instance { get; private set; }

        [SerializeField] OVRHand leftHand;
        [SerializeField] OVRHand rightHand;
        [SerializeField] float captureHold = 0.6f;
        [SerializeField] float rescanHold = 1.0f;
        [SerializeField] float toggleHold = 0.6f;

        public bool CaptureRequested { get; private set; }
        public bool RescanRequested { get; private set; }
        public bool ToggleDebugRequested { get; private set; }

        /// <summary>Progress (0..1) of the gesture currently being held, for UI feedback.</summary>
        public float HoldProgress { get; private set; }
        public string HoldLabel { get; private set; }

        /// <summary>Short confirmation text other components can set, shown by the debug view.</summary>
        public string Feedback { get; private set; }
        public float FeedbackTime { get; private set; } = -999f;

        readonly PinchHold _capture = new PinchHold();
        readonly PinchHold _rescan = new PinchHold();
        readonly PinchHold _toggle = new PinchHold();

        void Awake()
        {
            Instance = this;
            if (leftHand == null || rightHand == null)
            {
                foreach (var h in FindObjectsByType<OVRHand>(FindObjectsInactive.Include))
                {
                    if (h.GetHand() == OVRPlugin.Hand.HandLeft && leftHand == null) leftHand = h;
                    if (h.GetHand() == OVRPlugin.Hand.HandRight && rightHand == null) rightHand = h;
                }
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Say(string message)
        {
            Feedback = message;
            FeedbackTime = Time.time;
            Debug.Log($"[PictureChangesInput] {message}");
        }

        void Update()
        {
            bool capturePinch = Pinching(rightHand, OVRHand.HandFinger.Index) && !Pinching(rightHand, OVRHand.HandFinger.Middle);
            bool togglePinch = Pinching(rightHand, OVRHand.HandFinger.Middle);
            bool rescanPinch = Pinching(leftHand, OVRHand.HandFinger.Index);

            CaptureRequested = OVRInput.GetDown(OVRInput.Button.One) | _capture.Update(capturePinch, captureHold);
            RescanRequested = OVRInput.GetDown(OVRInput.Button.Two) | _rescan.Update(rescanPinch, rescanHold);
            ToggleDebugRequested = OVRInput.GetDown(OVRInput.Button.Four) | _toggle.Update(togglePinch, toggleHold);

            HoldProgress = 0f; HoldLabel = null;
            Track(_capture, captureHold, "Capture");
            Track(_rescan, rescanHold, "Rescan");
            Track(_toggle, toggleHold, "Debug view");
        }

        void Track(PinchHold p, float hold, string label)
        {
            if (!p.Holding || p.Fired) return;
            float progress = Mathf.Clamp01(p.Held / hold);
            if (progress > HoldProgress) { HoldProgress = progress; HoldLabel = label; }
        }

        static bool Pinching(OVRHand hand, OVRHand.HandFinger finger) =>
            hand != null && hand.IsTracked && hand.GetFingerIsPinching(finger);

        class PinchHold
        {
            public bool Holding, Fired;
            public float Held;

            /// <summary>Returns true once, when the pinch has been held long enough.</summary>
            public bool Update(bool pinching, float hold)
            {
                if (!pinching) { Holding = Fired = false; Held = 0f; return false; }
                Holding = true;
                Held += Time.deltaTime;
                if (Fired || Held < hold) return false;
                Fired = true;
                return true;
            }
        }
    }
}
