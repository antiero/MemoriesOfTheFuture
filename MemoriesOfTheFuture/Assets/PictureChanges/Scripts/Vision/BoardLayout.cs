using System;
using UnityEngine;

namespace PictureChanges.Vision
{
    /// <summary>
    /// Physical layout of the board, in metres, in the board plane (X right, Y up,
    /// origin at the centre of the four glyph centres).
    ///
    /// The defaults are ESTIMATED from the reference photo, assuming the painting is A4.
    /// Measure the real board with a ruler and enter the numbers in the inspector.
    /// </summary>
    [Serializable]
    public class BoardLayout
    {
        [Tooltip("Distance between glyph centres: X = TL to TR, Y = TL to BL (metres). MEASURE THIS.")]
        public Vector2 glyphSpacing = new Vector2(0.234f, 0.310f);

        [Tooltip("Painted area the virtual canvas must cover (metres). A4 = 0.210 x 0.297.")]
        public Vector2 paintingSize = new Vector2(0.210f, 0.297f);

        [Tooltip("Painting centre relative to the glyph-rectangle centre (metres, X right, Y up).")]
        public Vector2 paintingOffset = Vector2.zero;

        [Tooltip("Shrink the virtual canvas by this much on each side (metres) to hide registration error.")]
        public float inset = 0.0f;

        /// <summary>TL, TR, BR, BL, matching GlyphDetector's corner order.</summary>
        public Vector2[] GlyphCentres
        {
            get
            {
                float x = glyphSpacing.x * 0.5f, y = glyphSpacing.y * 0.5f;
                return new[] { new Vector2(-x, y), new Vector2(x, y), new Vector2(x, -y), new Vector2(-x, -y) };
            }
        }

        public float Aspect => glyphSpacing.x / glyphSpacing.y;

        public static BoardLayout Default() => new BoardLayout();
    }
}
