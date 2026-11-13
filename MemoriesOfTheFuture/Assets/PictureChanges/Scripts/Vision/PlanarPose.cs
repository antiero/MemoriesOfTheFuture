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

        /// <param name="board">Board points (metres) in the board plane, X right, Y up.</param>
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

        /// <summary>DLT homography from 4 point pairs, h33 fixed at 1. Returns a row-major 3x3.</summary>
        static bool Homography(Vector2[] src, Vector2[] dst, out double[] H)
        {
            H = null;
            var A = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double X = src[i].x, Y = src[i].y, u = dst[i].x, v = dst[i].y;
                int r = i * 2;
                A[r, 0] = X; A[r, 1] = Y; A[r, 2] = 1; A[r, 6] = -u * X; A[r, 7] = -u * Y; A[r, 8] = u;
                A[r + 1, 3] = X; A[r + 1, 4] = Y; A[r + 1, 5] = 1; A[r + 1, 6] = -v * X; A[r + 1, 7] = -v * Y; A[r + 1, 8] = v;
            }
            // Gaussian elimination with partial pivoting on the 8x8 system (column 8 is the RHS).
            for (int col = 0; col < 8; col++)
            {
                int piv = col;
                for (int r = col + 1; r < 8; r++)
                    if (System.Math.Abs(A[r, col]) > System.Math.Abs(A[piv, col])) piv = r;
                if (System.Math.Abs(A[piv, col]) < 1e-12) return false;
                if (piv != col)
                    for (int k = 0; k < 9; k++) { var tmp = A[col, k]; A[col, k] = A[piv, k]; A[piv, k] = tmp; }
                for (int r = 0; r < 8; r++)
                {
                    if (r == col) continue;
                    double f = A[r, col] / A[col, col];
                    for (int k = col; k < 9; k++) A[r, k] -= f * A[col, k];
                }
            }
            H = new double[9];
            for (int i = 0; i < 8; i++) H[i] = A[i, 8] / A[i, i];
            H[8] = 1;
            return true;
        }
    }
}
