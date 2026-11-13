using System;
using UnityEngine;

namespace PictureChanges.Vision
{
    /// <summary>
    /// Physical layout of the board, in metres, in the board plane (X right, Y up, origin at the
    /// centre of the four marker centres).
    ///
    /// Measured geometry (default): the exact inner corners of each hand-drawn marker and the four
    /// corners of the painted area, taken from the 13 Nov 2026 flatbed scan. The scan's scale was
    /// fixed from the painting's ruler-measured edges (top 199, bottom 204, left 287, right 285 mm);
    /// the scan matches those to within 2.4 mm. Nothing is assumed square or rectangular.
    ///
    /// If the markers are redrawn, rescan and regenerate these numbers
    /// (DebugOutput/scan/board_geometry_m.json from the scan analysis).
    /// </summary>
    [Serializable]
    public class BoardLayout
    {
        [Tooltip("Use the measured corner positions below. Off = idealised rectangle/squares from the legacy fields.")]
        public bool useMeasuredGeometry = true;

        [Tooltip("Inner corners of the markers (metres): marker TL, TR, BR, BL, each with corners TL, TR, BR, BL.")]
        public Vector2[] markerInnerCorners =
        {
            new Vector2(-0.12096f, 0.16592f), new Vector2(-0.09882f, 0.16588f), new Vector2(-0.09785f, 0.14800f), new Vector2(-0.12121f, 0.14768f),
            new Vector2(0.10628f, 0.16561f), new Vector2(0.12355f, 0.16481f), new Vector2(0.12324f, 0.14519f), new Vector2(0.10536f, 0.14583f),
            new Vector2(0.10231f, -0.14391f), new Vector2(0.12219f, -0.14479f), new Vector2(0.12218f, -0.16652f), new Vector2(0.10207f, -0.16623f),
            new Vector2(-0.12600f, -0.14755f), new Vector2(-0.10753f, -0.14775f), new Vector2(-0.10852f, -0.16618f), new Vector2(-0.12629f, -0.16601f),
        };

        [Tooltip("Corners of the painted area the virtual painting covers (metres): TL, TR, BR, BL.")]
        public Vector2[] paintingCorners =
        {
            new Vector2(-0.09894f, 0.14281f), new Vector2(0.09765f, 0.14200f),
            new Vector2(0.10089f, -0.14360f), new Vector2(-0.10442f, -0.14430f),
        };

        [Tooltip("Shrink the virtual painting by this much on each side (metres) to hide registration error.")]
        public float inset = 0.0f;

        [Header("Legacy idealised geometry (used only when Use Measured Geometry is off)")]
        [Tooltip("Distance between marker centres: X = TL to TR, Y = TL to BL (metres).")]
        public Vector2 glyphSpacing = new Vector2(0.227f, 0.312f);
        [Tooltip("Outer side length of each marker square (metres).")]
        public float markerSize = 0.024f;
        [Tooltip("Line width of the marker squares (metres).")]
        public float markerLine = 0.002f;
        [Tooltip("Painted area (metres).")]
        public Vector2 paintingSize = new Vector2(0.201f, 0.286f);
        [Tooltip("Painting centre relative to the marker-centre rectangle centre (metres, X right, Y up).")]
        public Vector2 paintingOffset = Vector2.zero;

        bool Measured => useMeasuredGeometry && markerInnerCorners != null && markerInnerCorners.Length == 16
                         && paintingCorners != null && paintingCorners.Length == 4;

        /// <summary>
        /// Inner corners of the four markers: marker TL, TR, BR, BL, each with corners TL, TR, BR, BL.
        /// 16 points, matching SquareMarkerDetector.Result.markerCorners.
        /// </summary>
        public Vector2[] MarkerCorners
        {
            get
            {
                if (Measured) return (Vector2[])markerInnerCorners.Clone();
                var centres = IdealCentres();
                float h = Mathf.Max(0.001f, markerSize - 2f * markerLine) * 0.5f;
                var pts = new Vector2[16];
                for (int m = 0; m < 4; m++)
                {
                    pts[m * 4 + 0] = centres[m] + new Vector2(-h, h);
                    pts[m * 4 + 1] = centres[m] + new Vector2(h, h);
                    pts[m * 4 + 2] = centres[m] + new Vector2(h, -h);
                    pts[m * 4 + 3] = centres[m] + new Vector2(-h, -h);
                }
                return pts;
            }
        }

        /// <summary>Centres of the four markers, TL, TR, BR, BL.</summary>
        public Vector2[] MarkerCentres
        {
            get
            {
                if (!Measured) return IdealCentres();
                var c = new Vector2[4];
                for (int m = 0; m < 4; m++)
                    c[m] = (markerInnerCorners[m * 4] + markerInnerCorners[m * 4 + 1] +
                            markerInnerCorners[m * 4 + 2] + markerInnerCorners[m * 4 + 3]) * 0.25f;
                return c;
            }
        }

        /// <summary>Painting corners TL, TR, BR, BL, shrunk towards the centre by the inset.</summary>
        public Vector2[] PaintingCorners
        {
            get
            {
                Vector2[] p;
                if (Measured) p = (Vector2[])paintingCorners.Clone();
                else
                {
                    float x = paintingSize.x * 0.5f, y = paintingSize.y * 0.5f;
                    p = new[] { paintingOffset + new Vector2(-x, y), paintingOffset + new Vector2(x, y),
                                paintingOffset + new Vector2(x, -y), paintingOffset + new Vector2(-x, -y) };
                }
                if (inset > 0f)
                {
                    var c = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
                    for (int i = 0; i < 4; i++)
                    {
                        var d = p[i] - c;
                        // Move each corner in by `inset` along both board axes.
                        p[i] -= new Vector2(Mathf.Sign(d.x), Mathf.Sign(d.y)) * inset;
                    }
                }
                return p;
            }
        }

        Vector2[] IdealCentres()
        {
            float x = glyphSpacing.x * 0.5f, y = glyphSpacing.y * 0.5f;
            return new[] { new Vector2(-x, y), new Vector2(x, y), new Vector2(x, -y), new Vector2(-x, -y) };
        }

        public static BoardLayout Default() => new BoardLayout();
    }
}
