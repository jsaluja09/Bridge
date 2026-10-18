using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public enum ResolvedKind
    {
        Line,
        Arc,
        Polyline,
        Invalid,
    }

    public enum InvalidReason
    {
        None,
        TooShort,
        TooNoisy,
        NonPlanar,
        SelfIntersecting,
    }

    /// <summary>
    /// A stroke resolved to clean geometry (W4-01). Line: Points holds the two ends. Polyline: the ends and every
    /// corner, in order. Arc: Points holds the two ends on the circle; Center, Normal, Radius and Sweep (radians,
    /// signed about Normal, from the first end to the last) describe the circle. Room meters.
    /// </summary>
    public sealed class ResolvedShape
    {
        public ResolvedKind Kind;
        public InvalidReason Reason;
        public List<Vec3> Points = new List<Vec3>();
        public Vec3 Center;
        public Vec3 Normal;
        public double Radius;
        public double Sweep;
        /// <summary>RMS distance from the smoothed stroke to this shape, over the stroke's length.</summary>
        public double Residual;
        /// <summary>The stroke fit no shape and was replaced by a straight line between its ends (forgiving drawing).</summary>
        public bool Straightened;

        public bool Valid => Kind != ResolvedKind.Invalid;
        public int Segments => Kind == ResolvedKind.Polyline ? Points.Count - 1 : Kind == ResolvedKind.Invalid ? 0 : 1;
        public int Corners => Kind == ResolvedKind.Polyline ? Points.Count - 2 : 0;

        public static ResolvedShape Invalid(InvalidReason reason, double residual = 0) =>
            new ResolvedShape { Kind = ResolvedKind.Invalid, Reason = reason, Residual = residual };

        /// <summary>
        /// The clean geometry as a path ready for equal-segment snapping: the ends and corners for lines and
        /// polylines; for an arc, points along it no farther apart than maxStep.
        /// </summary>
        public List<Vec3> Path(double maxStep = 0.01)
        {
            if (Kind != ResolvedKind.Arc)
                return new List<Vec3>(Points);
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(Sweep) * Radius / maxStep));
            var path = new List<Vec3>(n + 1);
            Vec3 start = Points[0] - Center;
            for (int i = 0; i <= n; i++)
                path.Add(i == 0 ? Points[0] : i == n ? Points[1] : Center + ShapeResolver.Rotate(start, Normal, Sweep * i / n));
            return path;
        }
    }

    /// <summary>
    /// Resolves a hand-drawn stroke to one shape from a finite set (W4-01, spec 11.5): a line, a circular arc, or a
    /// polyline of 2 to N straight pieces (triangle, zigzag, W). Every candidate is fitted to the smoothed stroke;
    /// the lowest residual wins, with a bias toward the simpler shape when they're close. A stroke that fits
    /// nothing is Invalid with a reason. Deterministic: the same stroke always resolves the same way.
    /// </summary>
    public static class ShapeResolver
    {
        public static ResolvedShape Resolve(IReadOnlyList<Vec3> stroke)
        {
            if (stroke == null || stroke.Count < 2)
                return ResolvedShape.Invalid(InvalidReason.TooShort);
            double length = Geometry.PathLength(stroke);
            if (!(length >= Constants.Resolver.ResolverMinStrokeLength))
                return ResolvedShape.Invalid(InvalidReason.TooShort);

            var path = Smooth(Geometry.Resample(stroke, Constants.Resolver.ResolverSampleSpacing), Constants.Resolver.ResolverSmoothingWindow);
            length = Geometry.PathLength(path);
            Geometry.FitPlane(path, out Vec3 centroid, out Vec3 normal, out double planeResidual);
            bool planar = planeResidual <= Constants.Resolver.ResolverPlanarityTolerance;

            var candidates = new List<(ResolvedShape shape, int complexity)> { (FitLine(path, length), 0) };
            if (planar)
            {
                var arc = FitArc(path, length, centroid, normal);
                if (arc != null)
                    candidates.Add((arc, 1));
                var poly = FitPolyline(path, length);
                if (poly != null)
                    candidates.Add((poly, poly.Segments));
            }

            ResolvedShape best = null;
            double bestScore = double.MaxValue;
            foreach (var (shape, complexity) in candidates)
            {
                if (length > ShapeLength(shape) * Constants.Resolver.ResolverMaxPathRatio)
                    continue;                 // the hand wandered far more than this shape explains
                double score = shape.Residual + Constants.Resolver.ResolverSimplicityBias * complexity;
                if (score < bestScore)
                {
                    best = shape;
                    bestScore = score;
                }
            }

            if (best == null || best.Residual > Constants.Resolver.ResolverRejectTolerance)
            {
                double residual = best?.Residual ?? candidates[0].shape.Residual;
                if (!planar)
                    return ResolvedShape.Invalid(InvalidReason.NonPlanar, residual);
                if (SelfIntersects(path, normal, Closed(path, length)))
                    return ResolvedShape.Invalid(InvalidReason.SelfIntersecting, residual);
                return ResolvedShape.Invalid(InvalidReason.TooNoisy, residual);
            }
            if (best.Kind == ResolvedKind.Polyline && SelfIntersects(best.Points, normal, Closed(best.Points, length)))
                return ResolvedShape.Invalid(InvalidReason.SelfIntersecting, best.Residual);
            Check(best);
            return best;
        }

        /// <summary>
        /// Resolve, but forgiving: a stroke that fits no shape becomes a straight line from its first point to its
        /// last (marked Straightened), so a messy stroke still builds something. Only a stroke whose ends are too
        /// close together to make a beam stays Invalid (TooShort).
        /// </summary>
        public static ResolvedShape ResolveOrStraighten(IReadOnlyList<Vec3> stroke)
        {
            var shape = Resolve(stroke);
            if (shape.Valid || shape.Reason == InvalidReason.TooShort)
                return shape;
            Vec3 a = stroke[0], b = stroke[stroke.Count - 1];
            if (!(Vec3.Distance(a, b) >= Constants.Resolver.ResolverMinStrokeLength))
                return ResolvedShape.Invalid(InvalidReason.TooShort, shape.Residual);
            return new ResolvedShape { Kind = ResolvedKind.Line, Points = { a, b }, Residual = shape.Residual, Straightened = true };
        }

        // ---------- Candidates ----------

        /// <summary>The best-fit line through the stroke, from the first end's projection to the last's.</summary>
        static ResolvedShape FitLine(List<Vec3> path, double length)
        {
            FitAxis(path, 0, path.Count - 1, out Vec3 mid, out Vec3 dir);
            Vec3 a = Project(path[0], mid, dir), b = Project(path[path.Count - 1], mid, dir);
            var shape = new ResolvedShape { Kind = ResolvedKind.Line, Points = { a, b } };
            shape.Residual = Rms(path, p => Geometry.DistanceToSegment(p, a, b)) / length;
            return shape;
        }

        /// <summary>A circle fitted in the stroke's plane (algebraic fit), cut to the swept angle. Null if no sensible arc.</summary>
        static ResolvedShape FitArc(List<Vec3> path, double length, Vec3 origin, Vec3 normal)
        {
            Geometry.PlaneBasis(normal, out Vec3 u, out Vec3 v);
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sz = 0, sxz = 0, syz = 0;
            int n = path.Count;
            foreach (var p in path)
            {
                Vec3 d = p - origin;
                double x = Vec3.Dot(d, u), y = Vec3.Dot(d, v), z = x * x + y * y;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sz += z; sxz += x * z; syz += y * z;
            }
            if (!Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, n, -sxz, -syz, -sz, out double D, out double E, out double F))
                return null;
            double cx = -D / 2, cy = -E / 2, r2 = cx * cx + cy * cy - F;
            if (!(r2 > 0))
                return null;
            double radius = Math.Sqrt(r2);

            // The algebraic fit is biased on short arcs: refine with Gauss-Newton on the true distances.
            var xs = new double[n];
            var ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                Vec3 d = path[i] - origin;
                xs[i] = Vec3.Dot(d, u);
                ys[i] = Vec3.Dot(d, v);
            }
            for (int it = 0; it < 20; it++)
            {
                double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0, g1 = 0, g2 = 0, g3 = 0;
                for (int i = 0; i < n; i++)
                {
                    double dx = xs[i] - cx, dy = ys[i] - cy, dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 1e-12)
                        continue;
                    // residual = dist - r; gradient w.r.t. (cx, cy, r) = (-dx/dist, -dy/dist, -1)
                    double jx = -dx / dist, jy = -dy / dist, jr = -1, res = dist - radius;
                    a11 += jx * jx; a12 += jx * jy; a13 += jx * jr; a22 += jy * jy; a23 += jy * jr; a33 += jr * jr;
                    g1 -= jx * res; g2 -= jy * res; g3 -= jr * res;
                }
                if (!Solve3(a11, a12, a13, a12, a22, a23, a13, a23, a33, g1, g2, g3, out double sx1, out double sy1, out double sr))
                    break;
                cx += sx1;
                cy += sy1;
                radius += sr;
                if (!(radius > 0) || !double.IsFinite(radius))
                    return null;
                if (Math.Abs(sx1) + Math.Abs(sy1) + Math.Abs(sr) < 1e-10)
                    break;
            }
            if (radius > length * 10)
                return null;                  // practically straight: the line covers it
            Vec3 center = origin + u * cx + v * cy;

            double traced = 0;
            for (int i = 1; i < n; i++)
            {
                double a0 = Angle(path[i - 1] - center, u, v), a1 = Angle(path[i] - center, u, v);
                double d = a1 - a0;
                while (d > Math.PI) d -= 2 * Math.PI;
                while (d < -Math.PI) d += 2 * Math.PI;
                traced += d;
            }
            double degrees = Math.Abs(traced) * 180 / Math.PI;
            if (degrees < Constants.Recognition.ArcSweepMinDegrees || degrees > Constants.Recognition.ArcSweepMaxDegrees)
                return null;

            Vec3 OnCircle(Vec3 p)
            {
                Vec3 d = p - center;
                Vec3 inPlane = d - normal * Vec3.Dot(d, normal);
                return inPlane.Length < 1e-12 ? center + u * radius : center + inPlane.Normalized() * radius;
            }
            var shape = new ResolvedShape
            {
                Kind = ResolvedKind.Arc,
                Points = { OnCircle(path[0]), OnCircle(path[n - 1]) },
                Center = center,
                Normal = normal,
                Radius = radius,
                Sweep = traced,
            };
            shape.Residual = Rms(path, p =>
            {
                Vec3 d = p - center;
                double off = Vec3.Dot(d, normal);
                double radial = (d - normal * off).Length - radius;
                return Math.Sqrt(off * off + radial * radial);
            }) / length;
            return shape;
        }

        /// <summary>
        /// Splits the stroke at its farthest point from the current pieces until every piece is straight, then puts
        /// each corner where the neighbouring pieces' best-fit lines meet. Null if it needs too many corners or is
        /// just one piece (that's a line).
        /// </summary>
        static ResolvedShape FitPolyline(List<Vec3> path, double length)
        {
            var cuts = new List<int> { 0, path.Count - 1 };
            while (true)
            {
                int worstPiece = -1, worstIndex = -1;
                double worst = Constants.Resolver.ResolverSegmentStraightness;
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    for (int i = cuts[k] + 1; i < cuts[k + 1]; i++)
                    {
                        double d = Geometry.DistanceToSegment(path[i], path[cuts[k]], path[cuts[k + 1]]);
                        if (d > worst)
                        {
                            worst = d;
                            worstPiece = k;
                            worstIndex = i;
                        }
                    }
                }
                if (worstPiece < 0)
                    break;
                if (cuts.Count - 2 >= Constants.Resolver.ResolverMaxPolylineCorners)
                    return null;
                cuts.Insert(worstPiece + 1, worstIndex);
            }
            if (cuts.Count < 3)
                return null;

            // Fit each piece away from its ends (the hand rounds corners), then meet the neighbours.
            int pieces = cuts.Count - 1;
            var mids = new Vec3[pieces];
            var dirs = new Vec3[pieces];
            for (int k = 0; k < pieces; k++)
            {
                int a = cuts[k], b = cuts[k + 1], trim = (b - a) / 5;
                FitAxis(path, a + trim, b - trim, out mids[k], out dirs[k]);
            }
            var shape = new ResolvedShape { Kind = ResolvedKind.Polyline };
            shape.Points.Add(Project(path[0], mids[0], dirs[0]));
            for (int k = 0; k + 1 < pieces; k++)
                shape.Points.Add(Meet(mids[k], dirs[k], mids[k + 1], dirs[k + 1], path[cuts[k + 1]]));
            shape.Points.Add(Project(path[path.Count - 1], mids[pieces - 1], dirs[pieces - 1]));
            for (int i = 1; i < shape.Points.Count; i++)
            {
                if (Vec3.Distance(shape.Points[i], shape.Points[i - 1]) < Constants.Resolver.ResolverSampleSpacing)
                    return null;              // a piece collapsed: not a clean polyline
            }
            shape.Residual = Rms(path, p => DistanceToPolyline(p, shape.Points)) / length;
            return shape;
        }

        static double ShapeLength(ResolvedShape s) =>
            s.Kind == ResolvedKind.Arc ? Math.Abs(s.Sweep) * s.Radius : Geometry.PathLength(s.Points);

        // ---------- Checks ----------

        static bool Closed(List<Vec3> pts, double length) =>
            Vec3.Distance(pts[0], pts[pts.Count - 1]) <= Math.Max(Constants.Draw.SnapRadiusMeters, length * 0.05);

        /// <summary>Do two non-neighbouring pieces cross, seen in the stroke's plane? A closed shape may touch at its ends.</summary>
        static bool SelfIntersects(List<Vec3> pts, Vec3 normal, bool closed)
        {
            Geometry.PlaneBasis(normal, out Vec3 u, out Vec3 v);
            int n = pts.Count;
            var flat = new Vec2[n];
            for (int i = 0; i < n; i++)
                flat[i] = new Vec2(Vec3.Dot(pts[i], u), Vec3.Dot(pts[i], v));
            for (int i = 0; i + 1 < n; i++)
            {
                for (int j = i + 2; j + 1 < n; j++)
                {
                    if (closed && i == 0 && j == n - 2)
                        continue;
                    if (Cross(flat[i], flat[i + 1], flat[j], flat[j + 1]))
                        return true;
                }
            }
            return false;
        }

        static bool Cross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            double d1 = Vec2.Cross(b - a, c - a), d2 = Vec2.Cross(b - a, d - a);
            double d3 = Vec2.Cross(d - c, a - c), d4 = Vec2.Cross(d - c, b - c);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        static void Check(ResolvedShape s)
        {
            bool ok = double.IsFinite(s.Residual) && double.IsFinite(s.Radius) && double.IsFinite(s.Sweep) && s.Center.IsFinite;
            foreach (var p in s.Points)
                ok &= p.IsFinite;
            if (!ok)
                throw new InvalidOperationException("Resolved shape is NaN or Infinity");
        }

        // ---------- Helpers ----------

        static List<Vec3> Smooth(List<Vec3> pts, int window)
        {
            if (pts.Count < 3 || window < 1)
                return pts;
            var result = new List<Vec3>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                int lo = Math.Max(0, i - window), hi = Math.Min(pts.Count - 1, i + window);
                Vec3 sum = Vec3.Zero;
                for (int j = lo; j <= hi; j++)
                    sum += pts[j];
                result.Add(sum / (hi - lo + 1));
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        /// <summary>Principal direction of pts[from..to] (power iteration on the covariance), oriented first to last.</summary>
        static void FitAxis(List<Vec3> pts, int from, int to, out Vec3 mid, out Vec3 dir)
        {
            if (to <= from)
            {
                from = Math.Max(0, from - 1);
                to = Math.Min(pts.Count - 1, to + 1);
            }
            Vec3 sum = Vec3.Zero;
            for (int i = from; i <= to; i++)
                sum += pts[i];
            mid = sum / (to - from + 1);
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int i = from; i <= to; i++)
            {
                Vec3 d = pts[i] - mid;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
                yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }
            Vec3 chord = pts[to] - pts[from];
            dir = chord.LengthSquared > 1e-18 ? chord.Normalized() : Vec3.UnitX;
            for (int it = 0; it < 50; it++)
            {
                var next = new Vec3(xx * dir.X + xy * dir.Y + xz * dir.Z, xy * dir.X + yy * dir.Y + yz * dir.Z, xz * dir.X + yz * dir.Y + zz * dir.Z);
                if (next.LengthSquared < 1e-30)
                    break;
                dir = next.Normalized();
            }
            if (Vec3.Dot(dir, chord) < 0)
                dir = -dir;
        }

        static Vec3 Project(Vec3 p, Vec3 mid, Vec3 dir) => mid + dir * Vec3.Dot(p - mid, dir);

        /// <summary>Midpoint of the closest points of two lines; near-parallel lines meet at the fallback sample.</summary>
        static Vec3 Meet(Vec3 p1, Vec3 d1, Vec3 p2, Vec3 d2, Vec3 fallback)
        {
            Vec3 w = p1 - p2;
            double b = Vec3.Dot(d1, d2), d = Vec3.Dot(d1, w), e = Vec3.Dot(d2, w);
            double denom = 1 - b * b;
            if (denom < 1e-6)
                return fallback;
            double s = (b * e - d) / denom, t = (e - b * d) / denom;
            Vec3 meet = (p1 + d1 * s + p2 + d2 * t) * 0.5;
            // A corner far from where the hand turned means the fit went wrong; trust the hand.
            return Vec3.Distance(meet, fallback) <= Constants.Resolver.ResolverSegmentStraightness * 4 ? meet : fallback;
        }

        static double DistanceToPolyline(Vec3 p, List<Vec3> pts)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < pts.Count; i++)
                best = Math.Min(best, Geometry.DistanceToSegment(p, pts[i], pts[i + 1]));
            return best;
        }

        static double Rms(List<Vec3> pts, Func<Vec3, double> distance)
        {
            double sum = 0;
            foreach (var p in pts)
            {
                double d = distance(p);
                sum += d * d;
            }
            return Math.Sqrt(sum / pts.Count);
        }

        static double Angle(Vec3 d, Vec3 u, Vec3 v) => Math.Atan2(Vec3.Dot(d, v), Vec3.Dot(d, u));

        /// <summary>Rodrigues: v turned by angle about the unit axis k.</summary>
        internal static Vec3 Rotate(Vec3 v, Vec3 k, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c));
        }

        static bool Solve3(double a, double b, double c, double d, double e, double f, double g, double h, double i,
                           double r0, double r1, double r2, out double x, out double y, out double z)
        {
            double det = Det(a, b, c, d, e, f, g, h, i);
            x = y = z = 0;
            if (Math.Abs(det) < 1e-18)
                return false;
            x = Det(r0, b, c, r1, e, f, r2, h, i) / det;
            y = Det(a, r0, c, d, r1, f, g, r2, i) / det;
            z = Det(a, b, r0, d, e, r1, g, h, r2) / det;
            return true;
        }

        static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
            a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }
}
