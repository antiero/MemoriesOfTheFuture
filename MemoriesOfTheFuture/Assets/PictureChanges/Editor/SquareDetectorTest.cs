using System.IO;
using System.Text;
using PictureChanges.Vision;
using UnityEditor;
using UnityEngine;

namespace PictureChanges.EditorTools
{
    /// <summary>
    /// Runs SquareMarkerDetector + PlanarPose on headset captures (pulled into DebugOutput/headset)
    /// and writes annotated PNGs to DebugOutput/. Pose uses the capture's camera.json intrinsics.
    ///
    /// RunSynthetic draws marker squares digitally around given glyph positions first, so the
    /// pipeline can be tested before the real squares are drawn on the board.
    /// </summary>
    public static class SquareDetectorTest
    {
        const string OutputFolder = "DebugOutput";

        [MenuItem("Tools/Picture Changes/Run Square Detector On Headset Captures")]
        public static void RunMenu() => Debug.Log(Run("DebugOutput/headset"));

        public static string Run(string folder, SquareMarkerDetector.Settings settings = null)
        {
            var report = new StringBuilder();
            foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                if (file.Contains("_squares")) continue;
                var gray = Load(file, out int w, out int h);
                report.Append(Analyse(Path.GetFileNameWithoutExtension(file), gray, w, h, Intrinsics(file, w, h), settings));
            }
            return report.ToString();
        }

        /// <summary>Draws squares (outer side and line width in px) centred on the given points, then analyses.</summary>
        public static string RunSynthetic(string file, Vector2[] centres, float outerPx, float linePx,
                                          SquareMarkerDetector.Settings settings = null)
        {
            var gray = Load(file, out int w, out int h);
            foreach (var c in centres)
            {
                float half = outerPx * 0.5f, inner = half - linePx;
                for (int y = Mathf.FloorToInt(c.y - half); y <= Mathf.CeilToInt(c.y + half); y++)
                for (int x = Mathf.FloorToInt(c.x - half); x <= Mathf.CeilToInt(c.x + half); x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    float dx = Mathf.Abs(x - c.x), dy = Mathf.Abs(y - c.y);
                    if (dx > half || dy > half) continue;
                    int i = y * w + x;
                    if (dx > inner || dy > inner) gray[i] = 25;                 // ink ring
                    else if (gray[i] > 90) gray[i] = (byte)Mathf.Max(gray[i], 200); // paper inside, glyph strokes kept
                }
            }
            return Analyse(Path.GetFileNameWithoutExtension(file) + "_synthetic", gray, w, h, Intrinsics(file, w, h), settings);
        }

        /// <summary>
        /// Pastes a flat board scan into a headset frame at the size it would appear from each
        /// distance (camera fx ~869 px at 1280x960), runs detector + pose, and reports the error.
        /// </summary>
        /// <param name="scanSpacingPx">TL->TR marker spacing in the scan, pixels.</param>
        public static string SimulateDistances(string scanFile, string backgroundFile, float spacingMm,
                                               float scanSpacingPx, float[] distances, SquareMarkerDetector.Settings settings = null)
        {
            const float fx = 869f, cx = 641f, cy = 484f;
            var bg = Load(backgroundFile, out int W, out int H);
            var scan = Load(scanFile, out int sw, out int sh);
            var layout = BoardLayout.Default();
            settings = settings ?? new SquareMarkerDetector.Settings();
            var report = new StringBuilder();
            foreach (var dist in distances)
            {
                float scale = spacingMm * fx / (dist * 1000f) / scanSpacingPx;
                int tw = Mathf.RoundToInt(sw * scale), th = Mathf.RoundToInt(sh * scale);
                int ox = W / 2 - tw / 2, oy = H / 2 - th / 2;
                var g = (byte[])bg.Clone();
                for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    // Area-average (box filter) downsample, like a camera pixel integrating the scene.
                    int y0 = (int)(y / scale), y1 = Mathf.Min(sh, Mathf.CeilToInt((y + 1) / scale));
                    int x0 = (int)(x / scale), x1 = Mathf.Min(sw, Mathf.CeilToInt((x + 1) / scale));
                    int sum = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { sum += scan[yy * sw + xx]; n++; }
                    int gx = ox + x, gy = oy + y;
                    if (n > 0 && gx >= 0 && gy >= 0 && gx < W && gy < H) g[gy * W + gx] = (byte)(sum / n);
                }

                var r = SquareMarkerDetector.Detect(g, W, H, settings, true);
                report.Append($"{dist:F1} m: marker interior ~{56 * scale:F1} px, line ~{5 * scale:F1} px -> candidates {r.candidates.Count}, ");
                if (!r.found) { report.AppendLine("NOT FOUND"); continue; }
                report.Append(r.missingMarker >= 0 ? $"found with 3 markers (missing {"TL,TR,BR,BL".Split(',')[r.missingMarker]})" : "found with 4 markers");

                var img = new System.Collections.Generic.List<Vector2>();
                var brd = new System.Collections.Generic.List<Vector2>();
                var all = layout.MarkerCorners;
                for (int m = 0; m < 4; m++)
                {
                    if (r.markerCorners[m] == null) continue;
                    for (int j = 0; j < 4; j++)
                    {
                        var p = r.markerCorners[m][j];
                        img.Add(new Vector2((p.x - cx) / fx, (p.y - cy) / fx));
                        brd.Add(all[m * 4 + j]);
                    }
                }
                if (PlanarPose.Solve(brd.ToArray(), img.ToArray(), out var pose))
                    report.Append($", pose {pose.position.magnitude:F3} m (error {(pose.position.magnitude - dist) * 1000f:F0} mm), " +
                                  $"view {Vector3.Angle(pose.intoWall, pose.position):F1} deg");
                report.AppendLine();
            }
            return report.ToString();
        }

        static byte[] Load(string file, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(file));
            var gray = GlyphDetector.ToGray(tex.GetPixels32(), tex.width, tex.height, 1, out w, out h);
            Object.DestroyImmediate(tex);
            return gray;
        }

        /// <summary>
        /// Intrinsics for the capture image. camera.json gives them at sensor resolution
        /// (square sensor); the image is the sensor scaled to the image width, cropped vertically.
        /// </summary>
        static Vector4 Intrinsics(string file, int w, int h)
        {
            var json = Path.Combine(Path.GetDirectoryName(file), "camera.json");
            if (!File.Exists(json)) return new Vector4(0.68f * w, 0.68f * w, w * 0.5f, h * 0.5f);
            var j = JsonUtility.FromJson<CameraJson>(File.ReadAllText(json));
            float scale = w / (float)j.sensorWidth;
            float cropY = (j.sensorHeight * scale - h) * 0.5f;
            return new Vector4(j.fx * scale, j.fy * scale, j.cx * scale, j.cy * scale - cropY);
        }

        [System.Serializable]
        class CameraJson { public float fx, fy, cx, cy; public int sensorWidth, sensorHeight; }

        static string Analyse(string name, byte[] gray, int w, int h, Vector4 k, SquareMarkerDetector.Settings settings)
        {
            var report = new StringBuilder();
            settings = settings ?? new SquareMarkerDetector.Settings();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = SquareMarkerDetector.Detect(gray, w, h, settings, true);
            sw.Stop();
            report.AppendLine($"{name}: {sw.ElapsedMilliseconds} ms, marker candidates {r.candidates.Count}, found {r.found}");

            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) { byte g = gray[i]; px[i] = new Color32(g, g, g, 255); }
            foreach (var c in r.candidates) Box(px, w, h, c.centre, c.size * 0.6f, new Color32(255, 210, 0, 255));

            if (r.found)
            {
                var colours = new[] { new Color32(255, 40, 40, 255), new Color32(40, 255, 40, 255), new Color32(60, 140, 255, 255), new Color32(255, 40, 255, 255) };
                for (int m = 0; m < 4; m++)
                {
                    Line(px, w, h, r.corners[m], r.corners[(m + 1) % 4], colours[m]);
                    var sq = r.markerCorners[m];
                    if (sq == null) continue;
                    for (int j = 0; j < 4; j++) Line(px, w, h, sq[j], sq[(j + 1) % 4], colours[m]);
                }

                // Only markers actually seen (3 or 4) go into the pose.
                var all = BoardLayout.Default().MarkerCorners;
                var imgL = new System.Collections.Generic.List<Vector2>();
                var brdL = new System.Collections.Generic.List<Vector2>();
                for (int m = 0; m < 4; m++)
                {
                    if (r.markerCorners[m] == null) continue;
                    for (int j = 0; j < 4; j++) { imgL.Add(r.markerCorners[m][j]); brdL.Add(all[m * 4 + j]); }
                }
                var img = imgL.ToArray(); var board = brdL.ToArray();
                var norm = new Vector2[img.Length];
                for (int i = 0; i < img.Length; i++) norm[i] = new Vector2((img[i].x - k.z) / k.x, (img[i].y - k.w) / k.y);
                if (PlanarPose.Solve(board, norm, out var pose))
                {
                    float view = Vector3.Angle(pose.intoWall, pose.position);
                    float tilt = Vector3.Angle(pose.up, Vector3.up);
                    // Reprojection error of the corners used, in pixels.
                    float err = 0f;
                    for (int i = 0; i < img.Length; i++)
                    {
                        var p = pose.position + pose.right * board[i].x + pose.up * board[i].y;
                        var proj = new Vector2(p.x / p.z * k.x + k.z, p.y / p.z * k.y + k.w);
                        err += (proj - img[i]).sqrMagnitude;
                    }
                    report.AppendLine($"   {img.Length / 4} markers; pose: distance {pose.position.magnitude:F2} m, view angle {view:F1} deg, " +
                                      $"board roll vs camera {tilt:F1} deg, reprojection RMS {Mathf.Sqrt(err / img.Length):F2} px");
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            string outPath = Path.Combine(OutputFolder, name + "_squares.png");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            report.AppendLine($"   wrote {outPath}");
            return report.ToString();
        }

        static void Box(Color32[] px, int w, int h, Vector2 c, float half, Color32 col)
        {
            var a = c + new Vector2(-half, -half); var b = c + new Vector2(half, -half);
            var d = c + new Vector2(half, half); var e = c + new Vector2(-half, half);
            Line(px, w, h, a, b, col); Line(px, w, h, b, d, col); Line(px, w, h, d, e, col); Line(px, w, h, e, a, col);
        }

        static void Line(Color32[] px, int w, int h, Vector2 a, Vector2 b, Color32 col)
        {
            int steps = Mathf.CeilToInt((b - a).magnitude);
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
                int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = col;
            }
        }
    }
}
