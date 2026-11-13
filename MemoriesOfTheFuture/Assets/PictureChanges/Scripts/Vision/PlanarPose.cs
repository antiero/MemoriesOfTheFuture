using UnityEngine;

namespace PictureChanges.Vision
{
    /// <summary>
    /// Pose of a flat, known rectangle from four image points (planar PnP via homography).
    ///
    /// Inputs are normalised camera coordinates: for a ray direction d in camera space
    /// (x right, y up, z forward), u = d.x / d.z and v = d.y / d.z. Using rays rather than
    /// raw pixels lets Meta's ViewportPointToRay handle intrinsics, lens offset and flips.
    /// </summary>
    public static class PlanarPose
    {
        public struct CameraSpacePose
        {
            public Vector3 position;   // board origin in camera space, metres
            public Vector3 right;      // board +X in camera space (unit)
            public Vector3 up;         // board +Y in camera space (unit)
            public Vector3 intoWall;   // unit normal pointing away from the camera
        }

        /// <param name="board">Board points (metres) in the board plane, X right, Y up. Four or more.</param>
        /// <param name="image">Matching normalised image points (u, v).</param>
        public static bool Solve(Vector2[] board, Vector2[] image, out CameraSpacePose pose)
        {
            pose = default;
            if (!Homography(board, image, out var H)) return false;

            var h1 = new Vector3((float)H[0], (float)H[3], (float)H[6]);
            var h2 = new Vector3((float)H[1], (float)H[4], (float)H[7]);
            var h3 = new Vector3((float)H[2], (float)H[5], 1f);

            float lambda = 2f / (h1.magnitude + h2.magnitude);
            var t = h3 * lambda;
            if (t.z < 0f) { lambda = -lambda; t = -t; }   // board must be in front of the camera

            var r1 = h1 * lambda;
            var r2 = h2 * lambda;

            // Symmetric orthonormalisation of r1, r2 (spreads the error over both axes).
            var a = r1.normalized; var b = r2.normalized;
            var bis = (a + b).normalized;
            var perp = Vector3.Cross(Vector3.Cross(a, b), bis).normalized;
            var right = (bis + perp).normalized;   // 45 degrees either side of the bisector
            var up = (bis - perp).normalized;
            if (Vector3.Dot(right, a) < Vector3.Dot(up, a)) { var tmp = right; right = up; up = tmp; }

            var n = Vector3.Cross(right, up).normalized;
            if (Vector3.Dot(n, t) < 0f) n = -n;   // point away from the camera

            pose = new CameraSpacePose { position = t, right = right, up = up, intoWall = n };
            return true;
        }

        /// <summary>
        /// Homography from N >= 4 point pairs (h33 fixed at 1), least squares via the normal
        /// equations. With exactly 4 pairs this is the exact DLT solution. Row-major 3x3.
        /// </summary>
        static bool Homography(Vector2[] src, Vector2[] dst, out double[] H)
        {
            H = null;
            int n = Mathf.Min(src.Length, dst.Length);
            if (n < 4) return false;

            // Normalise both point sets (centre + scale) for numerical stability.
            Normalise(src, n, out var ts, out double ss);
            Normalise(dst, n, out var td, out double sd);

            var AtA = new double[8, 9];   // column 8 holds At*b
            var row = new double[9];
            for (int i = 0; i < n; i++)
            {
                double X = (src[i].x - ts.x) * ss, Y = (src[i].y - ts.y) * ss;
                double u = (dst[i].x - td.x) * sd, v = (dst[i].y - td.y) * sd;
                for (int k = 0; k < 2; k++)
                {
                    System.Array.Clear(row, 0, 9);
                    if (k == 0) { row[0] = X; row[1] = Y; row[2] = 1; row[6] = -u * X; row[7] = -u * Y; row[8] = u; }
                    else        { row[3] = X; row[4] = Y; row[5] = 1; row[6] = -v * X; row[7] = -v * Y; row[8] = v; }
                    for (int a = 0; a < 8; a++)
                    {
                        if (row[a] == 0) continue;
                        for (int b = 0; b < 9; b++) AtA[a, b] += row[a] * row[b];
                    }
                }
            }

            for (int col = 0; col < 8; col++)
            {
                int piv = col;
                for (int r = col + 1; r < 8; r++)
                    if (System.Math.Abs(AtA[r, col]) > System.Math.Abs(AtA[piv, col])) piv = r;
                if (System.Math.Abs(AtA[piv, col]) < 1e-12) return false;
                if (piv != col)
                    for (int k = 0; k < 9; k++) { var tmp = AtA[col, k]; AtA[col, k] = AtA[piv, k]; AtA[piv, k] = tmp; }
                for (int r = 0; r < 8; r++)
                {
                    if (r == col) continue;
                    double f = AtA[r, col] / AtA[col, col];
                    for (int k = col; k < 9; k++) AtA[r, k] -= f * AtA[col, k];
                }
            }
            var Hn = new double[9];
            for (int i = 0; i < 8; i++) Hn[i] = AtA[i, 8] / AtA[i, i];
            Hn[8] = 1;

            // Undo normalisation: H = Td^-1 * Hn * Ts.
            var Ts = new double[] { ss, 0, -ss * ts.x, 0, ss, -ss * ts.y, 0, 0, 1 };
            var TdInv = new double[] { 1 / sd, 0, td.x, 0, 1 / sd, td.y, 0, 0, 1 };
            var M = Mul(TdInv, Mul(Hn, Ts));
            if (System.Math.Abs(M[8]) < 1e-12) return false;
            H = new double[9];
            for (int i = 0; i < 9; i++) H[i] = M[i] / M[8];
            return true;
        }

        static void Normalise(Vector2[] p, int n, out Vector2 centre, out double scale)
        {
            centre = Vector2.zero;
            for (int i = 0; i < n; i++) centre += p[i];
            centre /= n;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += (p[i] - centre).magnitude;
            mean /= n;
            scale = mean > 1e-12 ? System.Math.Sqrt(2) / mean : 1;
        }

        static double[] Mul(double[] a, double[] b)
        {
            var c = new double[9];
            for (int r = 0; r < 3; r++)
            for (int k = 0; k < 3; k++)
            {
                double s = 0;
                for (int j = 0; j < 3; j++) s += a[r * 3 + j] * b[j * 3 + k];
                c[r * 3 + k] = s;
            }
            return c;
        }
    }
}
