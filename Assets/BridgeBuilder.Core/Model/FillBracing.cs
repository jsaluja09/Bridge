using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Hidden fill bracing (spec 2.4), pattern Constants.Bracing.PatternId from HG-bracing (ported from bridge-core):
    /// triangulate the fill with its own loop nodes, shortest valid diagonal first. A simple loop of n nodes gets
    /// n - 3 braces and a triangle gets none. A diagonal that passes another loop node, crosses a loop edge or a
    /// kept brace, or lies outside the loop is dropped. Braces are free (no budget) and create no nodes.
    /// </summary>
    public static class FillBracing
    {
        /// <summary>
        /// The braces for a loop (node-id pairs, lower id first), in the order the pattern keeps them. Pairs in
        /// <paramref name="joined"/> are already members and are not braced again. Throws if the loop crosses itself.
        /// </summary>
        public static List<(int a, int b)> Plan(IReadOnlyList<int> loop, IReadOnlyList<Vec3> positions, ISet<(int, int)> joined = null)
        {
            Vec3 normal = FillPlanarity.Normal(positions);
            Geometry.PlaneBasis(normal, out Vec3 u, out Vec3 v);
            Vec3 origin = positions[0];
            var pts = positions.Select(p => new Vec2(Vec3.Dot(p - origin, u), Vec3.Dot(p - origin, v))).ToArray();
            if (IntersectsItself(pts))
                throw new InvalidOperationException("Fill intersects itself");

            int n = pts.Length;
            var candidates = new List<(int i, int j, double len)>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 2; j < n; j++)
                {
                    if ((i == 0 && j == n - 1) || (joined != null && joined.Contains(Key(loop[i], loop[j]))))
                        continue;
                    double len = Math.Sqrt(Vec2.DistanceSquared(pts[i], pts[j]));
                    candidates.Add((i, j, Math.Round(len / Constants.Bracing.LengthTieToleranceMeters, MidpointRounding.AwayFromZero)));
                }
            }
            candidates.Sort((x, y) => x.len != y.len ? x.len.CompareTo(y.len) : x.i != y.i ? x.i.CompareTo(y.i) : x.j.CompareTo(y.j));

            var kept = new List<(int i, int j)>();
            foreach (var (i, j, _) in candidates)
            {
                if (PassesOtherNode(pts, i, j)
                    || Enumerable.Range(0, n).Any(k => CrossesAwayFromSharedEnd(pts, i, j, k, (k + 1) % n))
                    || kept.Any(b => CrossesAwayFromSharedEnd(pts, i, j, b.i, b.j))
                    || !Contains(pts, new Vec2(0.5 * (pts[i].X + pts[j].X), 0.5 * (pts[i].Y + pts[j].Y))))
                    continue;
                kept.Add((i, j));
            }
            return kept.Select(b => Key(loop[b.i], loop[b.j])).ToList();
        }

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>
        /// Adds the hidden braces for a fill already in the model. Throws if the fill is not planar or crosses
        /// itself (members unchanged). Skips pairs already joined by a member and pairs of two fixed nodes.
        /// </summary>
        public static List<int> Apply(BridgeModel model, int fillId, int sectionId)
        {
            var fill = model.GetFill(fillId) ?? throw new ArgumentException("Unknown fill");
            var positions = fill.Loop.Select(id => model.GetNode(id).Position).ToList();
            if (!FillPlanarity.IsPlanar(positions))
                throw new InvalidOperationException("Fill is not planar");

            var joined = new HashSet<(int, int)>(model.Members.Select(m => Key(m.NodeA, m.NodeB)));
            var added = new List<int>();
            foreach (var (a, b) in Plan(fill.Loop, positions, joined))
            {
                if (model.GetNode(a).Fixed && model.GetNode(b).Fixed)
                    continue;
                var result = model.TryAddHiddenBrace(a, b, sectionId);
                if (result.Ok)
                    added.Add(result.Id);
            }
            return added;
        }

        static bool IntersectsItself(Vec2[] p)
        {
            int n = p.Length;
            for (int e = 0; e < n; e++)
                for (int f = e + 2; f < n; f++)
                    if (!(e == 0 && f == n - 1) && SegmentsMeet(p[e], p[e + 1], p[f], p[(f + 1) % n]))
                        return true;
            return false;
        }

        static bool PassesOtherNode(Vec2[] p, int i, int j)
        {
            for (int k = 0; k < p.Length; k++)
                if (k != i && k != j && DistanceToSegment(p[k], p[i], p[j]) <= Constants.Bracing.VertexClearanceMeters)
                    return true;
            return false;
        }

        /// <summary>True when segment (i, j) and segment (k, l) meet anywhere other than an end node they share.</summary>
        static bool CrossesAwayFromSharedEnd(Vec2[] p, int i, int j, int k, int l)
        {
            bool sharesI = i == k || i == l, sharesJ = j == k || j == l;
            if (!sharesI && !sharesJ)
                return SegmentsMeet(p[i], p[j], p[k], p[l]);
            // Two segments from one node meet again only when they overlap, which puts a far end on the other segment.
            int farOfFirst = sharesI ? j : i;
            int farOfSecond = (k == i || k == j) ? l : k;
            double c = Constants.Bracing.VertexClearanceMeters;
            return DistanceToSegment(p[farOfFirst], p[k], p[l]) <= c || DistanceToSegment(p[farOfSecond], p[i], p[j]) <= c;
        }

        /// <summary>True when the segments cross, or come within Constants.Bracing.VertexClearanceMeters of each other.</summary>
        static bool SegmentsMeet(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            if (Straddles(Orient(a, b, c), Orient(a, b, d)) && Straddles(Orient(c, d, a), Orient(c, d, b)))
                return true;
            double t = Constants.Bracing.VertexClearanceMeters;
            return DistanceToSegment(a, c, d) <= t || DistanceToSegment(b, c, d) <= t
                || DistanceToSegment(c, a, b) <= t || DistanceToSegment(d, a, b) <= t;
        }

        static bool Straddles(double x, double y) => (x > 0 && y < 0) || (x < 0 && y > 0);

        static double Orient(Vec2 a, Vec2 b, Vec2 c) => Vec2.Cross(b - a, c - a);

        static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2)) : 0;
            return Math.Sqrt(Vec2.DistanceSquared(p, new Vec2(a.X + t * dx, a.Y + t * dy)));
        }

        /// <summary>Even-odd test, so it does not depend on the loop's winding.</summary>
        static bool Contains(Vec2[] poly, Vec2 q)
        {
            bool inside = false;
            for (int k = 0, prev = poly.Length - 1; k < poly.Length; prev = k++)
            {
                Vec2 a = poly[k], b = poly[prev];
                if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < a.X + (q.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                    inside = !inside;
            }
            return inside;
        }
    }
}
