using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Divides lines into the nearest whole number of equal segments near the target spacing (spec 2.3),
    /// which avoids tiny runt segments. Never splits an existing member: it only produces new points.
    /// </summary>
    public static class EqualSegments
    {
        public static int Count(double length) => Count(length, Constants.Snapping.TargetSpacingMeters);

        /// <summary>Round(length / spacing), half away from zero, at least 1. Decimal avoids 0.15/0.10 = 1.4999...</summary>
        public static int Count(double length, double spacing)
        {
            if (!(length > 0))
                throw new ArgumentOutOfRangeException(nameof(length));
            if (!(spacing > 0))
                throw new ArgumentOutOfRangeException(nameof(spacing));
            int n = (int)Math.Round((decimal)length / (decimal)spacing, MidpointRounding.AwayFromZero);
            return Math.Max(1, n);
        }

        /// <summary>Points from a to b inclusive, equally spaced; the endpoints are exact.</summary>
        public static Vec3[] Divide(Vec3 a, Vec3 b, double spacing)
        {
            int n = Count(Vec3.Distance(a, b), spacing);
            var points = new Vec3[n + 1];
            points[0] = a;
            points[n] = b;
            for (int i = 1; i < n; i++)
                points[i] = Vec3.Lerp(a, b, i / (double)n);
            return points;
        }

        public static Vec3[] Divide(Vec3 a, Vec3 b) => Divide(a, b, Constants.Snapping.TargetSpacingMeters);

        /// <summary>
        /// Divides each straight piece of a polyline. The first vertex must be an existing node (spec 2.3:
        /// "attaching a new line means starting it from an existing node"). Returns every point, vertices exact.
        /// </summary>
        public static List<Vec3> SegmentPolyline(IReadOnlyList<Vec3> vertices, IReadOnlyList<Vec3> existingNodes, double spacing)
        {
            if (vertices.Count < 2)
                throw new ArgumentException("A polyline needs two vertices");
            bool starts = false;
            foreach (var n in existingNodes)
            {
                if (Vec3.Distance(n, vertices[0]) <= 1e-9)
                {
                    starts = true;
                    break;
                }
            }
            if (!starts)
                throw new ArgumentException("A line must start at an existing node");

            var points = new List<Vec3> { vertices[0] };
            for (int v = 0; v + 1 < vertices.Count; v++)
            {
                var piece = Divide(vertices[v], vertices[v + 1], spacing);
                for (int i = 1; i < piece.Length; i++)
                    points.Add(piece[i]);
            }
            return points;
        }
    }
}
