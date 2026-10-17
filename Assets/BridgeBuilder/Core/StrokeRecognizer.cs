using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>A stroke turned into a line: node positions, with existing node ids where it snapped (-1 = new).</summary>
    public sealed class RecognizedStroke
    {
        public StrokeShape Shape;
        public readonly List<Vector3> Points = new List<Vector3>();
        public readonly List<int> ExistingNodes = new List<int>();
    }

    /// <summary>
    /// Turns a freehand 3D stroke into a line, an arc, or a polyline (spec 2.1), then divides it into
    /// equal segments near the snap spacing (spec 2.3). The start is always an existing node; the end
    /// and polyline corners snap to nearby nodes.
    /// </summary>
    public static class StrokeRecognizer
    {
        public static bool TryRecognize(BridgeDesign design, int startNode, IReadOnlyList<Vector3> raw,
                                        out RecognizedStroke result, out string error)
        {
            var s = design.Settings;
            result = null;
            error = null;

            Vector3 start = design.GetNode(startNode).Position;
            var input = new List<Vector3>(raw.Count + 1) { start };
            input.AddRange(raw);
            var pts = Resample(input, 0.01f);
            if (PathLength(pts) < s.MinLineLength)
            {
                error = "Too short";
                return false;
            }

            int endNode = design.FindNode(pts[pts.Count - 1], s.NodeSnapRadius, startNode);
            Vector3 end = endNode >= 0 ? design.GetNode(endNode).Position : pts[pts.Count - 1];
            pts[pts.Count - 1] = end;
            float chord = Vector3.Distance(start, end);

            List<Vector3> vertices;
            StrokeShape shape;
            ArcFit arc = default;

            if (chord >= s.MinLineLength && MaxDeviationFromSegment(pts, start, end) <= Math.Max(s.LineTolerance, s.LineToleranceRelative * chord))
            {
                shape = StrokeShape.Line;
                vertices = new List<Vector3> { start, end };
            }
            else
            {
                bool arcOk = TryFitArc(pts, start, end, s, out arc);
                var corners = Simplify(pts, s.PolylineTolerance);
                bool polyOk = corners.Count - 2 <= s.MaxPolylineCorners && corners.Count >= 3 && MinSegment(corners) >= s.MinLineLength * 0.8f;

                if (arcOk && (!polyOk || corners.Count - 2 >= 2 || arc.MaxError < s.PolylineTolerance * 0.5f))
                {
                    shape = StrokeShape.Arc;
                    vertices = null;
                }
                else if (polyOk)
                {
                    shape = StrokeShape.Polyline;
                    vertices = corners;
                }
                else
                {
                    error = "Couldn't read that shape. Draw a straight line, an arc, or straight segments.";
                    return false;
                }
            }

            result = new RecognizedStroke { Shape = shape };
            if (shape == StrokeShape.Arc)
                SegmentArc(arc, start, s.SnapSpacing, result);
            else
                SegmentPolyline(design, vertices, s, result);

            result.ExistingNodes[0] = startNode;
            result.Points[result.Points.Count - 1] = end;
            result.ExistingNodes[result.ExistingNodes.Count - 1] = endNode;

            for (int i = 0; i + 1 < result.Points.Count; i++)
            {
                if (Vector3.Distance(result.Points[i], result.Points[i + 1]) < 0.01f)
                {
                    error = "Too short";
                    return false;
                }
            }
            if (endNode == startNode)
            {
                error = "Line ends where it started";
                return false;
            }
            return true;
        }

        // ---------- Fitting ----------

        struct ArcFit
        {
            public Vector3 Center;
            public Vector3 Normal;
            public float Radius;
            public float Sweep;       // radians, signed about Normal
            public float MaxError;
        }

        static bool TryFitArc(List<Vector3> pts, Vector3 a, Vector3 b, BridgeSettings s, out ArcFit fit)
        {
            fit = default;
            Geometry.FitPlane(pts, out _, out Vector3 normal, out float planeError);
            float chord = Vector3.Distance(a, b);
            if (planeError > Math.Max(s.PlaneTolerance, 0.05f * PathLength(pts)) || chord < s.MinLineLength)
                return false;

            // The center lies on the chord's perpendicular bisector within the plane. Each point gives
            // one estimate of where along it; the median is robust to hand wobble.
            Vector3 m = (a + b) * 0.5f;
            Vector3 d = Vector3.Normalize(Vector3.Cross(normal, b - a));
            var estimates = new List<float>();
            foreach (var p in pts)
            {
                float den = 2f * Vector3.Dot(d, p - a);
                if (Math.Abs(den) < 1e-4f)
                    continue;
                estimates.Add(((p - m).LengthSquared() - (a - m).LengthSquared()) / den);
            }
            if (estimates.Count < 3)
                return false;
            estimates.Sort();
            float t = estimates[estimates.Count / 2];
            Vector3 c = m + d * t;
            float r = Vector3.Distance(c, a);

            float maxErr = 0f;
            foreach (var p in pts)
            {
                Vector3 q = p - Vector3.Dot(p - c, normal) * normal;   // into the plane
                maxErr = Math.Max(maxErr, Math.Abs(Vector3.Distance(q, c) - r));
            }

            // Sweep: accumulate the signed angle along the stroke.
            float traced = 0f;
            for (int i = 1; i < pts.Count; i++)
                traced += SignedAngle(pts[i - 1] - c, pts[i] - c, normal);

            // Exact angle from start to end, on the side the stroke actually went.
            float sweep = SignedAngle(a - c, b - c, normal);
            if (traced > 0f && sweep < 0f)
                sweep += 2f * MathF.PI;
            else if (traced < 0f && sweep > 0f)
                sweep -= 2f * MathF.PI;

            fit = new ArcFit { Center = c, Normal = normal, Radius = r, Sweep = sweep, MaxError = maxErr };
            float sweepDeg = Math.Abs(sweep) * 180f / MathF.PI;
            return maxErr <= Math.Max(s.ArcTolerance, 0.06f * r) && sweepDeg >= s.MinArcAngleDeg && sweepDeg <= 300f;
        }

        static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            Vector3 f = from - Vector3.Dot(from, axis) * axis;
            Vector3 t = to - Vector3.Dot(to, axis) * axis;
            return MathF.Atan2(Vector3.Dot(Vector3.Cross(f, t), axis), Vector3.Dot(f, t));
        }

        // ---------- Segmenting ----------

        static void SegmentArc(ArcFit arc, Vector3 start, float spacing, RecognizedStroke result)
        {
            float length = arc.Radius * Math.Abs(arc.Sweep);
            int n = Math.Max(2, (int)MathF.Round(length / spacing));
            Vector3 r0 = start - arc.Center;
            for (int i = 0; i <= n; i++)
            {
                float angle = arc.Sweep * i / n;
                var q = Quaternion.CreateFromAxisAngle(arc.Normal, angle);
                result.Points.Add(arc.Center + Vector3.Transform(r0, q));
                result.ExistingNodes.Add(-1);
            }
        }

        static void SegmentPolyline(BridgeDesign design, List<Vector3> vertices, BridgeSettings s, RecognizedStroke result)
        {
            // Snap interior corners to nearby nodes, so W shapes can land on existing joints.
            var snapped = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                snapped[i] = -1;
                if (i > 0 && i < vertices.Count - 1)
                {
                    int node = design.FindNode(vertices[i], s.NodeSnapRadius);
                    if (node >= 0)
                    {
                        snapped[i] = node;
                        vertices[i] = design.GetNode(node).Position;
                    }
                }
            }

            result.Points.Add(vertices[0]);
            result.ExistingNodes.Add(-1);
            for (int v = 0; v + 1 < vertices.Count; v++)
            {
                Vector3 a = vertices[v], b = vertices[v + 1];
                int n = Math.Max(1, (int)MathF.Round(Vector3.Distance(a, b) / s.SnapSpacing));
                for (int i = 1; i <= n; i++)
                {
                    result.Points.Add(Vector3.Lerp(a, b, i / (float)n));
                    result.ExistingNodes.Add(i == n ? snapped[v + 1] : -1);
                }
            }
        }

        // ---------- Helpers ----------

        public static List<Vector3> Resample(IReadOnlyList<Vector3> pts, float step)
        {
            var output = new List<Vector3> { pts[0] };
            float carried = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float seg = Vector3.Distance(a, b);
                if (seg < 1e-6f)
                    continue;
                float t = step - carried;
                while (t <= seg)
                {
                    output.Add(Vector3.Lerp(a, b, t / seg));
                    t += step;
                }
                carried = seg - (t - step);
            }
            if (Vector3.Distance(output[output.Count - 1], pts[pts.Count - 1]) > step * 0.25f)
                output.Add(pts[pts.Count - 1]);
            else
                output[output.Count - 1] = pts[pts.Count - 1];
            return output;
        }

        static float PathLength(List<Vector3> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++)
                len += Vector3.Distance(pts[i - 1], pts[i]);
            return len;
        }

        static float MaxDeviationFromSegment(List<Vector3> pts, Vector3 a, Vector3 b)
        {
            float max = 0f;
            foreach (var p in pts)
                max = Math.Max(max, Geometry.DistanceToSegment(p, a, b));
            return max;
        }

        static float MinSegment(List<Vector3> pts)
        {
            float min = float.MaxValue;
            for (int i = 1; i < pts.Count; i++)
                min = Math.Min(min, Vector3.Distance(pts[i - 1], pts[i]));
            return min;
        }

        /// <summary>Ramer-Douglas-Peucker: the corners of the stroke, keeping both ends.</summary>
        static List<Vector3> Simplify(List<Vector3> pts, float epsilon)
        {
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                float max = 0f;
                int index = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float d = Geometry.DistanceToSegment(pts[i], pts[first], pts[last]);
                    if (d > max)
                    {
                        max = d;
                        index = i;
                    }
                }
                if (index >= 0 && max > epsilon)
                {
                    keep[index] = true;
                    stack.Push((first, index));
                    stack.Push((index, last));
                }
            }
            var result = new List<Vector3>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (keep[i])
                    result.Add(pts[i]);
            }
            return result;
        }
    }
}
