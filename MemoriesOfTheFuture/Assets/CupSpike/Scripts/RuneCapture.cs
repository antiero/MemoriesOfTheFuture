using System;
using System.IO;
using Meta.XR;
using UnityEngine;

namespace CupSpike
{
    /// <summary>
    /// Records headset camera frames while the visitor looks into the mug, to tune a rune detector
    /// (step 2) on real lighting and angles. Toggled by the Rec button; off by default (encoding a
    /// frame causes a small hitch).
    ///
    /// Saves every <see cref="interval"/> s while looking in, up to <see cref="maxFrames"/> per
    /// session: frame_NNN.jpg plus a line in frames.csv with the camera pose, the mug pose and the
    /// cup box. Folder: Application.persistentDataPath/RuneCaptures/&lt;session&gt;. Pull with:
    /// adb pull /sdcard/Android/data/com.fiveoftwelve.memoriesofthefuture/files/RuneCaptures
    /// </summary>
    public class RuneCapture : MonoBehaviour
    {
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] CupDetector detector;
        [SerializeField] MugInterior interior;
        [SerializeField] float interval = 0.5f;
        [SerializeField] int maxFrames = 60;

        public bool Recording { get; private set; }
        public int Saved { get; private set; }

        string _dir;
        float _last = -99f;
        Texture2D _tex;

        public void Toggle()
        {
            Recording = !Recording;
            if (Recording && _dir == null)
            {
                _dir = Path.Combine(Application.persistentDataPath, "RuneCaptures", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_dir);
                File.WriteAllText(Path.Combine(_dir, "frames.csv"),
                    "file,time,camPos,camRot,mugPos,mugRot,cupScore,boxXMin,boxYMin,boxXMax,boxYMax,source\n");
            }
            Debug.Log(Recording ? $"[RuneCapture] Recording to {_dir} (while looking in)" : $"[RuneCapture] Stopped, {Saved} frames saved");
        }

        void Update()
        {
            if (!Recording || interior == null || !interior.LookingIn || Saved >= maxFrames) return;
            if (cameraAccess == null || !cameraAccess.IsPlaying || !cameraAccess.IsUpdatedThisFrame) return;
            if (Time.time - _last < interval) return;
            _last = Time.time;

            var res = cameraAccess.CurrentResolution;
            var colors = cameraAccess.GetColors();
            if (!colors.IsCreated || colors.Length != res.x * res.y) return;
            if (_tex == null || _tex.width != res.x || _tex.height != res.y) _tex = new Texture2D(res.x, res.y, TextureFormat.RGBA32, false);
            _tex.SetPixelData(colors, 0);
            _tex.Apply(false);
            string name = $"frame_{Saved:000}.jpg";
            File.WriteAllBytes(Path.Combine(_dir, name), _tex.EncodeToJPG(90));

            var cam = cameraAccess.GetCameraPose();
            var mug = detector != null ? detector.CupPose : default;
            var box = detector != null ? detector.CupViewportRect : default;
            File.AppendAllText(Path.Combine(_dir, "frames.csv"),
                $"{name},{Time.time:F2},{V(cam.position)},{Q(cam.rotation)},{V(mug.position)},{Q(mug.rotation)}," +
                $"{(detector != null ? detector.CupScore : 0):F2},{box.xMin:F3},{box.yMin:F3},{box.xMax:F3},{box.yMax:F3},{detector?.TrackingSource}\n");
            Saved++;
            if (Saved == maxFrames) Debug.Log($"[RuneCapture] Reached {maxFrames} frames in {_dir}");
        }

        static string V(Vector3 v) => $"{v.x:F4} {v.y:F4} {v.z:F4}";
        static string Q(Quaternion q) => $"{q.x:F4} {q.y:F4} {q.z:F4} {q.w:F4}";
    }
}
