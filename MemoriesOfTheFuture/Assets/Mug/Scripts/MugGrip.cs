using System;
using Oculus.Interaction.Input;
using UnityEngine;

namespace MemoriesOfTheFuture.Mug
{
    /// <summary>
    /// Where a hand puts the mug when it holds it by the handle, with fingers through the loop.
    ///
    /// Mug space is the ProxyMug prefab: origin at the base centre, +Y up, handle on -X, body
    /// centre at x = 0. Measured from zipinkent-cambridge-mug.blend: the finger hole runs from
    /// the body wall (x = -0.040) to the handle bar (x = -0.074), 0.028-0.068 m up, centred at
    /// (-0.057, 0.048, 0.0107).
    ///
    /// Hand to mug:
    ///  - hole point = average of the index and middle proximal phalanx midpoints (the fingers
    ///    through the loop)
    ///  - mug up (+Y) = knuckle line, pinky -> index (the bar sits along the closed fingers)
    ///  - handle -> body (+X) = out of the palm (the bar is gripped between palm and fingers)
    /// Both offsets are tunable, since grips vary and hand tracking has its own offsets.
    ///
    /// This finger model is only a first guess (it decides whether a gripping hand is at the mug).
    /// Curled fingers are the least reliable joints, so while held, CupDetector follows the wrist
    /// with an offset fused from the camera and this model at the moment of pick-up.
    /// </summary>
    [Serializable]
    public class MugGrip
    {
        [Tooltip("Point in mug space where the fingers sit (the middle of the handle hole).")]
        public Vector3 holeInMug = new Vector3(-0.057f, 0.048f, 0.0107f);
        [Tooltip("Extra rotation of the mug relative to the hand (degrees), for tuning.")]
        public Vector3 rotationOffset = Vector3.zero;
        [Tooltip("Index + middle finger bend (cosine between proximal and distal segments): below this the hand counts as gripping.")]
        [Range(-1f, 1f)] public float curledBelow = 0.6f;
        [Tooltip("...and above this as open (hysteresis between the two).")]
        [Range(-1f, 1f)] public float openAbove = 0.85f;

        public struct Result
        {
            public bool tracked;
            /// <summary>Average straightness of index and middle (1 = straight, lower = more bent).</summary>
            public float straightness;
            /// <summary>Where the mug's base would be if this hand were holding it.</summary>
            public Pose mugBase;
            /// <summary>The finger point used for the hole, with the mug's rotation (for debugging).</summary>
            public Pose hole;
            /// <summary>The wrist joint's full pose: the steadiest part of the hand while gripping.</summary>
            public Pose wrist;
            public Vector3 MugCentre(float height) => mugBase.position + mugBase.rotation * (Vector3.up * (height * 0.5f));
        }

        public Result Evaluate(Hand hand, bool isLeft)
        {
            var r = new Result();
            if (hand == null || !hand.IsTrackedDataValid) return r;
            if (!Get(hand, HandJointId.HandWristRoot, out var wrist) ||
                !Get(hand, HandJointId.HandIndex1, out var i1) || !Get(hand, HandJointId.HandIndex2, out var i2) ||
                !Get(hand, HandJointId.HandIndexTip, out var it) ||
                !Get(hand, HandJointId.HandMiddle1, out var m1) || !Get(hand, HandJointId.HandMiddle2, out var m2) ||
                !Get(hand, HandJointId.HandMiddleTip, out var mt) ||
                !Get(hand, HandJointId.HandPinky1, out var p1) ||
                !hand.GetJointPose(HandJointId.HandWristRoot, out r.wrist)) return r;
            r.tracked = true;
            r.straightness = (Straightness(i1, i2, it) + Straightness(m1, m2, mt)) * 0.5f;

            var up = (i1 - p1).normalized;                       // knuckle line, towards the thumb side
            var along = (m1 - wrist).normalized;                 // wrist -> fingers
            // Out of the palm. HandMenu verified Cross(across, along) is out of the palm for the
            // left hand on device; the right hand is the mirror image, so the sign flips.
            var palm = Vector3.Cross(up, along).normalized * (isLeft ? 1f : -1f);
            var x = Vector3.ProjectOnPlane(palm, up).normalized; // handle -> body
            if (x.sqrMagnitude < 1e-6f) return r;
            var rot = Quaternion.LookRotation(Vector3.Cross(x, up), up) * Quaternion.Euler(rotationOffset);

            var hole = ((i1 + i2) * 0.5f + (m1 + m2) * 0.5f) * 0.5f;
            r.hole = new Pose(hole, rot);
            r.mugBase = new Pose(hole - rot * holeInMug, rot);
            return r;
        }

        static float Straightness(Vector3 knuckle, Vector3 mid, Vector3 tip)
        {
            var a = mid - knuckle; var b = tip - mid;
            return a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f ? 1f : Vector3.Dot(a.normalized, b.normalized);
        }

        static bool Get(Hand hand, HandJointId id, out Vector3 p)
        {
            bool ok = hand.GetJointPose(id, out var pose);
            p = pose.position;
            return ok;
        }
    }
}
