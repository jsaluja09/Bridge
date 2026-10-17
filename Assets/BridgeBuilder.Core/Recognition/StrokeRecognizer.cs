using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Classifies a 3D stroke as a line, an arc, or a polyline (spec 2.1). Decision order:
    /// degenerate, snap start, line, non-planar, arc, polyline, otherwise unrecognized.
    /// </summary>
    public sealed class StrokeRecognizer : IStrokeRecognizer
    {
        public static RecognizerTolerances DefaultTolerances => new RecognizerTolerances
        {
            SnapStartToleranceMeters = Constants.Recognition.SnapStartToleranceMeters,
            LineResidualMeters = Constants.Recognition.LineResidualMeters,
            PlaneResidualMeters = Constants.Recognition.PlaneResidualMeters,
            ArcResidualMeters = Constants.Recognition.ArcResidualMeters,
            CornerAngleDegrees = Constants.Recognition.CornerAngleDegrees,
            ArcSweepMinDegrees = Constants.Recognition.ArcSweepMinDegrees,
            ArcSweepMaxDegrees = Constants.Recognition.ArcSweepMaxDegrees,
        };

        public RecognitionResult Recognize(IReadOnlyList<Vec3> stroke, IReadOnlyList<Vec3> snapPoints) =>
            Recognize(stroke, snapPoints, DefaultTolerances);

        public RecognitionResult Recognize(IReadOnlyList<Vec3> stroke, IReadOnlyList<Vec3> snapPoints, RecognizerTolerances tol)
        {
            if (stroke == null || Distinct(stroke) < 2)
                return Reject(RejectionReason.Degenerate);

            bool snapped = false;
            foreach (var s in snapPoints)
            {
                if (Vec3.Distance(s, stroke[0]) <= tol.SnapStartToleranceMeters)
                {
                    snapped = true;
                    break;
                }
            }
            if (!snapped)
                return Reject(RejectionReason.SnapStart);

            Vec3 first = stroke[0], last = stroke[stroke.Count - 1];
            if (LineResidual(stroke, 0, stroke.Count - 1) <= tol.LineResidualMeters)
                return new RecognitionResult { Kind = StrokeKind.Line, Corners = { first, last } };

            Geometry.FitPlane(stroke, out Vec3 centroid, out Vec3 normal, out double planeResidual);
            if (planeResidual > tol.PlaneResidualMeters)
                return Reject(RejectionReason.NonPlanar);

            if (FitCircle(stroke, centroid, normal, out Vec3 center, out double radius, out double radial, out double sweep) &&
                radial <= tol.ArcResidualMeters && sweep >= tol.ArcSweepMinDegrees && sweep <= tol.ArcSweepMaxDegrees)
            {
                return new RecognitionResult
                {
                    Kind = StrokeKind.Arc,
                    Corners = { first, last },
                    ArcCenter = center,
                    ArcNormal = normal,
                    ArcSweepDegrees = sweep,
                };
            }

            var corners = Corners(stroke, tol.CornerAngleDegrees);
            if (corners.Count >= 3)
            {
                bool straight = true;
                for (int c = 0; c + 1 < corners.Count && straight; c++)
                    straight = LineResidual(stroke, corners[c], corners[c + 1]) <= tol.LineResidualMeters;
                if (straight)
                {
                    var result = new RecognitionResult { Kind = StrokeKind.Polyline };
                    foreach (int c in corners)
                        result.Corners.Add(stroke[c]);
                    return result;
                }
            }
            return Reject(RejectionReason.Unrecognized);
        }

        static RecognitionResult Reject(RejectionReason reason) => new RecognitionResult { Kind = StrokeKind.Rejected, RejectionReason = reason };

        static int Distinct(IReadOnlyList<Vec3> pts)
        {
            int count = 1;
            for (int i = 1; i < pts.Count && count < 2; i++)
            {
                if (Vec3.Distance(pts[i], pts[0]) > 1e-12)
                    count++;
            }
            return count;
        }

        /// <summary>Max orthogonal distance of samples from..to from the chord between them.</summary>
        static double LineResidual(IReadOnlyList<Vec3> pts, int from, int to)
        {
            Vec3 a = pts[from], b = pts[to];
            Vec3 ab = b - a;
            double len = ab.Length;
            double max = 0;
            for (int i = from; i <= to; i++)
            {
                Vec3 d = pts[i] - a;
                double dist = len < 1e-12 ? d.Length : Vec3.Cross(d, ab).Length / len;
                max = Math.Max(max, dist);
            }
            return max;
        }

        /// <summary>Algebraic (Kasa) circle fit in the stroke's plane; sweep is the angle traced, in degrees.</summary>
        static bool FitCircle(IReadOnlyList<Vec3> pts, Vec3 origin, Vec3 normal, out Vec3 center, out double radius, out double radial, out double sweep)
        {
            Geometry.PlaneBasis(normal, out Vec3 u, out Vec3 v);
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sz = 0, sxz = 0, syz = 0;
            int n = pts.Count;
            var xs = new double[n];
            var ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                Vec3 d = pts[i] - origin;
                double x = Vec3.Dot(d, u), y = Vec3.Dot(d, v), z = x * x + y * y;
                xs[i] = x;
                ys[i] = y;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sz += z; sxz += x * z; syz += y * z;
            }
            // Solve [sxx sxy sx; sxy syy sy; sx sy n] [D E F] = -[sxz syz sz].
            double[,] m = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
            double[] rhs = { -sxz, -syz, -sz };
            center = origin;
            radius = radial = sweep = 0;
            if (!Solve3(m, rhs, out double D, out double E, out double F))
                return false;
            double cx = -D / 2, cy = -E / 2, r2 = cx * cx + cy * cy - F;
            if (!(r2 > 0))
                return false;
            radius = Math.Sqrt(r2);
            center = origin + u * cx + v * cy;

            double traced = 0;
            for (int i = 0; i < n; i++)
            {
                radial = Math.Max(radial, Math.Abs(Math.Sqrt((xs[i] - cx) * (xs[i] - cx) + (ys[i] - cy) * (ys[i] - cy)) - radius));
                if (i > 0)
                {
                    double a0 = Math.Atan2(ys[i - 1] - cy, xs[i - 1] - cx), a1 = Math.Atan2(ys[i] - cy, xs[i] - cx);
                    double d = a1 - a0;
                    while (d > Math.PI) d -= 2 * Math.PI;
                    while (d < -Math.PI) d += 2 * Math.PI;
                    traced += d;
                }
            }
            sweep = Math.Abs(traced) * 180 / Math.PI;
            return true;
        }

        static bool Solve3(double[,] m, double[] b, out double x, out double y, out double z)
        {
            double det = Det(m[0, 0], m[0, 1], m[0, 2], m[1, 0], m[1, 1], m[1, 2], m[2, 0], m[2, 1], m[2, 2]);
            x = y = z = 0;
            if (Math.Abs(det) < 1e-18)
                return false;
            x = Det(b[0], m[0, 1], m[0, 2], b[1], m[1, 1], m[1, 2], b[2], m[2, 1], m[2, 2]) / det;
            y = Det(m[0, 0], b[0], m[0, 2], m[1, 0], b[1], m[1, 2], m[2, 0], b[2], m[2, 2]) / det;
            z = Det(m[0, 0], m[0, 1], b[0], m[1, 0], m[1, 1], b[1], m[2, 0], m[2, 1], b[2]) / det;
            return true;
        }

        static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
            a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

        /// <summary>Indices of the ends and every sample where the direction turns by more than the corner angle.</summary>
        static List<int> Corners(IReadOnlyList<Vec3> pts, double cornerDegrees)
        {
            var corners = new List<int> { 0 };
            Vec3 previous = Vec3.Zero;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vec3 d = pts[i + 1] - pts[i];
                if (d.Length < 1e-12)
                    continue;
                d = d.Normalized();
                if (previous.LengthSquared > 0)
                {
                    double angle = Math.Acos(Math.Clamp(Vec3.Dot(previous, d), -1, 1)) * 180 / Math.PI;
                    if (angle > cornerDegrees)
                        corners.Add(i);
                }
                previous = d;
            }
            if (corners[corners.Count - 1] != pts.Count - 1)
                corners.Add(pts.Count - 1);
            return corners;
        }
    }
}
