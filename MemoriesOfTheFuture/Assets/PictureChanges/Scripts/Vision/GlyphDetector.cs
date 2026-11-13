using System;
using System.Collections.Generic;
using UnityEngine;

namespace PictureChanges.Vision
{
    /// <summary>
    /// Finds the four hand-drawn corner glyphs on the board in a greyscale camera image.
    ///
    /// A glyph is "dark ink strokes sitting on a small white paper patch". We look for
    /// clusters of dark pixels whose surrounding ring is clean, bright paper, then pick
    /// the four candidates that best form the board's corner quad.
    ///
    /// Corners are labelled by geometry (the board is assumed roughly upright on a wall),
    /// not by recognising each glyph. Glyph recognition is a later refinement.
    ///
    /// Pure C#, no Unity objects touched, so it is safe to run on a worker thread.
    /// </summary>
    public static class GlyphDetector
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("Ink must be this much darker than the local mean (0.3 = 30% darker).")]
            public float inkContrast = 0.30f;
            [Tooltip("Local-mean window as a fraction of image width.")]
            public float localWindowFraction = 0.03f;
            [Tooltip("Dilation radius (px) used to join a glyph's strokes into one blob.")]
            public int joinRadius = 2;
            [Tooltip("Glyph blob side length limits, as a fraction of image width.")]
            public float minSideFraction = 0.006f, maxSideFraction = 0.12f;
            [Tooltip("Fraction of the glyph bounding box covered by ink.")]
            public float minInkFill = 0.06f, maxInkFill = 0.55f;
            [Tooltip("Ring around the glyph, as a fraction of its size, that must be clean paper.")]
            public float ringFraction = 0.15f;
            [Tooltip("Max ink fraction allowed on the two cleanest (inner) ring sides.")]
            public float maxRingInk = 0.08f;
            [Tooltip("Paper must be at least this many grey levels brighter than the ink.")]
            public float minPaperInkDelta = 60f;
            [Tooltip("Keep this many best candidates when searching for the quad.")]
            public int maxCandidates = 24;
            [Tooltip("Expected width / height of the glyph-centre rectangle on the board.")]
            public float expectedAspect = 0.755f;
            [Tooltip("How far the image-space aspect may stray from expected (perspective), as a ratio.")]
            public float aspectTolerance = 1.8f;
        }

        public struct Candidate
        {
            public Vector2 centre;   // pixel coords, same origin as input
            public float size;      // mean of bbox width and height, px
            public float score;
        }

        public class Result
        {
            public bool found;
            /// <summary>TL, TR, BR, BL in pixel coords (same origin as the input image).</summary>
            public Vector2[] corners = new Vector2[4];
            public float glyphSize;
            public List<Candidate> candidates = new List<Candidate>();
        }

        /// <param name="gray">Greyscale image, row-major, w*h.</param>
        /// <param name="originBottomLeft">True for Unity texture order (row 0 = bottom).</param>
        public static Result Detect(byte[] gray, int w, int h, Settings s, bool originBottomLeft = true,
                                    Func<Vector2, bool> probe = null, List<string> diagnostics = null)
        {
            var result = new Result();
            int n = w * h;

            // 1. Integral image for a fast local mean.
            var integral = new long[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                long row = 0;
                for (int x = 0; x < w; x++)
                {
                    row += gray[y * w + x];
                    integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row;
                }
            }

            int r = Mathf.Max(4, Mathf.RoundToInt(w * s.localWindowFraction * 0.5f));
            var ink = new bool[n];
            for (int y = 0; y < h; y++)
            {
                int y0 = Mathf.Max(0, y - r), y1 = Mathf.Min(h, y + r + 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(w, x + r + 1);
                    long sum = integral[y1 * (w + 1) + x1] - integral[y0 * (w + 1) + x1]
                             - integral[y1 * (w + 1) + x0] + integral[y0 * (w + 1) + x0];
                    float mean = sum / (float)((x1 - x0) * (y1 - y0));
                    ink[y * w + x] = gray[y * w + x] < mean * (1f - s.inkContrast);
                }
            }

            // 2. Dilate so a glyph's separate strokes merge into one blob.
            var joined = Dilate(ink, w, h, s.joinRadius);

            // 3. Connected components (4-connected, iterative flood fill).
            var labels = new int[n];
            var stack = new Stack<int>();
            int minSide = Mathf.Max(3, Mathf.RoundToInt(w * s.minSideFraction));
            int maxSide = Mathf.RoundToInt(w * s.maxSideFraction);
            int label = 0;
            var candidates = new List<Candidate>();

            for (int i = 0; i < n; i++)
            {
                if (!joined[i] || labels[i] != 0) continue;
                label++;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
                bool tooBig = false;
                stack.Push(i);
                labels[i] = label;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int px = p % w, py = p / w;
                    count++;
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                    if (maxX - minX > maxSide * 2 || maxY - minY > maxSide * 2) tooBig = true;
                    if (px > 0) Visit(p - 1);
                    if (px < w - 1) Visit(p + 1);
                    if (py > 0) Visit(p - w);
                    if (py < h - 1) Visit(p + w);
                }

                void Visit(int q)
                {
                    if (joined[q] && labels[q] == 0) { labels[q] = label; stack.Push(q); }
                }

                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                bool log = diagnostics != null && probe != null && probe(new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f));
                string why = null;
                if (tooBig) why = "too big";
                else if (bw < minSide || bh < minSide || bw > maxSide || bh > maxSide) why = $"side {bw}x{bh} outside {minSide}..{maxSide}";
                else if (bw / (float)bh < 0.5f || bw / (float)bh > 2f) why = $"aspect {bw}x{bh}";
                else if (TryScore(gray, ink, w, h, minX, minY, maxX, maxY, s, out var cand, out why))
                    candidates.Add(cand);
                if (log) diagnostics.Add($"blob at ({(minX + maxX) / 2},{(minY + maxY) / 2}) {bw}x{bh}: {why ?? "ACCEPTED"}");
            }

            // Drop small blobs that sit inside a bigger candidate (e.g. the TR glyph's dots).
            candidates.Sort((a, b) => b.size.CompareTo(a.size));
            for (int i = candidates.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if ((candidates[i].centre - candidates[j].centre).magnitude < candidates[j].size * 0.9f)
                    { candidates.RemoveAt(i); break; }

            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            if (candidates.Count > s.maxCandidates) candidates.RemoveRange(s.maxCandidates, candidates.Count - s.maxCandidates);
            result.candidates = candidates;
            if (candidates.Count < 4) return result;

            // 4. Choose the best quad of four candidates.
            float bestScore = float.NegativeInfinity;
            bool any = false;
            int m = candidates.Count;
            var idx = new int[4];
            var pts = new Vector2[4];
            for (int a = 0; a < m; a++)
            for (int b = a + 1; b < m; b++)
            for (int c = b + 1; c < m; c++)
            for (int d = c + 1; d < m; d++)
            {
                idx[0] = a; idx[1] = b; idx[2] = c; idx[3] = d;
                float sizeMin = float.MaxValue, sizeMax = 0f, candScore = 0f;
                for (int k = 0; k < 4; k++)
                {
                    var cd = candidates[idx[k]];
                    pts[k] = cd.centre;
                    sizeMin = Mathf.Min(sizeMin, cd.size);
                    sizeMax = Mathf.Max(sizeMax, cd.size);
                    candScore += cd.score;
                }
                if (sizeMax / sizeMin > 2.2f) continue;

                var ordered = OrderCorners(pts, originBottomLeft);
                if (ordered == null) continue;
                if (!QuadLooksLikeBoard(ordered, sizeMax, s)) continue;

                float area = QuadArea(ordered);
                // The real board quad spans the whole board, so area dominates; size spread is penalised.
                float score = candScore + 3f * Mathf.Log(area + 1f) - 2f * (sizeMax / sizeMin - 1f);
                if (score > bestScore)
                {
                    bestScore = score;
                    any = true;
                    ordered.CopyTo(result.corners, 0);
                    result.glyphSize = (sizeMin + sizeMax) * 0.5f;
                }
            }

            result.found = any;
            return result;
        }

        static bool TryScore(byte[] gray, bool[] ink, int w, int h, int minX, int minY, int maxX, int maxY,
                             Settings s, out Candidate cand, out string why)
        {
            cand = default;
            why = null;
            int bw = maxX - minX + 1, bh = maxY - minY + 1;

            long inkSum = 0; int inkCount = 0, boxCount = 0;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                boxCount++;
                if (ink[y * w + x]) { inkCount++; inkSum += gray[y * w + x]; }
            }
            if (inkCount == 0) { why = "no ink"; return false; }
            float fill = inkCount / (float)boxCount;
            if (fill < s.minInkFill || fill > s.maxInkFill) { why = $"fill {fill:F2}"; return false; }

            int ring = Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(bw, bh) * s.ringFraction));
            int rx0 = minX - ring, ry0 = minY - ring, rx1 = maxX + ring, ry1 = maxY + ring;
            if (rx0 < 0 || ry0 < 0 || rx1 >= w || ry1 >= h) { why = "ring off image"; return false; }

            // Per-side ring stats: 0 left, 1 right, 2 below, 3 above. A glyph sits at a board
            // corner, so its outer sides may see the board edge; its inner sides see clean tape.
            var sideCount = new int[4]; var sideInk = new int[4];
            var sideSum = new long[4]; var sideSq = new long[4];
            for (int y = ry0; y <= ry1; y++)
            for (int x = rx0; x <= rx1; x++)
            {
                if (x >= minX && x <= maxX && y >= minY && y <= maxY) continue;
                int i = y * w + x;
                for (int sd = 0; sd < 4; sd++)
                {
                    bool inSide = sd == 0 ? x < minX : sd == 1 ? x > maxX : sd == 2 ? y < minY : y > maxY;
                    if (!inSide) continue;
                    sideCount[sd]++;
                    if (ink[i]) { sideInk[sd]++; continue; }
                    sideSum[sd] += gray[i];
                    sideSq[sd] += gray[i] * gray[i];
                }
            }
            var frac = new float[4];
            for (int sd = 0; sd < 4; sd++) frac[sd] = sideInk[sd] / (float)Mathf.Max(1, sideCount[sd]);
            // The two cleanest sides must be adjacent (one horizontal, one vertical), i.e. an inner corner.
            int bestX = frac[0] <= frac[1] ? 0 : 1, bestY = frac[2] <= frac[3] ? 2 : 3;
            float ringInkFrac = Mathf.Max(frac[bestX], frac[bestY]);
            if (ringInkFrac > s.maxRingInk) { why = $"ring ink {ringInkFrac:F3} (sides {frac[0]:F2},{frac[1]:F2},{frac[2]:F2},{frac[3]:F2})"; return false; }

            int paperCount = sideCount[bestX] + sideCount[bestY] - sideInk[bestX] - sideInk[bestY];
            float paperMean = (sideSum[bestX] + sideSum[bestY]) / (float)Mathf.Max(1, paperCount);
            float paperVar = (sideSq[bestX] + sideSq[bestY]) / (float)Mathf.Max(1, paperCount) - paperMean * paperMean;
            float inkMean = inkSum / (float)inkCount;
            float delta = paperMean - inkMean;
            if (delta < s.minPaperInkDelta) { why = $"paper-ink delta {delta:F0}"; return false; }

            // Score: strong contrast, clean even paper, square box.
            float evenness = 1f / (1f + Mathf.Sqrt(Mathf.Max(0f, paperVar)) / 20f);
            float squareness = Mathf.Min(bw, bh) / (float)Mathf.Max(bw, bh);
            float score = (delta / 255f) + evenness + 0.5f * squareness - 4f * ringInkFrac
                        + 0.3f * Mathf.Log(Mathf.Max(bw, bh));   // glyphs are the biggest clean blobs

            cand = new Candidate
            {
                centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f),
                size = (bw + bh) * 0.5f,
                score = score,
            };
            return true;
        }

        static bool[] Dilate(bool[] src, int w, int h, int radius)
        {
            if (radius <= 0) return src;
            var tmp = new bool[src.Length];
            var dst = new bool[src.Length];
            for (int y = 0; y < h; y++)
            {
                int run = -1;
                for (int x = 0; x < w; x++)
                {
                    if (src[y * w + x]) run = x;
                    if (run >= 0 && x - run <= radius) tmp[y * w + x] = true;
                }
                run = -1;
                for (int x = w - 1; x >= 0; x--)
                {
                    if (src[y * w + x]) run = x;
                    if (run >= 0 && run - x <= radius) tmp[y * w + x] = true;
                }
            }
            for (int x = 0; x < w; x++)
            {
                int run = -1;
                for (int y = 0; y < h; y++)
                {
                    if (tmp[y * w + x]) run = y;
                    if (run >= 0 && y - run <= radius) dst[y * w + x] = true;
                }
                run = -1;
                for (int y = h - 1; y >= 0; y--)
                {
                    if (tmp[y * w + x]) run = y;
                    if (run >= 0 && run - y <= radius) dst[y * w + x] = true;
                }
            }
            return dst;
        }

        /// <summary>Returns TL, TR, BR, BL, or null if the points are not a convex quad.</summary>
        public static Vector2[] OrderCorners(Vector2[] p, bool originBottomLeft)
        {
            var c = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
            var sorted = new List<Vector2>(p);
            // Sort by angle around the centroid.
            sorted.Sort((a, b) => Mathf.Atan2(a.y - c.y, a.x - c.x).CompareTo(Mathf.Atan2(b.y - c.y, b.x - c.x)));

            // Convexity: all cross products share a sign.
            float sign = 0f;
            for (int i = 0; i < 4; i++)
            {
                var e0 = sorted[(i + 1) % 4] - sorted[i];
                var e1 = sorted[(i + 2) % 4] - sorted[(i + 1) % 4];
                float cross = e0.x * e1.y - e0.y * e1.x;
                if (Mathf.Abs(cross) < 1e-3f) return null;
                if (sign == 0f) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != sign) return null;
            }

            // "Up" in the image is +y for bottom-left origin, -y for top-left.
            float up = originBottomLeft ? 1f : -1f;
            Vector2 tl = default, tr = default, br = default, bl = default;
            float bestTL = float.NegativeInfinity, bestTR = float.NegativeInfinity,
                  bestBR = float.NegativeInfinity, bestBL = float.NegativeInfinity;
            foreach (var q in sorted)
            {
                float u = (q.y - c.y) * up, x = q.x - c.x;
                if (-x + u > bestTL) { bestTL = -x + u; tl = q; }
                if (x + u > bestTR) { bestTR = x + u; tr = q; }
                if (x - u > bestBR) { bestBR = x - u; br = q; }
                if (-x - u > bestBL) { bestBL = -x - u; bl = q; }
            }
            if (tl == tr || tl == br || tl == bl || tr == br || tr == bl || br == bl) return null;
            return new[] { tl, tr, br, bl };
        }

        static bool QuadLooksLikeBoard(Vector2[] q, float glyphSize, Settings s)
        {
            float top = (q[1] - q[0]).magnitude, bottom = (q[2] - q[3]).magnitude;
            float left = (q[3] - q[0]).magnitude, right = (q[2] - q[1]).magnitude;
            if (Ratio(top, bottom) > 1.7f || Ratio(left, right) > 1.7f) return false;

            float aspect = (top + bottom) / (left + right);
            if (Ratio(aspect, s.expectedAspect) > s.aspectTolerance) return false;

            // Glyphs are small relative to the board (about 1/8 of its width or less).
            if (Mathf.Min(top, bottom) < glyphSize * 3f) return false;
            return true;
        }

        static float Ratio(float a, float b) => a > b ? a / Mathf.Max(b, 1e-6f) : b / Mathf.Max(a, 1e-6f);

        static float QuadArea(Vector2[] q)
        {
            float a = 0f;
            for (int i = 0; i < 4; i++)
            {
                var p0 = q[i]; var p1 = q[(i + 1) % 4];
                a += p0.x * p1.y - p1.x * p0.y;
            }
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>Converts Color32 pixels to greyscale (Rec. 601 luma), optionally downsampling by an integer factor.</summary>
        public static byte[] ToGray(Color32[] px, int w, int h, int step, out int outW, out int outH)
        {
            step = Mathf.Max(1, step);
            outW = w / step; outH = h / step;
            var g = new byte[outW * outH];
            for (int y = 0; y < outH; y++)
            for (int x = 0; x < outW; x++)
            {
                var c = px[(y * step) * w + x * step];
                g[y * outW + x] = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8);
            }
            return g;
        }
    }
}
