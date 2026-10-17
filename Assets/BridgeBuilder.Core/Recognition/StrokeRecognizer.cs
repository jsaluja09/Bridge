using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Classifies a freehand 3D stroke as a line, an arc, or a polyline (spec 2.1). The checks run in a
    /// fixed order: degenerate, snap start, line, plane, arc, polyline, and otherwise unrecognized.
    /// Consecutive repeated samples are dropped first, so a controller held still adds no kinks.
    /// A sample with an infinite component makes the stroke <see cref="RejectionReason.Degenerate"/>.
    /// </summary>
    public sealed class StrokeRecognizer : IStrokeRecognizer
    {
        private const int JacobiMaxSweeps = 50;
        private const int CircleRefinementMaxIterations = 20;
        private const double DegreesPerRadian = 180.0 / Math.PI;

        /// <summary>Tolerances read from <see cref="Constants.Recognition"/>.</summary>
        public static RecognizerTolerances DefaultTolerances => new RecognizerTolerances(
            Constants.Recognition.SnapStartToleranceMeters,
            Constants.Recognition.LineResidualMeters,
            Constants.Recognition.PlaneResidualMeters,
            Constants.Recognition.ArcResidualMeters,
            Constants.Recognition.CornerAngleDegrees,
            Constants.Recognition.ArcSweepMinDegrees,
            Constants.Recognition.ArcSweepMaxDegrees);

        /// <summary>Recognizes <paramref name="stroke"/> with <see cref="DefaultTolerances"/>.</summary>
        public RecognitionResult Recognize(IReadOnlyList<Vec3> stroke, IReadOnlyList<Vec3> snapPoints)
        {
            return Recognize(stroke, snapPoints, DefaultTolerances);
        }

        public RecognitionResult Recognize(IReadOnlyList<Vec3> stroke, IReadOnlyList<Vec3> snapPoints, RecognizerTolerances tolerances)
        {
            if (stroke == null)
            {
                throw new ArgumentNullException(nameof(stroke));
            }

            if (snapPoints == null)
            {
                throw new ArgumentNullException(nameof(snapPoints));
            }

            if (!TryDropRepeatedSamples(stroke, out Vec3[] points) || points.Length < 2)
            {
                return RecognitionResult.Reject(RejectionReason.Degenerate);
            }

            if (!StartsAtSnapPoint(points[0], snapPoints, tolerances.SnapStartToleranceMeters))
            {
                return RecognitionResult.Reject(RejectionReason.SnapStart);
            }

            int last = points.Length - 1;
            Axes axes = PrincipalAxes(points, 0, last);
            var line = new FittedLine(axes.Centroid, axes.Primary);
            if (MaxDistance(line, points, 0, last) <= tolerances.LineResidualMeters)
            {
                return Accept(StrokeKind.Line, new[] { line.Project(points[0]), line.Project(points[last]) });
            }

            if (MaxPlaneDistance(axes, points) > tolerances.PlaneResidualMeters)
            {
                return RecognitionResult.Reject(RejectionReason.NonPlanar);
            }

            if (TryFitArc(points, axes, tolerances, out Vec3[] arcVertices))
            {
                return Accept(StrokeKind.Arc, arcVertices);
            }

            if (TryFitPolyline(points, tolerances, out Vec3[] polylineVertices))
            {
                return Accept(StrokeKind.Polyline, polylineVertices);
            }

            return RecognitionResult.Reject(RejectionReason.Unrecognized);
        }

        private static RecognitionResult Accept(StrokeKind kind, Vec3[] vertices)
        {
            foreach (Vec3 vertex in vertices)
            {
                if (!IsFinite(vertex))
                {
                    return RecognitionResult.Reject(RejectionReason.Unrecognized);
                }
            }

            return RecognitionResult.Accepted(kind, vertices);
        }

        private static bool TryDropRepeatedSamples(IReadOnlyList<Vec3> stroke, out Vec3[] points)
        {
            var kept = new List<Vec3>(stroke.Count);
            for (int i = 0; i < stroke.Count; i++)
            {
                Vec3 sample = stroke[i];
                if (!IsFinite(sample))
                {
                    points = Array.Empty<Vec3>();
                    return false;
                }

                if (kept.Count == 0 || sample != kept[kept.Count - 1])
                {
                    kept.Add(sample);
                }
            }

            points = kept.ToArray();
            return true;
        }

        private static bool StartsAtSnapPoint(Vec3 start, IReadOnlyList<Vec3> snapPoints, double toleranceMeters)
        {
            for (int i = 0; i < snapPoints.Count; i++)
            {
                if (Vec3.Distance(start, snapPoints[i]) <= toleranceMeters)
                {
                    return true;
                }
            }

            return false;
        }

        private static double MaxDistance(FittedLine line, Vec3[] points, int first, int last)
        {
            double max = 0.0;
            for (int i = first; i <= last; i++)
            {
                max = Math.Max(max, line.DistanceTo(points[i]));
            }

            return max;
        }

        private static double MaxPlaneDistance(Axes axes, Vec3[] points)
        {
            double max = 0.0;
            foreach (Vec3 point in points)
            {
                max = Math.Max(max, Math.Abs(Vec3.Dot(point - axes.Centroid, axes.Normal)));
            }

            return max;
        }

        private static FittedLine FitLine(Vec3[] points, int first, int last)
        {
            Axes axes = PrincipalAxes(points, first, last);
            return new FittedLine(axes.Centroid, axes.Primary);
        }

        private static double LineResidual(Vec3[] points, int first, int last)
        {
            return MaxDistance(FitLine(points, first, last), points, first, last);
        }

        /// <summary>
        /// Total least squares fit of points[first..last]: the centroid and the scatter matrix's
        /// eigenvectors by descending eigenvalue. The first is the best-fit line direction and the last is
        /// the best-fit plane normal.
        /// </summary>
        private static Axes PrincipalAxes(Vec3[] points, int first, int last)
        {
            int count = last - first + 1;
            double sumX = 0.0;
            double sumY = 0.0;
            double sumZ = 0.0;
            for (int i = first; i <= last; i++)
            {
                sumX += points[i].X;
                sumY += points[i].Y;
                sumZ += points[i].Z;
            }

            var centroid = new Vec3(sumX / count, sumY / count, sumZ / count);
            var scatter = new double[3, 3];
            for (int i = first; i <= last; i++)
            {
                Vec3 d = points[i] - centroid;
                scatter[0, 0] += d.X * d.X;
                scatter[0, 1] += d.X * d.Y;
                scatter[0, 2] += d.X * d.Z;
                scatter[1, 1] += d.Y * d.Y;
                scatter[1, 2] += d.Y * d.Z;
                scatter[2, 2] += d.Z * d.Z;
            }

            scatter[1, 0] = scatter[0, 1];
            scatter[2, 0] = scatter[0, 2];
            scatter[2, 1] = scatter[1, 2];
            Vec3[] vectors = EigenvectorsByDescendingValue(scatter);
            return new Axes(centroid, vectors[0], vectors[1], vectors[2]);
        }

        /// <summary>Cyclic Jacobi eigen decomposition of a symmetric 3 by 3 matrix, which it overwrites.</summary>
        private static Vec3[] EigenvectorsByDescendingValue(double[,] a)
        {
            var v = new double[3, 3] { { 1.0, 0.0, 0.0 }, { 0.0, 1.0, 0.0 }, { 0.0, 0.0, 1.0 } };
            for (int sweep = 0; sweep < JacobiMaxSweeps; sweep++)
            {
                if (a[0, 1] == 0.0 && a[0, 2] == 0.0 && a[1, 2] == 0.0)
                {
                    break;
                }

                JacobiRotate(a, v, 0, 1);
                JacobiRotate(a, v, 0, 2);
                JacobiRotate(a, v, 1, 2);
            }

            int[] order = { 0, 1, 2 };
            Array.Sort(order, (i, j) =>
            {
                int byValue = a[j, j].CompareTo(a[i, i]);
                return byValue != 0 ? byValue : i.CompareTo(j);
            });

            var vectors = new Vec3[3];
            for (int k = 0; k < 3; k++)
            {
                int column = order[k];
                vectors[k] = new Vec3(v[0, column], v[1, column], v[2, column]);
            }

            return vectors;
        }

        private static void JacobiRotate(double[,] a, double[,] v, int p, int q)
        {
            double apq = a[p, q];
            if (apq == 0.0)
            {
                return;
            }

            double theta = (a[q, q] - a[p, p]) / (2.0 * apq);
            double t = (theta >= 0.0 ? 1.0 : -1.0) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
            double c = 1.0 / Math.Sqrt(t * t + 1.0);
            double s = t * c;
            for (int k = 0; k < 3; k++)
            {
                double akp = a[k, p];
                double akq = a[k, q];
                a[k, p] = c * akp - s * akq;
                a[k, q] = s * akp + c * akq;
            }

            for (int k = 0; k < 3; k++)
            {
                double apk = a[p, k];
                double aqk = a[q, k];
                a[p, k] = c * apk - s * aqk;
                a[q, k] = s * apk + c * aqk;
            }

            a[p, q] = 0.0;
            a[q, p] = 0.0;
            for (int k = 0; k < 3; k++)
            {
                double vkp = v[k, p];
                double vkq = v[k, q];
                v[k, p] = c * vkp - s * vkq;
                v[k, q] = s * vkp + c * vkq;
            }
        }

        /// <summary>
        /// Fits a circle to the samples projected into the stroke's plane: an algebraic (Kasa) fit, then
        /// Gauss-Newton steps on the geometric distance that are kept only while they lower the error.
        /// </summary>
        private static bool TryFitArc(Vec3[] points, Axes plane, RecognizerTolerances tolerances, out Vec3[] vertices)
        {
            vertices = null;
            int n = points.Length;
            var x = new double[n];
            var y = new double[n];
            double meanX = 0.0;
            double meanY = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vec3 d = points[i] - plane.Centroid;
                x[i] = Vec3.Dot(d, plane.Primary);
                y[i] = Vec3.Dot(d, plane.Secondary);
                meanX += x[i];
                meanY += y[i];
            }

            meanX /= n;
            meanY /= n;
            double sxx = 0.0;
            double syy = 0.0;
            double sxy = 0.0;
            double sxz = 0.0;
            double syz = 0.0;
            double sz = 0.0;
            for (int i = 0; i < n; i++)
            {
                x[i] -= meanX;
                y[i] -= meanY;
                double z = x[i] * x[i] + y[i] * y[i];
                sxx += x[i] * x[i];
                syy += y[i] * y[i];
                sxy += x[i] * y[i];
                sxz += x[i] * z;
                syz += y[i] * z;
                sz += z;
            }

            double determinant = sxx * syy - sxy * sxy;
            if (!(determinant > 0.0))
            {
                return false;
            }

            double a = (sxz * syy - syz * sxy) / (2.0 * determinant);
            double b = (syz * sxx - sxz * sxy) / (2.0 * determinant);
            double r = Math.Sqrt(a * a + b * b + sz / n);
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(r))
            {
                return false;
            }

            RefineCircle(x, y, ref a, ref b, ref r);

            double swept = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - a;
                double dy = y[i] - b;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (!(distance > 0.0) || Math.Abs(distance - r) > tolerances.ArcResidualMeters)
                {
                    return false;
                }

                if (i > 0)
                {
                    double px = x[i - 1] - a;
                    double py = y[i - 1] - b;
                    swept += Math.Atan2(px * dy - py * dx, px * dx + py * dy);
                }
            }

            double sweptDegrees = Math.Abs(swept) * DegreesPerRadian;
            if (!(sweptDegrees >= tolerances.ArcSweepMinDegrees && sweptDegrees <= tolerances.ArcSweepMaxDegrees))
            {
                return false;
            }

            double startAngle = Math.Atan2(y[0] - b, x[0] - a);
            double endAngle = Math.Atan2(y[n - 1] - b, x[n - 1] - a);
            Vec3 center = plane.Centroid + plane.Primary * (meanX + a) + plane.Secondary * (meanY + b);
            vertices = new[]
            {
                PointOnCircle(center, plane, r, startAngle),
                PointOnCircle(center, plane, r, startAngle + 0.5 * swept),
                PointOnCircle(center, plane, r, endAngle),
            };
            return true;
        }

        private static Vec3 PointOnCircle(Vec3 center, Axes plane, double radius, double angle)
        {
            return center + plane.Primary * (radius * Math.Cos(angle)) + plane.Secondary * (radius * Math.Sin(angle));
        }

        private static void RefineCircle(double[] x, double[] y, ref double a, ref double b, ref double r)
        {
            double error = CircleSquaredError(x, y, a, b, r);
            for (int iteration = 0; iteration < CircleRefinementMaxIterations; iteration++)
            {
                double jaa = 0.0;
                double jab = 0.0;
                double jar = 0.0;
                double jbb = 0.0;
                double jbr = 0.0;
                double ga = 0.0;
                double gb = 0.0;
                double gr = 0.0;
                for (int i = 0; i < x.Length; i++)
                {
                    double dx = x[i] - a;
                    double dy = y[i] - b;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (!(distance > 0.0))
                    {
                        return;
                    }

                    double da = -dx / distance;
                    double db = -dy / distance;
                    double residual = distance - r;
                    jaa += da * da;
                    jab += da * db;
                    jar -= da;
                    jbb += db * db;
                    jbr -= db;
                    ga += da * residual;
                    gb += db * residual;
                    gr -= residual;
                }

                var normal = new Mat3(jaa, jab, jar, jab, jbb, jbr, jar, jbr, x.Length);
                double determinant = normal.Determinant;
                if (!(Math.Abs(determinant) > 0.0) || !IsFinite(determinant))
                {
                    return;
                }

                var rhs = new Vec3(-ga, -gb, -gr);
                double nextA = a + Mat3.FromColumns(rhs, normal.Column(1), normal.Column(2)).Determinant / determinant;
                double nextB = b + Mat3.FromColumns(normal.Column(0), rhs, normal.Column(2)).Determinant / determinant;
                double nextR = r + Mat3.FromColumns(normal.Column(0), normal.Column(1), rhs).Determinant / determinant;
                if (!IsFinite(nextA) || !IsFinite(nextB) || !(nextR > 0.0) || !IsFinite(nextR))
                {
                    return;
                }

                double nextError = CircleSquaredError(x, y, nextA, nextB, nextR);
                if (!(nextError < error))
                {
                    return;
                }

                a = nextA;
                b = nextB;
                r = nextR;
                error = nextError;
            }
        }

        private static double CircleSquaredError(double[] x, double[] y, double a, double b, double r)
        {
            double sum = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                double dx = x[i] - a;
                double dy = y[i] - b;
                double residual = Math.Sqrt(dx * dx + dy * dy) - r;
                sum += residual * residual;
            }

            return sum;
        }

        /// <summary>
        /// Splits at every sample whose direction change exceeds the corner angle and accepts when there are
        /// at least two segments, each within the line residual. Adjacent segments that still fit one line
        /// together are then merged, so depth-noise kinks inside a straight run do not become corners.
        /// </summary>
        private static bool TryFitPolyline(Vec3[] points, RecognizerTolerances tolerances, out Vec3[] vertices)
        {
            vertices = null;
            int last = points.Length - 1;
            var boundaries = new List<int> { 0 };
            for (int i = 1; i < last; i++)
            {
                if (DirectionChangeDegrees(points[i - 1], points[i], points[i + 1]) > tolerances.CornerAngleDegrees)
                {
                    boundaries.Add(i);
                }
            }

            boundaries.Add(last);
            if (boundaries.Count < 3)
            {
                return false;
            }

            for (int j = 0; j + 1 < boundaries.Count; j++)
            {
                if (LineResidual(points, boundaries[j], boundaries[j + 1]) > tolerances.LineResidualMeters)
                {
                    return false;
                }
            }

            MergeSegmentsThatFitOneLine(points, boundaries, tolerances.LineResidualMeters);

            int segmentCount = boundaries.Count - 1;
            var lines = new FittedLine[segmentCount];
            var lengths = new double[segmentCount];
            for (int j = 0; j < segmentCount; j++)
            {
                lines[j] = FitLine(points, boundaries[j], boundaries[j + 1]);
                lengths[j] = Vec3.Distance(points[boundaries[j]], points[boundaries[j + 1]]);
            }

            vertices = new Vec3[segmentCount + 1];
            vertices[0] = lines[0].Project(points[0]);
            for (int j = 1; j < segmentCount; j++)
            {
                vertices[j] = CornerVertex(lines[j - 1], lengths[j - 1], lines[j], lengths[j], points[boundaries[j]]);
            }

            vertices[segmentCount] = lines[segmentCount - 1].Project(points[last]);
            return true;
        }

        private static double DirectionChangeDegrees(Vec3 before, Vec3 at, Vec3 after)
        {
            Vec3 incoming = at - before;
            Vec3 outgoing = after - at;
            return Math.Atan2(Vec3.Cross(incoming, outgoing).Length, Vec3.Dot(incoming, outgoing)) * DegreesPerRadian;
        }

        /// <summary>
        /// Repeatedly merges the adjacent pair of segments whose union has the smallest line residual, while
        /// that residual is within the tolerance. The whole stroke already failed the line check, so at least
        /// two segments remain.
        /// </summary>
        private static void MergeSegmentsThatFitOneLine(Vec3[] points, List<int> boundaries, double lineResidualMeters)
        {
            var pairResiduals = new List<double>();
            for (int j = 0; j + 2 < boundaries.Count; j++)
            {
                pairResiduals.Add(LineResidual(points, boundaries[j], boundaries[j + 2]));
            }

            while (true)
            {
                int best = -1;
                for (int j = 0; j < pairResiduals.Count; j++)
                {
                    if (pairResiduals[j] <= lineResidualMeters && (best < 0 || pairResiduals[j] < pairResiduals[best]))
                    {
                        best = j;
                    }
                }

                if (best < 0)
                {
                    return;
                }

                boundaries.RemoveAt(best + 1);
                pairResiduals.RemoveAt(best);
                if (best > 0)
                {
                    pairResiduals[best - 1] = LineResidual(points, boundaries[best - 1], boundaries[best + 1]);
                }

                if (best < pairResiduals.Count)
                {
                    pairResiduals[best] = LineResidual(points, boundaries[best], boundaries[best + 2]);
                }
            }
        }

        /// <summary>
        /// The crossing of two adjacent fitted lines (the midpoint of their closest points). Falls back to the
        /// shared sample when the lines are parallel or the crossing lies farther along either line than that
        /// segment's length.
        /// </summary>
        private static Vec3 CornerVertex(FittedLine incoming, double incomingLength, FittedLine outgoing, double outgoingLength, Vec3 sharedSample)
        {
            double cosine = Vec3.Dot(incoming.Direction, outgoing.Direction);
            double sineSquared = Vec3.Cross(incoming.Direction, outgoing.Direction).LengthSquared;
            if (!(sineSquared > 0.0))
            {
                return sharedSample;
            }

            Vec3 offset = incoming.Point - outgoing.Point;
            double d = Vec3.Dot(incoming.Direction, offset);
            double e = Vec3.Dot(outgoing.Direction, offset);
            double s = (cosine * e - d) / sineSquared;
            double t = (e - cosine * d) / sineSquared;
            double sharedS = Vec3.Dot(sharedSample - incoming.Point, incoming.Direction);
            double sharedT = Vec3.Dot(sharedSample - outgoing.Point, outgoing.Direction);
            if (!(Math.Abs(s - sharedS) <= incomingLength) || !(Math.Abs(t - sharedT) <= outgoingLength))
            {
                return sharedSample;
            }

            return (incoming.At(s) + outgoing.At(t)) * 0.5;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Vec3 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
        }

        private readonly struct Axes
        {
            public Axes(Vec3 centroid, Vec3 primary, Vec3 secondary, Vec3 normal)
            {
                Centroid = centroid;
                Primary = primary;
                Secondary = secondary;
                Normal = normal;
            }

            public Vec3 Centroid { get; }

            public Vec3 Primary { get; }

            public Vec3 Secondary { get; }

            public Vec3 Normal { get; }
        }

        private readonly struct FittedLine
        {
            public FittedLine(Vec3 point, Vec3 direction)
            {
                Point = point;
                Direction = direction;
            }

            public Vec3 Point { get; }

            /// <summary>Unit direction.</summary>
            public Vec3 Direction { get; }

            public Vec3 At(double parameter)
            {
                return Point + Direction * parameter;
            }

            public Vec3 Project(Vec3 p)
            {
                return At(Vec3.Dot(p - Point, Direction));
            }

            public double DistanceTo(Vec3 p)
            {
                return (p - Project(p)).Length;
            }
        }
    }
}
