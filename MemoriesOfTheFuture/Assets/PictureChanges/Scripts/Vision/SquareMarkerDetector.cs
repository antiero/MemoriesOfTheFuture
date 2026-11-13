using System;
using System.Collections.Generic;
using UnityEngine;

namespace PictureChanges.Vision
{
    /// <summary>
    /// Finds the four corner markers on the board: a thick, hand-drawn dark square outline
    /// around each glyph. Rings like this are rare in a room, survive blur and distance
    /// far better than thin glyph strokes, and give four precise corner points each.
    ///
    /// Steps: adaptive threshold -> connected components of bright pixels -> keep enclosed,
    /// square interiors with a thick dark band round them -> inner-corner quad from each
    /// interior's extreme points -> pick the four markers that best form the board rectangle.
    /// Working from the interior means a ring touching other dark things still works.
    ///
    /// Pure C#, safe on a worker thread.
    /// </summary>
    public static class SquareMarkerDetector
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("Ring pixels must be this much darker than the local mean (0.25 = 25% darker).")]
            public float darkContrast = 0.25f;
            [Tooltip("Local-mean window as a fraction of image width. Should be wider than a marker.")]
            public float localWindowFraction = 0.05f;
            [Tooltip("Marker interior side limits, as a fraction of image width.")]
            public float minSideFraction = 0.006f, maxSideFraction = 0.15f;
            [Tooltip("Ring line width / interior side. The scanned board: ~2 mm line on a ~20 mm interior = 0.09.")]
            public float lineOverInner = 0.09f;
            [Tooltip("Fraction of the ring band (just outside the interior) that must be dark.")]
            public float minRingDark = 0.7f;
            [Tooltip("Interior pixels / interior quad area. Glyph ink makes this < 1.")]
            public float minInteriorSolidity = 0.5f;
            [Tooltip("Interior paper must be this many grey levels brighter than the ring.")]
            public float minInteriorDelta = 45f;
            [Tooltip("The interior must be this close to a quadrilateral (quad area / bbox area).")]
            public float minQuadFill = 0.55f;
            [Tooltip("Accept three markers in an L when the fourth is not found (pose from 12 corners).")]
            public bool allowThreeMarkers = true;
            [Tooltip("Keep this many best candidates when searching for the board.")]
            public int maxCandidates = 20;
            [Tooltip("Expected width / height of the marker-centre rectangle on the board.")]
            public float expectedAspect = 0.727f;
            [Tooltip("How far the image-space aspect may stray from expected (perspective), as a ratio.")]
            public float aspectTolerance = 1.8f;
            [Tooltip("Marker interior side relative to marker spacing (width). Used to reject quads of the wrong scale.")]
            public float expectedSideOverSpacing = 0.088f;
        }

        public struct Marker
        {
            public Vector2 centre;
            public Vector2[] corners;   // interior (inner) corners TL, TR, BR, BL in image space
            public float size;          // sqrt of quad area, px
            public float score;
        }

        /// <summary>Detection result. corners/candidates match GlyphDetector.Result for the debug view.</summary>
        public class Result : GlyphDetector.Result
        {
            /// <summary>Inner corner points of the four chosen markers, in board order TL, TR, BR, BL; each TL, TR, BR, BL.</summary>
            public Vector2[][] markerCorners;
            /// <summary>Board corner (0..3) whose marker was not seen and was predicted, or -1.</summary>
            public int missingMarker = -1;
        }

        public static Result Detect(byte[] gray, int w, int h, Settings s, bool originBottomLeft = true,
                                    Func<Vector2, bool> probe = null, List<string> diagnostics = null)
        {
            var result = new Result();
            int n = w * h;

            // 1. Adaptive threshold via an integral image.
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
            int r = Mathf.Max(6, Mathf.RoundToInt(w * s.localWindowFraction * 0.5f));
            var dark = new bool[n];
            for (int y = 0; y < h; y++)
            {
                int y0 = Mathf.Max(0, y - r), y1 = Mathf.Min(h, y + r + 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(w, x + r + 1);
                    long sum = integral[y1 * (w + 1) + x1] - integral[y0 * (w + 1) + x1]
                             - integral[y1 * (w + 1) + x0] + integral[y0 * (w + 1) + x0];
                    float mean = sum / (float)((x1 - x0) * (y1 - y0));
                    dark[y * w + x] = gray[y * w + x] < mean * (1f - s.darkContrast);
                }
            }

            // 2. Connected components of NOT-dark pixels (4-connected, the dual of an 8-connected
            //    ring). A marker's bright interior is enclosed by its ring, so it stays a separate,
            //    clean quad even when the ring outside touches other dark things.
            var labels = new int[n];
            var stack = new Stack<int>();
            int minSide = Mathf.Max(5, Mathf.RoundToInt(w * s.minSideFraction));
            int maxSide = Mathf.RoundToInt(w * s.maxSideFraction);
            var markers = new List<Marker>();
            int label = 0;

            for (int i = 0; i < n; i++)
            {
                if (dark[i] || labels[i] != 0) continue;
                label++;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
                long sumGray = 0;
                // Extreme points along the diagonals give the corners of a roughly upright square.
                int bestA = int.MaxValue, bestB = int.MinValue, bestC = int.MaxValue, bestD = int.MinValue;
                int pA = 0, pB = 0, pC = 0, pD = 0;
                bool tooBig = false, touchesBorder = false;
                stack.Push(i);
                labels[i] = label;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int px = p % w, py = p / w;
                    count++;
                    sumGray += gray[p];
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                    if (px == 0 || py == 0 || px == w - 1 || py == h - 1) touchesBorder = true;
                    int sum = px + py, diff = px - py;
                    if (sum < bestA) { bestA = sum; pA = p; }
                    if (sum > bestB) { bestB = sum; pB = p; }
                    if (diff < bestC) { bestC = diff; pC = p; }
                    if (diff > bestD) { bestD = diff; pD = p; }
                    if (maxX - minX > maxSide || maxY - minY > maxSide) tooBig = true;
                    if (px > 0) Visit(p - 1);
                    if (px < w - 1) Visit(p + 1);
                    if (py > 0) Visit(p - w);
                    if (py < h - 1) Visit(p + w);
                }

                void Visit(int q)
                {
                    if (!dark[q] && labels[q] == 0) { labels[q] = label; stack.Push(q); }
                }

                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (tooBig || touchesBorder || bw < 3 || bh < 3) continue;   // background and specks: not worth logging
                bool log = diagnostics != null && probe != null && probe(new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f));
                string why;
                if (bw < minSide || bh < minSide) why = $"interior {bw}x{bh} under {minSide}";
                else if (bw / (float)bh < 0.4f || bw / (float)bh > 2.5f) why = $"aspect {bw}x{bh}";
                else
                {
                    var quad = new[] { P(pA, w), P(pC, w), P(pB, w), P(pD, w) };   // (min x+y), (min x-y), (max x+y), (max x-y)
                    why = TryMarker(gray, dark, w, h, bw, bh, count, sumGray / (float)count, quad, s, originBottomLeft, out var m);
                    if (why == null) markers.Add(m);
                }
                if (log) diagnostics.Add($"interior at ({(minX + maxX) / 2},{(minY + maxY) / 2}) {bw}x{bh}: {why ?? "ACCEPTED"}");
            }

            // 3. Drop markers whose centre lies inside a bigger marker (e.g. a closed glyph inside its square).
            markers.Sort((a, b) => b.size.CompareTo(a.size));
            for (int i = markers.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if ((markers[i].centre - markers[j].centre).magnitude < markers[j].size * 0.5f)
                    { markers.RemoveAt(i); break; }

            markers.Sort((a, b) => b.score.CompareTo(a.score));
            if (markers.Count > s.maxCandidates) markers.RemoveRange(s.maxCandidates, markers.Count - s.maxCandidates);
            foreach (var m in markers)
                result.candidates.Add(new GlyphDetector.Candidate { centre = m.centre, size = m.size, score = m.score });
            if (markers.Count < 3) return result;

            // 4. Best board quad from four markers.
            float bestScore = float.NegativeInfinity;
            int cnt = markers.Count;
            var pts = new Vector2[4];
            var pick = new Marker[4];
            for (int a = 0; a < cnt; a++)
            for (int b = a + 1; b < cnt; b++)
            for (int c = b + 1; c < cnt; c++)
            for (int d = c + 1; d < cnt; d++)
            {
                pick[0] = markers[a]; pick[1] = markers[b]; pick[2] = markers[c]; pick[3] = markers[d];
                float sizeMin = float.MaxValue, sizeMax = 0f, candScore = 0f;
                for (int k = 0; k < 4; k++)
                {
                    pts[k] = pick[k].centre;
                    sizeMin = Mathf.Min(sizeMin, pick[k].size);
                    sizeMax = Mathf.Max(sizeMax, pick[k].size);
                    candScore += pick[k].score;
                }
                if (sizeMax / sizeMin > 1.8f) continue;

                var ordered = GlyphDetector.OrderCorners(pts, originBottomLeft);
                if (ordered == null) continue;
                if (!BoardShaped(ordered, (sizeMin + sizeMax) * 0.5f, s)) continue;

                float score = candScore - 3f * (sizeMax / sizeMin - 1f);
                if (score <= bestScore) continue;
                bestScore = score;
                ordered.CopyTo(result.corners, 0);
                result.glyphSize = (sizeMin + sizeMax) * 0.5f;
                result.markerCorners = new Vector2[4][];
                for (int k = 0; k < 4; k++)
                    foreach (var m in pick)
                        if (m.centre == ordered[k]) { result.markerCorners[k] = m.corners; break; }
                result.found = true;
            }
            if (result.found || !s.allowThreeMarkers) return result;

            // 5. Fallback: three markers forming an L (one hidden by ink, a hand or glare).
            //    Twelve corner points still fix the pose; the missing marker is predicted for display.
            bestScore = float.NegativeInfinity;
            for (int a = 0; a < cnt; a++)
            for (int b = a + 1; b < cnt; b++)
            for (int c = b + 1; c < cnt; c++)
            {
                var tri = new[] { markers[a], markers[b], markers[c] };
                float sizeMin = Mathf.Min(tri[0].size, Mathf.Min(tri[1].size, tri[2].size));
                float sizeMax = Mathf.Max(tri[0].size, Mathf.Max(tri[1].size, tri[2].size));
                if (sizeMax / sizeMin > 1.6f) continue;
                if (!LabelThree(tri, originBottomLeft, out var slots)) continue;

                var quad4 = new Vector2[4];
                int missing = -1;
                for (int k = 0; k < 4; k++)
                {
                    if (slots[k] < 0) { missing = k; continue; }
                    quad4[k] = tri[slots[k]].centre;
                }
                // Parallelogram prediction: missing = sum of its two neighbours - the opposite corner.
                quad4[missing] = quad4[(missing + 1) % 4] + quad4[(missing + 3) % 4] - quad4[(missing + 2) % 4];
                if (!BoardShaped(quad4, (sizeMin + sizeMax) * 0.5f, s)) continue;

                float score = tri[0].score + tri[1].score + tri[2].score - 3f * (sizeMax / sizeMin - 1f);
                if (score <= bestScore) continue;
                bestScore = score;
                quad4.CopyTo(result.corners, 0);
                result.glyphSize = (sizeMin + sizeMax) * 0.5f;
                result.markerCorners = new Vector2[4][];
                for (int k = 0; k < 4; k++) result.markerCorners[k] = slots[k] >= 0 ? tri[slots[k]].corners : null;
                result.found = true;
                result.missingMarker = missing;
            }
            return result;
        }

        /// <summary>
        /// Assigns three marker centres to board corners (TL=0, TR=1, BR=2, BL=3). The shared corner
        /// of the L is the one with the angle closest to 90 degrees; its horizontal and vertical
        /// neighbours (in image space, board assumed roughly upright) decide which corner it is.
        /// slots[k] = index into tri, or -1 for the missing corner.
        /// </summary>
        static bool LabelThree(Marker[] tri, bool originBottomLeft, out int[] slots)
        {
            slots = null;
            int v = -1; float best = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var a = tri[(i + 1) % 3].centre - tri[i].centre;
                var b = tri[(i + 2) % 3].centre - tri[i].centre;
                float off = Mathf.Abs(Vector2.Angle(a, b) - 90f);
                if (off < best) { best = off; v = i; }
            }
            if (best > 25f) return false;

            float up = originBottomLeft ? 1f : -1f;
            int hi = -1, vi = -1;
            for (int j = 1; j <= 2; j++)
            {
                int i = (v + j) % 3;
                var d = tri[i].centre - tri[v].centre;
                if (Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.5f) hi = i;
                else if (Mathf.Abs(d.y) > Mathf.Abs(d.x) * 1.5f) vi = i;
            }
            if (hi < 0 || vi < 0) return false;

            bool neighbourRight = tri[hi].centre.x > tri[v].centre.x;
            bool neighbourAbove = (tri[vi].centre.y - tri[v].centre.y) * up > 0f;
            // Corner of the shared vertex, and of its two neighbours.
            int cv = neighbourRight ? (neighbourAbove ? 3 : 0) : (neighbourAbove ? 2 : 1);
            int ch = neighbourRight ? (neighbourAbove ? 2 : 1) : (neighbourAbove ? 3 : 0);
            int cvv = neighbourAbove ? (neighbourRight ? 0 : 1) : (neighbourRight ? 3 : 2);
            slots = new[] { -1, -1, -1, -1 };
            slots[cv] = v; slots[ch] = hi; slots[cvv] = vi;
            return true;
        }


        static Vector2 P(int index, int w) => new Vector2(index % w, index / w);

        /// <summary>
        /// Checks a bright enclosed region: is it a square interior with a thick dark ring round it?
        /// Returns null if so (marker set), otherwise the rejection reason.
        /// </summary>
        static string TryMarker(byte[] gray, bool[] dark, int w, int h, int bw, int bh, int count, float interiorMean,
                                Vector2[] rawQuad, Settings s, bool originBottomLeft, out Marker marker)
        {
            marker = default;
            var quad = GlyphDetector.OrderCorners(rawQuad, originBottomLeft);
            if (quad == null) return "corners not a convex quad";
            float quadArea = Area(quad);
            float quadFill = quadArea / (bw * bh);
            if (quadFill < s.minQuadFill) return $"quad fill {quadFill:F2}";
            float top = (quad[1] - quad[0]).magnitude, bottom = (quad[2] - quad[3]).magnitude;
            float left = (quad[3] - quad[0]).magnitude, right = (quad[2] - quad[1]).magnitude;
            if (Ratio(top, bottom) > 2.2f || Ratio(left, right) > 2.2f) return "sides uneven (rough)";

            // The bright region should fill its quad, apart from the glyph's ink.
            float solidity = count / Mathf.Max(1f, quadArea);
            if (solidity < s.minInteriorSolidity) return $"solidity {solidity:F2}";

            var centre = Intersect(quad[0], quad[2], quad[1], quad[3]);

            // Rough ring check on the extreme-point quad (ink notches can distort it, so be lenient),
            // mainly to estimate the ring brightness for the edge threshold.
            float side = Mathf.Sqrt(quadArea);
            float band = Mathf.Max(1.5f, side * s.lineOverInner);
            if (!RingStats(quad, centre, band, gray, dark, w, h, out float ringDark, out float ringMean)) return "ring off image";
            if (ringDark < s.minRingDark * 0.5f) return $"ring dark {ringDark:F2} (rough)";
            if (interiorMean - ringMean < s.minInteriorDelta * 0.6f) return $"interior-ring delta {interiorMean - ringMean:F0} (rough)";

            // Refine: fit a line to each edge (robust to glyph ink touching the ring) and intersect.
            var refined = RefineCorners(quad, centre, side, band, gray, w, h, (interiorMean + ringMean) * 0.5f, out string fitWhy);
            if (refined == null) return $"edge fit failed: {fitWhy}";
            quad = refined;
            centre = Intersect(quad[0], quad[2], quad[1], quad[3]);
            top = (quad[1] - quad[0]).magnitude; bottom = (quad[2] - quad[3]).magnitude;
            left = (quad[3] - quad[0]).magnitude; right = (quad[2] - quad[1]).magnitude;
            if (Ratio(top, bottom) > 1.6f || Ratio(left, right) > 1.6f) return "sides uneven";
            quadArea = Area(quad);
            side = Mathf.Sqrt(quadArea);

            // Precise ring check on the refined quad.
            band = Mathf.Max(1.5f, side * s.lineOverInner);
            if (!RingStats(quad, centre, band, gray, dark, w, h, out ringDark, out ringMean)) return "ring off image";
            if (ringDark < s.minRingDark) return $"ring dark {ringDark:F2}";
            float delta = interiorMean - ringMean;
            if (delta < s.minInteriorDelta) return $"interior-ring delta {delta:F0}";

            float squareness = Mathf.Min(top + bottom, left + right) / Mathf.Max(top + bottom, left + right);
            marker = new Marker
            {
                centre = centre,
                corners = quad,
                size = side,
                score = delta / 255f + squareness + ringDark + quadFill,
            };
            return null;
        }

        /// <summary>Fraction of dark pixels, and mean grey, in a band just outside each edge.</summary>
        static bool RingStats(Vector2[] quad, Vector2 centre, float band, byte[] gray, bool[] dark, int w, int h,
                              out float darkFraction, out float mean)
        {
            darkFraction = 0f; mean = 0f;
            int samples = 0, darkSamples = 0; long sum = 0;
            for (int e = 0; e < 4; e++)
            {
                var a = quad[e]; var b = quad[(e + 1) % 4];
                var outward = ((a + b) * 0.5f - centre).normalized;
                int steps = Mathf.Max(6, Mathf.RoundToInt((b - a).magnitude));
                for (int i = 1; i < steps; i++)   // skip the very corners
                {
                    var p = Vector2.Lerp(a, b, i / (float)steps);
                    for (float d = 1f; d <= band; d += 1f)
                    {
                        var q = p + outward * d;
                        int x = Mathf.RoundToInt(q.x), y = Mathf.RoundToInt(q.y);
                        if (x < 0 || y < 0 || x >= w || y >= h) return false;
                        int gi = y * w + x;
                        samples++;
                        sum += gray[gi];
                        if (dark[gi]) darkSamples++;
                    }
                }
            }
            darkFraction = darkSamples / (float)Mathf.Max(1, samples);
            mean = sum / (float)Mathf.Max(1, samples);
            return true;
        }

        /// <summary>
        /// Sub-pixel edge points along each side (bright interior -> dark ring crossing), a robust
        /// line fit per side, and corner = intersection of neighbouring lines. Null if it fails.
        /// </summary>
        static Vector2[] RefineCorners(Vector2[] quad, Vector2 centre, float side, float band,
                                       byte[] gray, int w, int h, float threshold, out string why)
        {
            why = null;
            var linePoint = new Vector2[4];
            var lineDir = new Vector2[4];
            var pts = new List<Vector2>();
            for (int e = 0; e < 4; e++)
            {
                var a = quad[e]; var b = quad[(e + 1) % 4];
                var dir = (b - a).normalized;
                var normal = new Vector2(-dir.y, dir.x);
                if (Vector2.Dot(normal, (a + b) * 0.5f - centre) < 0f) normal = -normal;   // outward

                pts.Clear();
                int steps = Mathf.Max(8, Mathf.RoundToInt((b - a).magnitude));
                float start = -0.3f * side, end = band + 2f;
                for (int i = 0; i <= steps; i++)
                {
                    float t = Mathf.Lerp(0.15f, 0.85f, i / (float)steps);   // keep away from the corners
                    var p = Vector2.Lerp(a, b, t);
                    // Keep the LAST bright->dark crossing: the ring's inner edge. Earlier crossings
                    // are glyph ink inside the square.
                    float prevD = start, prevG = Sample(gray, w, h, p + normal * start);
                    float crossing = float.NaN;
                    for (float d = start + 0.5f; d <= end; d += 0.5f)
                    {
                        float g = Sample(gray, w, h, p + normal * d);
                        if (g < 0f) break;
                        if (prevG >= 0f && g < threshold && prevG >= threshold)
                            crossing = Mathf.Lerp(prevD, d, (prevG - threshold) / Mathf.Max(1e-3f, prevG - g));
                        prevD = d; prevG = g;
                    }
                    if (!float.IsNaN(crossing)) pts.Add(p + normal * crossing);
                }
                if (pts.Count < 5) { why = $"edge {e}: only {pts.Count} edge points"; return null; }
                if (!FitLine(pts, out linePoint[e], out lineDir[e])) { why = $"edge {e}: line fit"; return null; }
            }

            var result = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                // Corner k joins edge (k-1) and edge k.
                int prev = (k + 3) % 4;
                if (!IntersectLines(linePoint[prev], lineDir[prev], linePoint[k], lineDir[k], out result[k])) { why = $"corner {k}: parallel edges"; return null; }
                if ((result[k] - quad[k]).magnitude > side * 0.35f) { why = $"corner {k} moved {(result[k] - quad[k]).magnitude:F1}px"; return null; }
            }
            return result;
        }

        /// <summary>Total-least-squares line with two rounds of outlier trimming.</summary>
        static bool FitLine(List<Vector2> pts, out Vector2 point, out Vector2 dir)
        {
            point = default; dir = default;
            var use = new List<Vector2>(pts);
            for (int iter = 0; iter < 3; iter++)
            {
                if (use.Count < 4) return false;
                var c = Vector2.zero;
                foreach (var p in use) c += p;
                c /= use.Count;
                float sxx = 0, sxy = 0, syy = 0;
                foreach (var p in use) { var d = p - c; sxx += d.x * d.x; sxy += d.x * d.y; syy += d.y * d.y; }
                float angle = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
                point = c;
                dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (iter == 2) break;

                var normal = new Vector2(-dir.y, dir.x);
                var res = new List<float>();
                foreach (var p in use) res.Add(Mathf.Abs(Vector2.Dot(p - c, normal)));
                var sorted = new List<float>(res); sorted.Sort();
                float limit = Mathf.Max(0.75f, 2.5f * sorted[sorted.Count / 2]);
                var kept = new List<Vector2>();
                for (int i = 0; i < use.Count; i++) if (res[i] <= limit) kept.Add(use[i]);
                use = kept;
            }
            return true;
        }

        static bool IntersectLines(Vector2 p0, Vector2 d0, Vector2 p1, Vector2 d1, out Vector2 x)
        {
            float den = d0.x * d1.y - d0.y * d1.x;
            x = default;
            if (Mathf.Abs(den) < 1e-4f) return false;
            float t = ((p1.x - p0.x) * d1.y - (p1.y - p0.y) * d1.x) / den;
            x = p0 + d0 * t;
            return true;
        }

        /// <summary>Bilinear grey sample at a pixel-centre coordinate; -1 off image.</summary>
        static float Sample(byte[] gray, int w, int h, Vector2 p)
        {
            if (p.x < 0f || p.y < 0f || p.x > w - 1.001f || p.y > h - 1.001f) return -1f;
            int x = (int)p.x, y = (int)p.y;
            float fx = p.x - x, fy = p.y - y;
            int i = y * w + x;
            float top = Mathf.Lerp(gray[i], gray[i + 1], fx);
            float bot = Mathf.Lerp(gray[i + w], gray[i + w + 1], fx);
            return Mathf.Lerp(top, bot, fy);
        }

        static bool BoardShaped(Vector2[] q, float markerSize, Settings s)
        {
            float top = (q[1] - q[0]).magnitude, bottom = (q[2] - q[3]).magnitude;
            float left = (q[3] - q[0]).magnitude, right = (q[2] - q[1]).magnitude;
            if (Ratio(top, bottom) > 1.7f || Ratio(left, right) > 1.7f) return false;
            float aspect = (top + bottom) / (left + right);
            if (Ratio(aspect, s.expectedAspect) > s.aspectTolerance) return false;
            // Marker size should match the spacing (both scale with distance).
            float sideOverSpacing = markerSize / ((top + bottom) * 0.5f);
            return Ratio(sideOverSpacing, s.expectedSideOverSpacing) < 2.0f;
        }

        static Vector2 Intersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            var da = a1 - a0; var db = b1 - b0;
            float den = da.x * db.y - da.y * db.x;
            if (Mathf.Abs(den) < 1e-6f) return (a0 + a1 + b0 + b1) * 0.25f;
            float t = ((b0.x - a0.x) * db.y - (b0.y - a0.y) * db.x) / den;
            return a0 + da * t;
        }

        static float Area(Vector2[] q)
        {
            float a = 0f;
            for (int i = 0; i < 4; i++) { var p0 = q[i]; var p1 = q[(i + 1) % 4]; a += p0.x * p1.y - p1.x * p0.y; }
            return Mathf.Abs(a) * 0.5f;
        }

        static float Ratio(float a, float b) => a > b ? a / Mathf.Max(b, 1e-6f) : b / Mathf.Max(a, 1e-6f);
    }
}
