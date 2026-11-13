using System.IO;
using System.Text;
using PictureChanges.Vision;
using UnityEditor;
using UnityEngine;

namespace PictureChanges.EditorTools
{
    /// <summary>
    /// Runs the glyph detector and planar pose solver on the reference photos in
    /// Assets/Artwork, and writes annotated PNGs to DebugOutput/ (outside Assets).
    /// Use it to tune GlyphDetector.Settings before testing on the headset.
    /// </summary>
    public static class GlyphDetectorPhotoTest
    {
        const string ArtworkFolder = "Assets/Artwork";
        const string OutputFolder = "DebugOutput";

        // Phone photos: approximate focal length as a fraction of the long side (about 26 mm equivalent).
        const float PhoneFocalOverLongSide = 0.72f;

        [MenuItem("Tools/Picture Changes/Run Glyph Detector On Photos")]
        public static void RunMenu() => Debug.Log(Run());

        [MenuItem("Tools/Picture Changes/Run Glyph Detector On Headset Captures")]
        public static void RunCapturesMenu() => Debug.Log(Run(1280, "DebugOutput/headset", "*.png"));

        public static string Run(int targetWidth = 1024) => Run(targetWidth, ArtworkFolder, "*.jpg");

        /// <param name="folder">Searched recursively. Headset captures: pull them into DebugOutput/headset.</param>
        public static string Run(int targetWidth, string folder, string pattern, GlyphDetector.Settings settings = null)
        {
            Directory.CreateDirectory(OutputFolder);
            var report = new StringBuilder();
            settings = settings ?? new GlyphDetector.Settings();
            var board = BoardLayout.Default();

            foreach (var file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
            {
                if (file.EndsWith("_detect.png")) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(file));
                int step = Mathf.Max(1, tex.width / targetWidth);
                var gray = GlyphDetector.ToGray(tex.GetPixels32(), tex.width, tex.height, step, out int w, out int h);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = GlyphDetector.Detect(gray, w, h, settings, originBottomLeft: true);
                sw.Stop();

                report.AppendLine($"{Path.GetFileName(file)}  {w}x{h} (step {step})  {sw.ElapsedMilliseconds} ms  " +
                                  $"candidates={result.candidates.Count}  found={result.found}");

                var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = new Color32[w * h];
                for (int i = 0; i < px.Length; i++) { byte g = gray[i]; px[i] = new Color32(g, g, g, 255); }

                foreach (var c in result.candidates) DrawBox(px, w, h, c.centre, c.size * 0.6f, new Color32(255, 200, 0, 255));

                if (result.found)
                {
                    string[] names = { "TL", "TR", "BR", "BL" };
                    var colours = new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 120, 255, 255), new Color32(255, 0, 255, 255) };
                    for (int k = 0; k < 4; k++)
                    {
                        DrawBox(px, w, h, result.corners[k], result.glyphSize * 0.9f, colours[k]);
                        DrawLine(px, w, h, result.corners[k], result.corners[(k + 1) % 4], colours[k]);
                        report.AppendLine($"   {names[k]} at ({result.corners[k].x:F1}, {result.corners[k].y:F1})");
                    }

                    // Pose sanity check with an assumed phone focal length.
                    float f = PhoneFocalOverLongSide * Mathf.Max(w, h);
                    var cx = w * 0.5f; var cy = h * 0.5f;
                    var norm = new Vector2[4];
                    for (int k = 0; k < 4; k++) norm[k] = new Vector2((result.corners[k].x - cx) / f, (result.corners[k].y - cy) / f);
                    if (PlanarPose.Solve(board.MarkerCentres, norm, out var pose))
                    {
                        float tilt = Vector3.Angle(-pose.intoWall, -pose.position);
                        report.AppendLine($"   pose: distance {pose.position.magnitude:F2} m, board tilt vs view {tilt:F1} deg " +
                                          "(board marker-centre geometry, phone focal)");
                    }
                }

                outTex.SetPixels32(px);
                outTex.Apply();
                string outPath = Path.Combine(OutputFolder, Path.GetFileNameWithoutExtension(file) + "_detect.png");
                File.WriteAllBytes(outPath, outTex.EncodeToPNG());
                report.AppendLine($"   wrote {outPath}");
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(outTex);
            }
            return report.ToString();
        }

        static void DrawBox(Color32[] px, int w, int h, Vector2 c, float half, Color32 col)
        {
            int x0 = Mathf.RoundToInt(c.x - half), x1 = Mathf.RoundToInt(c.x + half);
            int y0 = Mathf.RoundToInt(c.y - half), y1 = Mathf.RoundToInt(c.y + half);
            for (int t = 0; t < 3; t++)
            {
                for (int x = x0; x <= x1; x++) { Set(px, w, h, x, y0 + t, col); Set(px, w, h, x, y1 - t, col); }
                for (int y = y0; y <= y1; y++) { Set(px, w, h, x0 + t, y, col); Set(px, w, h, x1 - t, y, col); }
            }
        }

        static void DrawLine(Color32[] px, int w, int h, Vector2 a, Vector2 b, Color32 col)
        {
            int steps = Mathf.CeilToInt((b - a).magnitude);
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
                Set(px, w, h, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), col);
                Set(px, w, h, Mathf.RoundToInt(p.x) + 1, Mathf.RoundToInt(p.y), col);
            }
        }

        static void Set(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c;
        }
    }
}
